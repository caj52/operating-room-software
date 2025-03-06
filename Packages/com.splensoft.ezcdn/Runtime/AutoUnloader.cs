using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplenSoft.AssetBundles
{
    public class AutoUnloader : MonoBehaviour
    {
        protected static Dictionary<string, List<AutoUnloader>>
        _instances = new Dictionary<string, List<AutoUnloader>>();

        [field: SerializeField, HideInInspector]
        public string AssetBundleName { get; set; }

        protected List<string> _dependencies;

        protected bool _isDestroyed;
        private bool _initialized;
        private bool _isInitializing;

        public async void Initialize()
        {
            if (_initialized || _isInitializing) return;

            _isInitializing = true;

            if (_dependencies == null)
            {
                var task = AssetBundleManager
                    .GetDependencies(AssetBundleName);

                await task;

                if (!Application.isPlaying)
                    throw new Exception("App quit during task");

                if (_isDestroyed)
                    return;

                _dependencies = task.Result;
            }
            
            RegisterInstance(AssetBundleName);

            foreach (var dependency in _dependencies)
            {
                RegisterInstance(dependency);
            }

            _initialized = true;
            _isInitializing = false;
        }

        public void UnregisterAndFlushAll()
        {
            UnregisterInstance(AssetBundleName);

            foreach (var dependency in _dependencies)
            {
                UnregisterInstance(dependency);
            }

            TryFlushUnusedAssetBundle(AssetBundleName);

            foreach (var dependency in _dependencies)
            {
                TryFlushUnusedAssetBundle(dependency);
            }

            _initialized = false;
        }

        private void UnregisterInstance(string assetBundleName)
        {
            _instances[assetBundleName].Remove(this);
        }

        private void TryFlushUnusedAssetBundle(string assetBundleName)
        {
            if (_instances[assetBundleName].Count == 0 &&
            AssetBundleManager.TryGetAssetBundleData
            (assetBundleName, out var data))
            {
                data.Flush();
            }
        }

        private void RegisterInstance(string assetBundleName)
        {
            if (!_instances.ContainsKey(assetBundleName))
            {
                _instances[assetBundleName] = new List<AutoUnloader>();
            }

            if (!_instances[assetBundleName].Contains(this))
            {
                _instances[assetBundleName].Add(this);
            }
        }
    }
}

