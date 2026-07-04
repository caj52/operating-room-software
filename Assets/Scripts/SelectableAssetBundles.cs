using SplenSoft.AssetBundles;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Tracks and handles all assets that will appear 
/// in the <see cref="ObjectMenu"/>. Automatically 
/// populates before an asset bundle build. Should 
/// only be used to get asset bundles by name 
/// (or save/load GUID). Should NOT be used to 
/// get up-to-date metadata. Use 
/// <see cref="ObjectMenu"/> instead
/// </summary>
[ManagedAsset]
[CreateAssetMenu(
    fileName = "SelectableAssetBundles",
    menuName = "ScriptableObjects/SelectableAssetBundles",
    order = 1)]
public class SelectableAssetBundles : ScriptableObject, IPreprocessAssetBundle
{
    private const string CatalogAssetPath = "Assets/SelectableAssetBundles.asset";
    private const string CatalogBundleName = "selectableassetbundles_837398f6c50183d4f80b5c0c2f0daa33";

    public static bool Initialized { get; private set; }

    private static List<SelectableData> SelectableData { get; } = new();

    public static IReadOnlyList<SelectableData> 
        GetSelectableData() => SelectableData.AsReadOnly();

    [field: SerializeField]
    private List<string> RelativePaths { get; set; } = new();

    [SerializeField]
    private List<SelectableData> _selectableData = new();

    [RuntimeInitializeOnLoadMethod]
    private static void OnAppStart()
    {
        GetDatas();
    }

    /// <summary>
    /// Populates <see cref="SelectableData"/> from the local catalog asset. 
    /// Runs on app start. 
    /// Track <see cref="Initialized"/> to know when it's finished
    /// </summary>
    private static void GetDatas()
    {
        Debug.Log("Getting SelectableAssetBundles datas");
        AssetPipelineDiagnostics.Log("Catalog", "GetDatas started — loading local catalog");
        var loadingToken = Loading.GetLoadingToken();

        if (!TryLoadLocalCatalog(out SelectableAssetBundles catalog))
        {
            Debug.LogError("Failed to load local SelectableAssetBundles catalog");
            AssetPipelineDiagnostics.Log("Catalog", "Failed — local catalog not found");
            loadingToken.Done();
            return;
        }

        SelectableData.AddRange(catalog._selectableData);
        Initialized = true;
        loadingToken.Done();
        Debug.Log("SelectableAssetBundles initialized");
        AssetPipelineDiagnostics.Log("Catalog", $"Initialized — {SelectableData.Count} SelectableData entries cached from local catalog");
    }

    private static bool TryLoadLocalCatalog(out SelectableAssetBundles catalog)
    {
        catalog = null;

#if UNITY_EDITOR
        catalog = AssetDatabase.LoadAssetAtPath<SelectableAssetBundles>(CatalogAssetPath);
        if (catalog != null)
        {
            AssetPipelineDiagnostics.Log("Catalog", $"Loaded from {CatalogAssetPath}");
            return true;
        }
#endif

        foreach (string path in GetLocalCatalogBundlePaths())
        {
            if (!File.Exists(path))
                continue;

            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
                continue;

            catalog = bundle.LoadAsset<SelectableAssetBundles>("SelectableAssetBundles");
            if (catalog == null)
                catalog = bundle.LoadAllAssets<SelectableAssetBundles>().FirstOrDefault();

            if (catalog != null)
            {
                AssetPipelineDiagnostics.Log("Catalog", $"Loaded from bundle {path}");
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetLocalCatalogBundlePaths()
    {
        yield return Path.Combine(Application.streamingAssetsPath, "AssetBundles", CatalogBundleName);

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (!string.IsNullOrEmpty(projectRoot))
            yield return Path.Combine(projectRoot, "TestData", "CdnMirror", CatalogBundleName);
    }

    /// <param name="query">Can be SaveLoadGuid or AssetBundleName</param>
    public static bool TryGetSelectableData(string query, out SelectableData data)
    {
        data = SelectableData
            .FirstOrDefault(x =>
                string.Compare(x.SaveLoadGuid, query, true) == 0 ||
                string.Compare(x.AssetBundleName, query, true) == 0);

        if (data == default)
            AssetPipelineDiagnostics.Log("Catalog.Lookup", $"MISS query='{query}' (catalog has {SelectableData.Count} entries, initialized={Initialized})");

        return data != default;
    }

    public void OnPreprocessAssetBundle()
    {
#if UNITY_EDITOR
        Debug.Log("Processing SelectableAssetBundles ScriptableObject");

        _selectableData.Clear();

        var folders = new string[] { "Assets/Prefabs/Selectables" };
        string[] assetGuids = AssetDatabase.FindAssets("t: prefab", folders);

        foreach (string guid in assetGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            var go = (GameObject)AssetDatabase
                .LoadAssetAtPath(path, typeof(GameObject));

            
            if (!go.TryGetComponent<Selectable>(out var selectable))
                continue;

            if (!AssetBundleManager.TryGetAssetBundleName
                (go, out string assetBundleName))
                continue;

            var data = new SelectableData
            {
                AssetBundleName = assetBundleName,
                SaveLoadGuid = selectable.GUID,
                PrefabName = go.name,
                MetaData = selectable.MetaData,
            };

            _selectableData.Add(data);
        }
#endif

    }
}