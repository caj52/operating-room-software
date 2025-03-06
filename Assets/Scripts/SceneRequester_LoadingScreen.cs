using SplenSoft.AssetBundles;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Helper to hook up SplenSoft's asset 
/// bundle manager to ORS's loading system
/// </summary>
[RequireComponent(typeof(SceneRequester))]
public class SceneRequester_LoadingScreen : MonoBehaviour
{
    private SceneRequester _sceneRequester;
    private Loading.LoadingToken _loadingToken;

    private void Awake()
    {
        _sceneRequester = GetComponent<SceneRequester>();

        AssetBundleManager
            .SceneAssetRetrievalStarted
            .AddListener(SceneRetrievalStarted);

        AssetBundleManager
            .SceneAssetLoaded
            .AddListener(SceneAssetLoaded);

        _sceneRequester.OnProgressUpdated
            .AddListener(OnProgressUpdated);
    }


    private void OnDestroy()
    {
        AssetBundleManager
            .SceneAssetRetrievalStarted
            .RemoveListener(SceneRetrievalStarted);

        AssetBundleManager
               .SceneAssetLoaded
               .RemoveListener(SceneAssetLoaded);

        _sceneRequester.OnProgressUpdated
            .RemoveListener(OnProgressUpdated);
    }

    private void SceneAssetLoaded(string assetBundleName)
    {
        if (assetBundleName == _sceneRequester.AssetBundleName)
        {
            _loadingToken.Done();
            Destroy(gameObject);
        }
    }

    private void SceneRetrievalStarted(string assetBundleName)
    {
        if (assetBundleName == _sceneRequester.AssetBundleName)
        {
            DontDestroyOnLoad(gameObject);
            _loadingToken = Loading.GetLoadingToken();
        }
    }

    private void OnProgressUpdated(float progress)
    {
        _loadingToken.SetProgress(Mathf.Min(progress, 0.95f));
    }
}