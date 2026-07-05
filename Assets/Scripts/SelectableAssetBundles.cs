using SplenSoft.AssetBundles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

    /// <summary>True after a successful CDN catalog merge (player builds).</summary>
    public static bool CdnCatalogMerged { get; private set; }

    /// <summary>Fired when CDN catalog entries are merged (player builds).</summary>
    public static event Action CatalogUpdated;

    private static Task _cdnRefreshTask;
    private static readonly object _cdnLock = new();

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
    /// Player: local catalog seed for fast room load, then merge online catalog for the menu.
    /// Prefab bundles stay local-first via <see cref="AssetBundleManager"/>, CDN on click when not shipped.
    /// </summary>
    private static async void GetDatas()
    {
        Debug.Log("Getting SelectableAssetBundles datas");
        AssetPipelineDiagnostics.Log("Catalog", "GetDatas started");
        var loadingToken = Loading.GetLoadingToken();

        if (TryLoadLocalCatalog(out SelectableAssetBundles localCatalog))
        {
            SelectableData.AddRange(localCatalog._selectableData);
            AssetPipelineDiagnostics.Log("Catalog", $"Local seed — {localCatalog._selectableData.Count} entries");
        }

        // Room load + menu can proceed on local catalog; CDN refresh adds online entries afterward.
        Initialized = true;
        loadingToken.Done();
        AssetPipelineDiagnostics.Log("Catalog", $"Initialized — local ready ({SelectableData.Count} entries)");

        if (ShouldUseLocalCatalogOnly())
            return;

        lock (_cdnLock)
        {
            _cdnRefreshTask ??= RefreshCdnCatalogAsync();
        }

        await _cdnRefreshTask;
        Debug.Log("SelectableAssetBundles CDN refresh finished");
    }

    /// <summary>
    /// Room load may call this when a GUID is missing from the local seed catalog.
    /// </summary>
    public static async Task EnsureCdnCatalogMerged(int timeoutMs = 120_000)
    {
        if (CdnCatalogMerged || ShouldUseLocalCatalogOnly())
            return;

        Task refreshTask;
        lock (_cdnLock)
        {
            refreshTask = _cdnRefreshTask ??= RefreshCdnCatalogAsync();
        }

        var timeoutTask = Task.Delay(timeoutMs);
        if (await Task.WhenAny(refreshTask, timeoutTask) == timeoutTask)
            AssetPipelineDiagnostics.Log("Catalog", "EnsureCdnCatalogMerged timed out waiting for CDN catalog");
    }

    private static async Task RefreshCdnCatalogAsync()
    {
        int beforeCdn = SelectableData.Count;
        var loadingToken = Loading.GetLoadingToken();

        try
        {
            bool cdnMerged = await TryMergeCdnCatalogAsync(loadingToken);
            if (!cdnMerged)
            {
                AssetPipelineDiagnostics.Log("Catalog",
                    $"CDN unavailable — staying on local catalog ({SelectableData.Count} entries)");
                return;
            }

            CdnCatalogMerged = true;
            AssetPipelineDiagnostics.Log("Catalog",
                $"CDN merged — {SelectableData.Count} entries (+{SelectableData.Count - beforeCdn} from online)");
            CatalogUpdated?.Invoke();
        }
        finally
        {
            loadingToken.Done();
        }
    }

    private static bool ShouldUseLocalCatalogOnly()
    {
#if UNITY_EDITOR
        return AssetBundleManagerSettings.Get().UseEditorAssetsIfAble;
#else
        return false;
#endif
    }

    private static async Task<bool> TryMergeCdnCatalogAsync(Loading.LoadingToken loadingToken)
    {
        const int maxWaitMs = 60_000;
        int waitedMs = 0;
        while (!AssetBundleManager.Initialized)
        {
            await Task.Yield();
            if (!Application.isPlaying)
                return false;

            waitedMs += 16;
            if (waitedMs >= maxWaitMs)
            {
                AssetPipelineDiagnostics.Log("Catalog", "CDN catalog — timed out waiting for AssetBundleManager");
                return false;
            }
        }

        var namesTask = AssetBundleManager.GetAssetBundleNames(typeof(SelectableAssetBundles));
        await namesTask;
        if (!Application.isPlaying)
            return false;

        string[] bundleNames = namesTask.Result;
        if (bundleNames == null || bundleNames.Length == 0)
        {
            AssetPipelineDiagnostics.Log("Catalog", "CDN catalog — no SelectableAssetBundles bundles in manifest");
            return false;
        }

        AssetPipelineDiagnostics.Log("Catalog", $"CDN catalog — fetching {bundleNames.Length} bundle(s) from CDN (not local copy)");

        var tasks = new List<Task<SelectableAssetBundles>>();
        var progresses = new float[bundleNames.Length];

        for (int i = 0; i < bundleNames.Length; i++)
        {
            string assetBundleName = bundleNames[i];
            var progress = new Progress<AssetRetrievalProgress>();
            int index = i;
            progress.ProgressChanged += (_, p) =>
            {
                progresses[index] = p.Progress;
                loadingToken.SetProgress(progresses.Sum() / bundleNames.Length);
            };

            // Online catalog is source of truth for the menu; local seed stays for fast room load.
            tasks.Add(AssetBundleManager.GetAsset<SelectableAssetBundles>(
                assetBundleName, progress, allowLocalFallback: false));
        }

        while (tasks.Any(x => !x.IsCompleted))
        {
            await Task.Yield();
            if (!Application.isPlaying)
                return false;
        }

        int merged = 0;
        foreach (Task<SelectableAssetBundles> task in tasks)
        {
            if (task.Result == null)
                continue;

            merged += MergeCatalogEntries(task.Result._selectableData);
        }

        AssetPipelineDiagnostics.Log("Catalog", $"CDN catalog — merged {merged} entry update(s), total {SelectableData.Count}");
        return merged > 0 || tasks.Any(t => t.Result != null);
    }

    private static int MergeCatalogEntries(IEnumerable<SelectableData> entries)
    {
        int changes = 0;
        foreach (SelectableData entry in entries)
        {
            if (entry == null)
                continue;

            int idx = FindEntryIndex(entry);
            if (idx >= 0)
            {
                SelectableData[idx] = entry;
            }
            else
            {
                SelectableData.Add(entry);
            }

            changes++;
        }

        return changes;
    }

    private static int FindEntryIndex(SelectableData entry)
    {
        if (!string.IsNullOrEmpty(entry.SaveLoadGuid))
        {
            int byGuid = SelectableData.FindIndex(x =>
                string.Equals(x.SaveLoadGuid, entry.SaveLoadGuid, StringComparison.OrdinalIgnoreCase));
            if (byGuid >= 0)
                return byGuid;
        }

        return SelectableData.FindIndex(x =>
            string.Equals(x.AssetBundleName, entry.AssetBundleName, StringComparison.OrdinalIgnoreCase));
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
