using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Designed to pair with <see cref="PrefabAutoUnloader"/> to handle 
    /// automatic unloading of unused asset bundles. Should only be one in the entire project
    /// </summary>
    [ManagedAsset]
    public class SceneAutoUnloader : ScriptableObject, IPreprocessAssetBundle
    {
        private static List<SceneAssetBundleName> 
        _sceneAssetBundleNames = new List<SceneAssetBundleName>();

        [field: SerializeField] 
        private List<SceneAssetBundleName> SceneAssetBundleNames 
        { get; set; }

        [RuntimeInitializeOnLoadMethod]
        private async static void OnAppStart()
        {
            var task = AssetBundleManager
                .GetAllAssetsOfType<SceneAutoUnloader>();

            await task;

            if (!Application.isPlaying)
                throw new Exception("App quit during task");

            if (task.Result.Count == 0)
                return;

            _sceneAssetBundleNames = task.Result[0].SceneAssetBundleNames;

            GameObject master = new GameObject("Scene Auto Unloaders");
            DontDestroyOnLoad(master);
            foreach (var item in _sceneAssetBundleNames)
            {
                var newObj = new GameObject($"Scene Auto Unloader - {item.SceneName}");
                newObj.transform.parent = master.transform;
                var newComp = newObj.AddComponent<AutoUnloader>();
                newComp.AssetBundleName = item.AssetBundleName;

                SceneManager.sceneLoaded += (scene, loadMode) =>
                {
                    if (scene.name == item.SceneName)
                    {
                        newComp.Initialize();
                    }
                };

                SceneManager.sceneUnloaded += scene =>
                {
                    if (scene.name == item.SceneName)
                    {
                        newComp.UnregisterAndFlushAll();
                    }
                };

                if (SceneManager.GetActiveScene().name == item.SceneName)
                {
                    newComp.Initialize();
                }
            }
        }

        public void OnPreprocessAssetBundle()
        {
#if UNITY_EDITOR
            SceneAssetBundleNames.Clear();

            string searchString = $"t:{typeof(SceneAsset)}".ToLower();

            var guids = AssetDatabase
                .FindAssets(searchString, new string[] { "Assets" })
                .ToList();

            List<string> objPaths = guids
                .ConvertAll(x => AssetDatabase.GUIDToAssetPath(x));

            foreach (var path in objPaths)
            {
                var asset = AssetDatabase
                    .LoadAssetAtPath<SceneAsset>(path);

                if (AssetBundleManager.TryGetAssetBundleName
                (asset, out var bundleName))
                {
                    SceneAssetBundleNames.Add(
                        new SceneAssetBundleName
                        {
                            AssetBundleName = bundleName,
                            SceneName = asset.name
                        });
                }
            }

            EditorUtility.SetDirty(this);
#endif
        }

        [Serializable]
        private class SceneAssetBundleName
        {
            public string AssetBundleName { get; set; }
            public string SceneName { get; set; }
        }
    }
}