using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Allows EZ-CDN to automatically unload unused asset bundles 
    /// when an asset is destroyed. Tracks dependencies as well.
    /// 
    /// <para />Must be placed on the root object of a Prefab.
    /// <para />
    /// 
    /// This system expects good design principles and will not check
    /// if other asset bundles that depend on this object are
    /// currently being loaded. 
    /// 
    /// <para /> For best results, either put this
    /// component on all of your prefabs or don't use it at all 
    /// (make your own custom asset handling logic). If you
    /// plan to use this system, you should also include
    /// a <see cref="SceneAutoUnloader"/> ScriptableObject in
    /// your assets to handle all non-prefab scene dependencies
    /// </summary>
    [AddComponentMenu("EZ-CDN/Prefab Auto Unloader")]
    public class PrefabAutoUnloader : AutoUnloader
    {
        private void Start()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            UnregisterAndFlushAll();
        }

        public void OnPreprocessAssetBundle()
        {
#if UNITY_EDITOR
            if (AssetBundleManager.TryGetAssetBundleName
            (gameObject, out string assetBundleName))
            {
                if (AssetBundleName != assetBundleName)
                {
                    AssetBundleName = assetBundleName;
                    EditorUtility.SetDirty(gameObject);
                }
            }
            else
            {
                throw new Exception($"EZ-CDN: AutoUnloader could not process asset bundle name for object {gameObject.name}");
            }
#endif
        }
    }
}
