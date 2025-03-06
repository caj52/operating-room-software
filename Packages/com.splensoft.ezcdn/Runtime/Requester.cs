using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Base class for asset bundle requesters of various objects or scenes
    /// </summary>
    public abstract class Requester<T> : MonoBehaviour where T : UnityEngine.Object
    {
        public abstract string AssetBundleName { get; set; }

        public T Asset { get; private set; }

        /// <summary>
        /// Fires when retrieval starts. Useful for starting 
        /// up a loading bar.
        /// </summary>
        [field: SerializeField]
        public UnityEvent OnRetrievalStarted 
        { get; private set; } = new UnityEvent();

        /// <summary>
        /// Fires when download / load progress changes. 
        /// Includes a float between 0 and 1 representing
        /// progress
        /// </summary>
        [field: SerializeField]
        public UnityEvent<float> OnProgressUpdated 
        { get; private set; } = new UnityEvent<float>();

        /// <summary>
        /// Fires when an asset retrieval attempt did not work. 
        /// Includes a <see cref="long"/> integer 
        /// <see href="https://developer.mozilla.org/en-US/docs/Web/HTTP/Status"> 
        /// http status code </see>
        /// </summary>
        [field: SerializeField]
        public UnityEvent<long> OnRetrievalFailed 
        { get; private set; } = new UnityEvent<long>();

        /// <summary>
        /// Fires when an asset retrieval attempt succeeded
        /// </summary>
        [field: SerializeField]
        public UnityEvent OnRetrievalSuccess 
        { get; private set; } = new UnityEvent();

        /// <summary>
        /// Gets status of last download attempt for an asset. 
        /// Note: If an asset was pulled from the cache, it 
        /// will return response code 200 with 
        /// <see cref="UnityWebRequest.Result.Success"/>. 
        /// </summary>
        /// <param name="assetRetrievalResult"></param>
        /// <returns>True if at least one attempt was made to 
        /// retrieve the manifest prior to this call</returns>
        public bool TryGetAssetRetrievalResult(
            out AssetRetrievalResult assetRetrievalResult)
        {
            return AssetBundleManager.TryGetAssetRetrievalResult(
                AssetBundleName, out assetRetrievalResult);
        }

        protected void ProgressChanged(object sender, AssetRetrievalProgress e)
        {
            float prog = e.Progress;
            if (e.Status == AssetRetrievalStatus.Done)
            {
                prog = 1;
            }
            OnProgressUpdated?.Invoke(prog);
        }

        protected void InvokeCompletionEvent()
        {
            if (TryGetAssetRetrievalResult(out var result))
            {
                Debug.Log($"Result of request is {result.Result}");
                if (result.Result == UnityWebRequest.Result.Success)
                {
                    OnRetrievalSuccess?.Invoke();
                }
                else
                {
                    OnRetrievalFailed?.Invoke(result.ResponseCode);
                }
            }
        }

        /// <summary>
        /// Retrieves this components Asset from the CDN
        /// </summary>
        /// <param name="progress">Optional custom progress tracker</param>
        /// <returns>A <see cref="Task"/> object</returns>
        public async Task<AssetBundle> GetAssetBundle(Progress<AssetRetrievalProgress> progress = null)
        {
            if (string.IsNullOrEmpty(AssetBundleName))
            {
                throw new Exception("Asset bundle name is null. Check " +
                "for a missing reference in the editor");
            }

            if (progress == null)
            {
                progress = new Progress<AssetRetrievalProgress>();
            }

            progress.ProgressChanged += ProgressChanged;

            OnRetrievalStarted?.Invoke();

            var task = AssetBundleManager.GetAssetBundle(AssetBundleName, progress);

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

            return task.Result;
        }

        /// <summary>
        /// Retrieves an asset from the content delivery network
        /// </summary>
        /// <typeparam name="T">Any <see cref="UnityEngine.Object"/> 
        /// derivative type, such as <see cref="GameObject"/> or 
        /// <see cref="Sprite"/></typeparam>
        public async Task<T> GetAsset(
            Progress<AssetRetrievalProgress> progress = null,
            Action<T> onSuccess = null,
            Action<AssetRetrievalResult> onFailure = null,
            bool waitForManagerInit = true
        )
        {
            if (string.IsNullOrEmpty(AssetBundleName))
            {
                throw new Exception("Asset bundle name is null. Check " +
                "for a missing reference in the editor");
            }

            if (progress == null)
            {
                progress = new Progress<AssetRetrievalProgress>();
            }

            progress.ProgressChanged += ProgressChanged;

            OnRetrievalStarted?.Invoke();

            var task = AssetBundleManager.GetAsset<T>(
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

            Asset = task.Result;

            InvokeCompletionEvent();

            return task.Result;
        }

        public void Flush(bool forceUnload = false)
        {
            if (!TryFlushAssets(forceUnload))
            {
                Debug.LogWarning($"Couldn't flush assets of requester {gameObject.name}");
            }
        }

        public bool TryFlushAssets(bool forceUnload = false)
        {
            if (AssetBundleManager.TryGetAssetBundleData(AssetBundleName, out var data))
            {
                data.Flush(forceUnload);
                return true;
            }
            else
            {
                Debug.Log("Couldn't flush");
                return false;
            }
            
        }
    }
}