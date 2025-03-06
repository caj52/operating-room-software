using SplenSoft.AssetBundles;
using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Used to download <see cref="Selectable"/> prfabs 
/// from the CDN using <see cref="AssetBundleManager"/>
/// </summary>
[Serializable]
public class SelectableData
{ 
    [field: SerializeField]
    public string PrefabName { get; set; }

    [field: SerializeField]
    public string SaveLoadGuid { get; set; }

    [field: SerializeField]
    public string AssetBundleName { get; set; }

    /// <summary>
    /// This field should be considered "seed" or 
    /// backup data. The most recent version should 
    /// always come from the online database (or 
    /// is cached from such)
    /// </summary>
    [field: SerializeField, MetaDataHandler]
    public SelectableMetaData MetaData { get; set; }

    /// <summary>
    /// Downloads and loads a prefab from the CDN
    /// </summary>
    /// <param name="progress"></param>
    /// <param name="loadingToken">Will call 
    /// <see cref="Loading.LoadingToken.Done"/> 
    /// when the task completes</param>
    /// <returns>A <see cref="GameObject"/> which can be instantiated</returns>
    public async Task<GameObject> GetPrefab(
        Progress<AssetRetrievalProgress> progress = null,
        Loading.LoadingToken loadingToken = null
    ){
        loadingToken ??= Loading.GetLoadingToken();

        if (progress == null) 
        {
            progress = new Progress<AssetRetrievalProgress>();
            progress.ProgressChanged += loadingToken.SetProgress;
        }

        var task = AssetBundleManager
            .GetAsset<GameObject>
            (AssetBundleName, progress);

        await task;

        // if app quit while getting asset, cancel
        if (!Application.isPlaying) return null;

        loadingToken.Done();

        return task.Result;
    }
}