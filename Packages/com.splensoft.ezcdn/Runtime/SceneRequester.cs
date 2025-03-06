using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// <see cref="MonoBehaviour"/> component that can 
    /// request a scene asset from the CDN. Accepts any 
    /// <see cref="UnityEditor.SceneAsset"/> as an asset.
    /// </summary>
    [AddComponentMenu("EZ-CDN/Scene Requester")]
    public class SceneRequester : Requester<UnityEngine.Object>
    {
        [field: SerializeField
#if UNITY_EDITOR
        , AssetBundleReference(typeof(SceneAsset), "Scene")
#endif
        ]
        public override string AssetBundleName { get; set; }

        /// <summary>
        /// UnityEvent listener for the inspector
        /// </summary>
        public void DownloadAndLoadScene()
        {
            DownloadAndLoadSceneAsync();
        }

        /// <summary>
        /// Downloads and loads the scene attached to this component
        /// </summary>
        /// <param name="progress">An optional 
        /// <see cref="Progress"/> object to track 
        /// download/load progress for a loading screen 
        /// / bar</param>
        public async void DownloadAndLoadSceneAsync(
            Progress<AssetRetrievalProgress> progress = null,
            Action onSuccess = null,
            Action<AssetRetrievalResult> onFailure = null,
            bool waitForManagerInit = true
        )
        {
            if (progress == null)
            {
                progress = new Progress<AssetRetrievalProgress>();
            }

            progress.ProgressChanged += ProgressChanged;

            var task = AssetBundleManager.LoadSceneAsssetBundle(
                AssetBundleName, 
                progress,
                onSuccess, 
                onFailure,
                waitForManagerInit
            );

            while (!task.IsCompleted)
            {
                await Task.Yield();
                if (!Application.isPlaying)
                {
                    progress.ProgressChanged -= ProgressChanged;
                    throw new Exception(
                        "Unity player closed while asset retrieval was in progress"
                    );
                }
            }

            InvokeCompletionEvent();
        }
    }
}