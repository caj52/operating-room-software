using SplenSoft.AssetBundles;
using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Populates <see cref="SelectableData"/> from CDN. 
    /// Runs on app start. 
    /// Track <see cref="Initialized"/> to know when it's finished
    /// </summary>
    private static async void GetDatas()
    {
        Debug.Log("Getting SelectableAssetBundles datas");
        var loadingToken = Loading.GetLoadingToken();

        // get all SelectableAssetBundles asset bundles from CDN
        var task = AssetBundleManager.GetAssetBundleNames(typeof(SelectableAssetBundles));
        await task;
        if (!Application.isPlaying) return;

        List<Task<SelectableAssetBundles>> tasks = new();
        var progresses = new float[task.Result.Length];

        for (int i = 0; i < task.Result.Length; i++)
        {
            string assetBundleName = task.Result[i];

            // track download/load progress for loadingToken
            var progress = new Progress<AssetRetrievalProgress>();
            int index = i;
            progress.ProgressChanged += (_, p) =>
            {
                progresses[index] = p.Progress;
                float prog = progresses.Sum() / task.Result.Length;
                loadingToken.SetProgress(prog);
            };

            var assetRetrievalTask = AssetBundleManager
                .GetAsset<SelectableAssetBundles>
                (assetBundleName, progress);

            tasks.Add(assetRetrievalTask);
        }

        // wait for all download/load tasks to complete
        while (tasks.Any(x => !x.IsCompleted)) await Task.Yield();
        if (!Application.isPlaying) return;

        // cache all Selectable Data
        tasks.ForEach(x => SelectableData
                           .AddRange(x.Result._selectableData));

        Initialized = true;
        loadingToken.Done();
        Debug.Log("SelectableAssetBundles initialized");
    }

    /// <param name="query">Can be SaveLoadGuid or AssetBundleName</param>
    public static bool TryGetSelectableData(string query, out SelectableData data)
    {
        data = SelectableData
            .FirstOrDefault(x =>
                string.Compare(x.SaveLoadGuid, query, true) == 0 ||
                string.Compare(x.AssetBundleName, query, true) == 0);

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