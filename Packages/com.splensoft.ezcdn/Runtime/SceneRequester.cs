using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Loads a scene that is already in Build Settings (local shipping),
    /// or falls back to an asset-bundle scene when CDN mode is enabled.
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
        /// Build Settings scene path, e.g. Assets/Scenes/Main.unity.
        /// Preferred for local shipping (editor + player).
        /// </summary>
        [field: SerializeField]
        public string ScenePath { get; set; }

        public void DownloadAndLoadScene()
        {
            DownloadAndLoadSceneAsync();
        }

        public async void DownloadAndLoadSceneAsync(
            Progress<AssetRetrievalProgress> progress = null,
            Action onSuccess = null,
            Action<AssetRetrievalResult> onFailure = null,
            bool waitForManagerInit = true)
        {
            progress ??= new Progress<AssetRetrievalProgress>();
            progress.ProgressChanged += ProgressChanged;

            try
            {
                string path = ResolveScenePath();
                if (!string.IsNullOrEmpty(path))
                {
                    await LoadBuiltInScene(path, progress, onSuccess, onFailure);
                    return;
                }

                var settings = AssetBundleManagerSettings.Get();
                if (!settings.AllowRemoteCdn)
                {
                    Debug.LogError(
                        $"{nameof(SceneRequester)} on {name}: set {nameof(ScenePath)} " +
                        $"(Build Settings scene). Asset-bundle scenes are only used when CDN is on.");
                    onFailure?.Invoke(new AssetRetrievalResult(404, UnityEngine.Networking.UnityWebRequest.Result.ProtocolError));
                    return;
                }

                var task = AssetBundleManager.LoadSceneAsssetBundle(
                    AssetBundleName,
                    progress,
                    onSuccess,
                    onFailure,
                    waitForManagerInit);

                while (!task.IsCompleted)
                {
                    await Task.Yield();
                    if (!Application.isPlaying)
                        throw new Exception("Unity player closed while asset retrieval was in progress");
                }
            }
            finally
            {
                progress.ProgressChanged -= ProgressChanged;
                InvokeCompletionEvent();
            }
        }

        private string ResolveScenePath()
        {
            if (!string.IsNullOrWhiteSpace(ScenePath))
                return ScenePath.Trim();

#if UNITY_EDITOR
            if (AssetBundleManagerSettings.Get().UseEditorAssetsIfAble &&
                !string.IsNullOrEmpty(AssetBundleName))
            {
                string[] paths = AssetDatabase.GetAssetPathsFromAssetBundle(AssetBundleName);
                return paths?.FirstOrDefault(p =>
                    p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));
            }
#endif
            return null;
        }

        private static async Task LoadBuiltInScene(
            string scenePath,
            IProgress<AssetRetrievalProgress> progress,
            Action onSuccess,
            Action<AssetRetrievalResult> onFailure)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(scenePath);
            if (operation == null)
            {
                Debug.LogError($"Could not load scene '{scenePath}'. Is it in Build Settings?");
                onFailure?.Invoke(new AssetRetrievalResult(404, UnityEngine.Networking.UnityWebRequest.Result.ProtocolError));
                return;
            }

            while (!operation.isDone)
            {
                progress?.Report(new AssetRetrievalProgress(
                    AssetRetrievalStatus.Loading, operation.progress));
                await Task.Yield();
                if (!Application.isPlaying)
                    return;
            }

            progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
            onSuccess?.Invoke();
        }
    }
}
