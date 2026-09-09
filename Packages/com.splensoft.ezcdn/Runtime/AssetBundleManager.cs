using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;
using UnityEngine.Events;
using UnityEngine.Scripting;
using System.Text.RegularExpressions;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Static class that manages asset bundle retrieval and packaging
    /// </summary>
    public static partial class AssetBundleManager
    {
        private class StreamingAssetBundleRequestResult
        {
            public StreamingAssetBundleRequestResult(bool success, AssetBundle assetBundle)
            {
                Success = success;
                AssetBundle = assetBundle;
            }

            public bool Success { get; }
            public AssetBundle AssetBundle { get; }
        }

        /// <summary>
        /// Fires when <see cref="GetAsset"/> is called 
        /// </summary>
        public static UnityEvent<string> AssetRetrievalStarted
        { get; } = new UnityEvent<string>();

        /// <summary>
        /// Fires when <see cref="GetAsset"/> is finished 
        /// and asset is fully loaded, ready to instantiate. 
        /// Note: Only fires if the asset is not already loaded. 
        /// Subsequent <see cref="GetAsset"> calls will not 
        /// trigger this event unless 
        /// <see cref="AssetBundleData.Flush(bool)"/> is called 
        /// on the data
        /// </summary>
        public static UnityEvent<string> AssetLoaded
        { get; } = new UnityEvent<string>();

        /// <summary>
        /// Fires when <see cref="GetAssetBundle"/> is called
        /// </summary>
        public static UnityEvent<string> AssetBundleDownloadStarted
        { get; } = new UnityEvent<string>();

        /// <summary>
        /// Fires when <see cref="GetAssetBundle"/> is finished and 
        /// asset bundle has completed downloading. Note that 
        /// this does not mean the asset has been loaded. 
        /// Subscribe to <see cref="AssetLoaded"/> 
        /// to know when the asset has been loaded
        /// </summary>
        public static UnityEvent<string> AssetBundleDownloadFinished
        { get; } = new UnityEvent<string>();

        /// <summary>
        /// Fires when <see cref="LoadSceneAssetBundle"/> 
        /// is called
        /// </summary>
        public static UnityEvent<string> SceneAssetRetrievalStarted
        { get; } = new UnityEvent<string>();

        /// <summary>
        /// Fires when <see cref="LoadSceneAssetBundle"/> 
        /// is finished and scene is fully loaded
        /// </summary>
        public static UnityEvent<string> SceneAssetLoaded
        { get; } = new UnityEvent<string>();

        private const string _quitWhileRetrievingMessage = "Application quit while retrieving asset";

        public static bool Initialized { get; private set; }
        public static bool IsInitializing { get; private set; }

        private static Dictionary<string, AssetBundleData>
            _assetBundleData = new Dictionary<string, AssetBundleData>();

        private static Dictionary<string, AssetRetrievalResult>
            _downloadResponseCodePerAssetBundleName = new Dictionary<string, AssetRetrievalResult>();

        private static float _selfInitializerTimeout = 5f;
        private static float _currentSelfInitializerTimeout = 0f;
        private static int _currentDownloads;
        /// <summary>
        /// Set when local-only init fails so AutoInitialize does not spin forever.
        /// </summary>
        private static bool _initializeAborted;


        /// <summary>
        /// Set to false to disable the auto initialization (retrieval of 
        /// <see cref="AssetBundleManifest"/> and caching dependencies). Note: 
        /// You must set this to false immedately when the app starts to avoid 
        /// the first initialization attempt. Best practice would be to use 
        /// a gameobject that calls awake in the inital scene in the game
        /// </summary>
        public static bool AutoInitialize { get; set; } = true;

        /// <summary>
        /// Optional hook for external pipeline diagnostics (phase, message).
        /// </summary>
        public static Action<string, string> DiagnosticLog;

        private static void Diag(string phase, string message)
            => DiagnosticLog?.Invoke(phase, message);

        [RuntimeInitializeOnLoadMethod, Preserve]
        private static async void OnAppStart()
        {
            while (true)
            {
                if (!AutoInitialize)
                {
                    Log.Write(LogLevel.Log, "Asset Bundle Manager auto initialization was disabled");
                    return;
                }

                if (_initializeAborted)
                    return;

                if (!Initialized && !IsInitializing)
                {
                    if (_currentSelfInitializerTimeout > 0f)
                    {
                        _currentSelfInitializerTimeout -= Time.unscaledDeltaTime;
                    }
                    else
                    {
                        Initialize();
                        _currentSelfInitializerTimeout = _selfInitializerTimeout;
                    }
                }

                await Task.Yield();
                if (!Application.isPlaying) return;
            }
        }

        /// <summary>
        /// Attempts to return <see cref="AssetBundleData"/> for 
        /// an asset bundle. The <see cref="AssetBundleManager"/> 
        /// must be initialized for the manifest data to be available
        /// </summary>
        /// <returns>True if the asset bundle data exists in the library</returns>
        public static bool TryGetAssetBundleData(string assetBundleName, out AssetBundleData data)
        {
            return _assetBundleData.TryGetValue(assetBundleName, out data);
        }

        /// <summary>
        /// Gets status of last download attempt for an asset. 
        /// Note: If an asset was pulled from the cache, it 
        /// will return 200 with <see cref="UnityWebRequest.Result.Success"/>. 
        /// To get the result of the initialization attempt (downloading 
        /// the manifest), use <see cref="TryGetAssetManifestRetrievalResult"/>
        /// </summary>
        /// <returns>True if the asset existed in the manifest (and the plugin 
        /// was properly initialized), and at least one attempt was made to 
        /// retrieve the asset prior to this call</returns>
        public static bool TryGetAssetRetrievalResult(string assetBundleName, out AssetRetrievalResult result)
        {
            return _downloadResponseCodePerAssetBundleName.TryGetValue(assetBundleName, out result);
        }

        /// <summary>
        /// Returns an already-loaded asset without triggering download or async retrieval.
        /// </summary>
        public static bool TryGetCachedAsset<T>(string name, out T asset) where T : UnityEngine.Object
        {
            asset = null;
            if (_assetBundleData.TryGetValue(name, out AssetBundleData data) && data.Asset != null)
            {
                asset = data.Asset as T;
                return asset != null;
            }

            return false;
        }

        /// <summary>
        /// Gets status of last download attempt of the project 
        /// asset bundle manifest for this platform. 
        /// Note: If the plugin is set to use editor assets and this is 
        /// running in an editor, it
        /// will return 200 with <see cref="UnityWebRequest.Result.Success"/>.
        /// </summary>
        /// <returns>True if at least one attempt was made to 
        /// retrieve the manifest prior to this call</returns>
        public static bool TryGetAssetManifestRetrievalResult(out AssetRetrievalResult result)
        {
            result = null;
            AssetBundleManagerSettings settings = AssetBundleManagerSettings.Get();

            if (!settings.BuildTargetsByPlatform.TryGetValue(Application.platform, out int buildtarget))
            {
                Debug.LogError($"Platform {Application.platform} was not " +
                    $"configured in the Asset Bundle Manager settings. The Asset" +
                    $" Bundle Manager cannot initialize.");
                return false;
            }

            string targetName = settings.BuildTargetNames[buildtarget];

            return _downloadResponseCodePerAssetBundleName.TryGetValue(targetName, out result);
        }

        /// <summary>
        /// Downloads and unpacks the <see cref="AssetBundleManifest"/> for 
        /// this platform from the CDN
        /// </summary>
        /// <returns>The <see cref="AssetBundleManifest"/> for this platform</returns>
        public static async Task<AssetBundleManifest> GetManifest()
        {
            AssetBundleManagerSettings settings = AssetBundleManagerSettings.Get();

            if (!settings.BuildTargetsByPlatform.TryGetValue(Application.platform, out int buildtarget))
            {
                throw new Exception($"Platform {Application.platform} was not configured " +
                    $"in the Asset Bundle Manager settings. The Asset Bundle " +
                    $"Manager cannot initialize.");
            }

            string targetName = settings.BuildTargetNames[buildtarget];

            var task = GetAsset<AssetBundleManifest>(targetName, waitForInitialize: false);

            await task;
            if (!Application.isPlaying) return null;

            return task.Result;
        }

        /// <summary>
        /// Sets a time, in seconds, that the Asset Bundle Manager will attempt to auto-initialize
        /// </summary>
        /// <param name="timeInSeconds">Should be a value >= 1</param>
        public static void SetSelfInitializerTimeout(float timeInSeconds)
        {
            if (timeInSeconds < 1)
            {
                timeInSeconds = 1;
                Log.Write(LogLevel.Warning, "Do not try to set " +
                    "the self initializer timeout to a " +
                    "value less than 1. This is bad practice " +
                    "and is not safe.");
            }
            _selfInitializerTimeout = timeInSeconds;
        }

        /// <summary>
        /// Downloads the project asset bundle manifest for this platform 
        /// and caches the dependency data. Called automatically on app 
        /// start unless <see cref="AutoInitialize"/> is set to false on app awake
        /// </summary>
        public static async void Initialize()
        {
            if (!Initialized && !IsInitializing)
            {
                Log.Write(LogLevel.Verbose, $"Asset bundle manager initializing ...");
                IsInitializing = true;
                // first get master manifest. This is used for dependencies.
                // Note: Not needed for in-editor playmode and will
                // immediately return
                var task = GetManifestAndCacheDependencyData();
                await task;
            }
            else
            {
                Log.Write(LogLevel.Warning, "Asset Bundle Manager was " +
                    "already initialized or is initializing. " +
                    "Check the Initialized property before " +
                    "calling Initialize()");
            }
        }

        private static async Task GetManifestAndCacheDependencyData()
        {
            AssetBundleManagerSettings settings = AssetBundleManagerSettings.Get();

            Diag("ABM.Initialize", $"Loading manifest — isEditor={Application.isEditor}");

#if UNITY_EDITOR
            // Editor Play Mode: Assets/ is the source of truth — no CDN / mirror manifest.
            if (settings.UseEditorAssetsIfAble)
            {
                _assetBundleData.Clear();
                foreach (string assetBundleName in AssetDatabase.GetAllAssetBundleNames())
                {
                    if (string.IsNullOrEmpty(assetBundleName))
                        continue;
                    _assetBundleData[assetBundleName] = new AssetBundleData(assetBundleName)
                    {
                        Dependencies = new List<string>()
                    };
                }

                Initialized = true;
                IsInitializing = false;
                Diag("ABM.Initialize",
                    $"Editor assets mode — {_assetBundleData.Count} AssetDatabase bundle names (no CDN/mirror)");
                return;
            }
#endif

            if (!settings.BuildTargetsByPlatform.TryGetValue(Application.platform, out int buildtarget))
            {
                Debug.LogError($"Platform {Application.platform} was " +
                    $"not configured in the Asset Bundle Manager " +
                    $"settings. The Asset Bundle Manager cannot " +
                    $"initialize.");

                AbortInitialize(settings);
                return;
            }
            string targetName = settings.BuildTargetNames[buildtarget];

            // Player / local shipping: only StreamingAssets (no CDN).
            if (!settings.AllowRemoteCdn)
            {
                var local = await TryGetLocalAssetBundleAsync(targetName);
                if (!Application.isPlaying) return;

                if (!local.Success || local.AssetBundle == null)
                {
                    Debug.LogError(
                        $"Asset Bundle Manager failed to load shipped platform manifest '{targetName}' " +
                        $"from StreamingAssets/AssetBundles. Rebuild the player so PostBuild copies local bundles.");
                    AbortInitialize(settings);
                    return;
                }

                var loadManifest = local.AssetBundle.LoadAllAssetsAsync<AssetBundleManifest>();
                while (!loadManifest.isDone)
                {
                    await Task.Yield();
                    if (!Application.isPlaying) return;
                }

                var masterManifest = loadManifest.asset as AssetBundleManifest;
                if (masterManifest == null)
                {
                    Debug.LogError(
                        $"Shipped platform bundle '{targetName}' did not contain an AssetBundleManifest.");
                    AbortInitialize(settings);
                    return;
                }

                CacheManifest(masterManifest);
                _downloadResponseCodePerAssetBundleName[targetName] =
                    new AssetRetrievalResult(200, UnityWebRequest.Result.Success);
                Initialized = true;
                IsInitializing = false;
                Log.Write(LogLevel.Verbose, "Asset bundle manager initialized from StreamingAssets");
                Diag("ABM.Initialize",
                    $"Shipped StreamingAssets — {_assetBundleData.Count} bundles for {Application.platform}");
                return;
            }

            var task = GetAsset<AssetBundleManifest>(targetName, waitForInitialize: false);
            await task;

            AssetBundleManifest remoteManifest = task.Result;
            if (remoteManifest == null)
            {
                Debug.LogError(
                    $"Asset Bundle Manager failed to load platform manifest '{targetName}'.");
                AbortInitialize(settings);
                return;
            }

            CacheManifest(remoteManifest);
            Initialized = true;
            IsInitializing = false;
            Log.Write(LogLevel.Verbose, $"Asset bundle manager initialized");
            Diag("ABM.Initialize", $"Manifest cached — {_assetBundleData.Count} bundle entries for platform {Application.platform}");
        }

        private static void CacheManifest(AssetBundleManifest masterManifest)
        {
            _assetBundleData.Clear();
            string[] assetBundleNames = masterManifest.GetAllAssetBundles();
            foreach (string assetBundleName in assetBundleNames)
            {
                _assetBundleData[assetBundleName] = new AssetBundleData(assetBundleName)
                {
                    Dependencies = masterManifest
                        .GetDirectDependencies(assetBundleName)
                        .ToList(),
                    Hash = masterManifest.GetAssetBundleHash(assetBundleName)
                };

                Log.Write(LogLevel.Log, $"{assetBundleName} | hash = " +
                    $"{masterManifest.GetAssetBundleHash(assetBundleName)}");
            }
        }

        private static void AbortInitialize(AssetBundleManagerSettings settings)
        {
            IsInitializing = false;
            // Local-only shipping: never spin forever waiting for CDN.
            if (settings == null || !settings.AllowRemoteCdn)
                _initializeAborted = true;
        }

        private static async Task<bool> WaitUntilInitializedOrAborted()
        {
            while (!Initialized && !_initializeAborted)
            {
                await Task.Yield();
                if (!Application.isPlaying)
                    return false;
            }

            return Initialized;
        }

        /// <summary>
        /// Uses client-facing 
        /// <see href="https://services.docs.unity.com/content-delivery-client/v1/index.html">
        /// Unity Cloud Content Delivery API</see> to determine if a 
        /// bucket exists. Requires an internet connection
        /// </summary>
        /// <param name="bucketId">Bucket ID, can be pulled from <see cref="AssetBundleManagerSettings"/></param>
        /// <returns>True if bucket exists on the Unity CDN</returns>
        public static async Task<bool> DoesBucketExist(string bucketId)
        {
            var settings = AssetBundleManagerSettings.Get();
            string url = $"https://{settings.UnityProjectId}.client-api.unity3dusercontent.com/client_api/v1/environments/{settings.ActiveEnvironmentId}/buckets/${bucketId}";
            using var request = UnityWebRequest.Get(url);
            var sentReq = request.SendWebRequest();

            while (!sentReq.isDone)
            {
                await Task.Yield();
                if (!Application.isPlaying)
                {
                    throw new Exception("Unity closed during web request.");
                }
            }

            var responseCode = request.responseCode;
            return responseCode == 200;
        }

        /// <summary>
        /// If runtime, requires plugin to be initialzied and <see cref="Initialized"/> must be true. 
        /// Scans manifest for all asset bundle names. If running in the editor
        /// with Use Editor Assets checked in the settings, it will scan the
        /// project's <see cref="AssetDatabase"/>
        /// </summary>
        /// <param name="regexPattern">A regex to match against 
        /// the asset bundle names</param>
        /// <returns>All asset bundle names that matched the regex</returns>
        public static async Task<string[]> GetAssetBundleNames(string regexPattern)
        {
#if UNITY_EDITOR
            // Play Mode with editor assets: AssetDatabase is the catalog of bundle names.
            if (Application.isPlaying && AssetBundleManagerSettings.Get().UseEditorAssetsIfAble)
            {
                if (!await WaitUntilInitializedOrAborted()) return null;
                return AssetDatabase.GetAllAssetBundleNames()
                    .Where(x => Regex.IsMatch(x, regexPattern))
                    .ToArray();
            }

            if (!Application.isPlaying)
            {
                return AssetDatabase.GetAllAssetBundleNames()
                    .Where(x => Regex.IsMatch(x, regexPattern))
                    .ToArray();
            }
#endif
            if (!await WaitUntilInitializedOrAborted()) return null;

            return _assetBundleData.Keys
                .Where(x => Regex.IsMatch(x, regexPattern))
                .ToArray();
        }

        /// <summary>
        /// If runtime, requires plugin to be initialzied and 
        /// <see cref="Initialized"/> must be true. 
        /// Scans manifest for all asset bundle names. If running in the editor
        /// with Use Editor Assets checked in the settings, it will scan the
        /// project's <see cref="AssetDatabase"/>
        /// </summary>
        /// <param name="type">Translates a type to a 
        /// string that follows the standard asset 
        /// naming format of EZ-CDN: typename_guid</param>
        /// <returns>All asset bundle names that matched typename_guid</returns>
        public static async Task<string[]> GetAssetBundleNames(Type type)
        {
            string pattern = @$"{type.Name.ToLower()}_.*";
            var task = GetAssetBundleNames(pattern);
            await task;
            if (!Application.isPlaying) return null;
            return task.Result;
        }

        /// <summary>
        /// Downloads an asset bundle from the Unity 
        /// Content Delivery cloud and unpacks the asset. 
        /// </summary>
        /// <param name="name">The name of the AssetBundle</param>
        /// <param name="progress">Trackable progress reporter</param>
        /// <param name="waitForInitialize">The task will wait for the Asset Bundle 
        /// Manager to initialize (retrieve its manifest for dependencies) before 
        /// requesting the asset. Strongly recommended to leave this as default (true)
        /// unless you have a very special case (like retrieving the manifest 
        /// manually)</param>
        /// <param name="onSuccess">Action which is invoked on successful retrieval</param>
        /// <param name="onFailure">Action which is invoked on failed retrieval</param>
        /// <returns>A <see cref="Task"/> object with a result of type T</returns>
        public static async Task<T> GetAsset<T>(
            string name,
            IProgress<AssetRetrievalProgress> progress = null,
            Action<T> onSuccess = null,
            Action<AssetRetrievalResult> onFailure = null,
            bool waitForInitialize = true,
            bool allowLocalFallback = true)
            where T : UnityEngine.Object
        {
            if (waitForInitialize)
            {
                if (!await WaitUntilInitializedOrAborted()) return null;
            }

            AssetRetrievalStarted?.Invoke(name);

            if (string.IsNullOrWhiteSpace(name))
            {
                Debug.LogError("Attempted to get empty asset bundle name");
                progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
                return null;
            }

            _assetBundleData.TryGetValue(name, out AssetBundleData data);

            if (allowLocalFallback && data != null && data.Asset != null)
            {
                if (data.Asset != null)
                {
                    Log.Write(LogLevel.Log, $"Retrieving loaded asset {data.Asset.name}");
                }
                Diag("ABM.GetAsset", $"CACHE HIT {name} -> {data.Asset.name} ({typeof(T).Name})");
                progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
                onSuccess?.Invoke((T)data.Asset);
                return (T)data.Asset;
            }

#if UNITY_EDITOR
            // Play Mode in editor: always resolve from AssetDatabase (Assets/ is source of truth).
            if (Application.isPlaying &&
                AssetBundleManagerSettings.Get().UseEditorAssetsIfAble &&
                !string.Equals(typeof(T).Name, nameof(AssetBundleManifest), StringComparison.Ordinal))
            {
                if (TryGetEditorAsset(name, out T editorAsset) && editorAsset != null)
                {
                    if (data != null)
                    {
                        data.Asset = editorAsset;
                        data.Loaded = true;
                    }
                    Diag("ABM.GetAsset", $"EDITOR ASSET {name} -> {editorAsset.name} ({typeof(T).Name})");
                    progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
                    onSuccess?.Invoke(editorAsset);
                    return editorAsset;
                }

                Diag("ABM.GetAsset", $"EDITOR ASSET MISS {name} ({typeof(T).Name})");
                progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
                onFailure?.Invoke(new AssetRetrievalResult(404, UnityWebRequest.Result.ProtocolError));
                return null;
            }
#endif

            Diag("ABM.GetAsset", $"LOAD {name} ({typeof(T).Name})");
            var getBundleProgress = new Progress<AssetRetrievalProgress>();
            void GetBundleProgress_ProgressChanged(object sender, AssetRetrievalProgress e)
            {
                progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Downloading, e.Progress * 0.4f));
            }
            getBundleProgress.ProgressChanged += GetBundleProgress_ProgressChanged;
            var getBundleTask = GetAssetBundle(
                name,
                getBundleProgress,
                waitForInitialize: waitForInitialize,
                allowLocalFallback: allowLocalFallback);
            await getBundleTask;
            if (!Application.isPlaying) return null;

            var bundle = getBundleTask.Result;

            if (bundle == null)
            {
                Debug.LogError($"Asset bundle {name} returned null after attempted download");
                Diag("ABM.GetAsset", $"CDN FAILED {name} — bundle download returned null");
                var res = new AssetRetrievalResult(404, UnityWebRequest.Result.ProtocolError);
                onFailure?.Invoke(res);
                _downloadResponseCodePerAssetBundleName[name] = res;
                return null;
            }

            if (data != null)
            {
                while (data.IsLoading)
                {
                    await Task.Yield();
                    if (!Application.isPlaying)
                        return null;
                }

                if (data.Loaded && data.Asset != null)
                {
                    Log.Write(
                        LogLevel.Verbose,
                        $"Cancelling load of asset {name} because it is already loaded. Returning loaded asset instead.");

                    progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
                    Log.Write(LogLevel.Log, $"Loaded asset {data.Asset.name}");
                    onSuccess?.Invoke((T)data.Asset);
                    //AssetLoaded?.Invoke(name);
                    return (T)data.Asset;
                }

                data.IsLoading = true;
            }

            var loadAsset = bundle.LoadAllAssetsAsync<T>();

            while (!loadAsset.isDone)
            {
                progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Loading, 0.4f + loadAsset.progress * 0.5f));

                await Task.Yield();
                if (!Application.isPlaying)
                    return null;
            }

            if (data != null)
            {
                data.Loaded = true;
                data.Asset = loadAsset.asset;
                data.IsLoading = false;

                if (data.Asset != null)
                {
                    Log.Write(LogLevel.Log, $"Loaded asset {data.Asset.name}");
                }
            }
            progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
            onSuccess?.Invoke((T)loadAsset.asset);
            AssetLoaded?.Invoke(name);
            Diag("ABM.GetAsset", $"LOADED {name} -> '{loadAsset.asset?.name}' ({typeof(T).Name})");
            return (T)loadAsset.asset;
        }

        /// <summary>
        /// Downloads an asset bundle from the Unity Content Delivery cloud
        /// </summary>
        /// <param name="name">The name of the AssetBundle</param>
        /// <param name="progress">Trackable progress reporter</param>
        /// <param name="waitForInitialize">The task will wait for the Asset Bundle 
        /// Manager to initialize (retrieve its manifest for dependencies) before 
        /// requesting the asset. Strongly recommended to leave this as default (true)
        /// unless you have a very special case (like retrieving the manifest 
        /// manually)</param>
        /// <param name="onSuccess">Action which is invoked on successful retrieval</param>
        /// <param name="onFailure">Action which is invoked on failed retrieval</param>
        /// <returns>A <see cref="Task"/> object with an <see cref="AssetBundle"/> result</returns>
        public static async Task<AssetBundle> GetAssetBundle(
            string name,
            IProgress<AssetRetrievalProgress> progress = null,
            Action<AssetBundle> onSuccess = null,
            Action<AssetRetrievalResult> onFailure = null,
            bool waitForInitialize = true,
            bool allowLocalFallback = true
        )
        {
            if (waitForInitialize)
            {
                if (!await WaitUntilInitializedOrAborted()) return null;
            }

            AssetBundleDownloadStarted?.Invoke(name);

            if (string.IsNullOrWhiteSpace(name))
            {
                Debug.LogError("Attempted to get empty asset bundle name");
                progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
                return null;
            }

            if (_assetBundleData.TryGetValue(name, out AssetBundleData data))
            {
                data.DownloadStarted = true;
            }

            var progress2 = new Progress<AssetRetrievalProgress>();

            void Progress2_ProgressChanged(object sender, AssetRetrievalProgress e)
            {
                progress?.Report(new AssetRetrievalProgress(
                    AssetRetrievalStatus.Downloading,
                    e.Progress * 0.4f
                ));
            }

            progress2.ProgressChanged += Progress2_ProgressChanged;
            await DownloadAndCacheDependencies(name, progress2);

            if (allowLocalFallback && data != null && data.AssetBundle != null)
            {
                progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
                onSuccess?.Invoke(data.AssetBundle);
                AssetBundleDownloadFinished?.Invoke(name);
                return data.AssetBundle;
            }

            if (allowLocalFallback)
            {
                var localBundle = await TryGetLocalAssetBundleAsync(name);
                if (localBundle.Success)
                {
                    progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
                    onSuccess?.Invoke(localBundle.AssetBundle);
                    if (data != null)
                    {
                        data.AssetBundle = localBundle.AssetBundle;
                        data.LastResponseCode = 200;
                    }
                    _downloadResponseCodePerAssetBundleName[name] =
                        new AssetRetrievalResult(200, UnityWebRequest.Result.Success);
                    AssetBundleDownloadFinished?.Invoke(name);
                    return localBundle.AssetBundle;
                }
            }

            Diag("ABM.GetAssetBundle", $"NO LOCAL BUNDLE for {name}");
            progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
            var missing = new AssetRetrievalResult(404, UnityWebRequest.Result.ProtocolError);
            onFailure?.Invoke(missing);
            _downloadResponseCodePerAssetBundleName[name] = missing;
            AssetBundleDownloadFinished?.Invoke(name);
            return null;
        }

        private static StreamingAssetBundleRequestResult TryGetLocalAssetBundle(string name)
        {
            string streamingPath = Path.Combine(
                Application.streamingAssetsPath, "AssetBundles", name);

            return TryLoadAssetBundleFromFile(
                streamingPath, name, "StreamingAssets");
        }

        private static async Task<StreamingAssetBundleRequestResult> TryGetLocalAssetBundleAsync(string name)
        {
            string streamingPath = Path.Combine(
                Application.streamingAssetsPath, "AssetBundles", name);

            return await TryLoadAssetBundleFromFileAsync(
                streamingPath, name, "StreamingAssets");
        }

        /// <summary>
        /// Unity refuses to load the same bundle file twice ("already loaded" error).
        /// Concurrent requests for the same bundle (e.g. two assemblies sharing an arm
        /// prefab during a room load) must reuse the in-memory bundle instead of failing.
        /// </summary>
        private static AssetBundle FindAlreadyLoadedBundle(string name)
        {
            foreach (var loaded in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (loaded != null && string.Equals(loaded.name, name, StringComparison.OrdinalIgnoreCase))
                    return loaded;
            }
            return null;
        }

        private static StreamingAssetBundleRequestResult TryLoadAssetBundleFromFile(
            string path, string name, string source)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new StreamingAssetBundleRequestResult(false, null);

            AssetBundle already = FindAlreadyLoadedBundle(name);
            if (already != null)
            {
                Diag("ABM.GetAssetBundle", $"REUSE already-loaded {name}");
                return new StreamingAssetBundleRequestResult(true, already);
            }

            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                // Load can fail because another request loaded it between our check and now.
                already = FindAlreadyLoadedBundle(name);
                if (already != null)
                {
                    Diag("ABM.GetAssetBundle", $"REUSE already-loaded (post-fail) {name}");
                    return new StreamingAssetBundleRequestResult(true, already);
                }

                Debug.LogError($"Could not load asset bundle {name} from {source}: {path}");
                return new StreamingAssetBundleRequestResult(false, null);
            }

            Log.Write(LogLevel.Log, $"Loaded asset bundle {name} from {source}: {path}");
            Diag("ABM.GetAssetBundle", $"LOCAL {source} {name} from {path}");
            return new StreamingAssetBundleRequestResult(true, bundle);
        }

        private static async Task<StreamingAssetBundleRequestResult> TryLoadAssetBundleFromFileAsync(
            string path, string name, string source)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new StreamingAssetBundleRequestResult(false, null);

            AssetBundle already = FindAlreadyLoadedBundle(name);
            if (already != null)
            {
                Diag("ABM.GetAssetBundle", $"REUSE already-loaded {name}");
                return new StreamingAssetBundleRequestResult(true, already);
            }

            AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(path);
            while (!request.isDone)
            {
                await Task.Yield();
                if (!Application.isPlaying)
                    return new StreamingAssetBundleRequestResult(false, null);
            }

            AssetBundle bundle = request.assetBundle;
            if (bundle == null)
            {
                already = FindAlreadyLoadedBundle(name);
                if (already != null)
                {
                    Diag("ABM.GetAssetBundle", $"REUSE already-loaded (post-fail) {name}");
                    return new StreamingAssetBundleRequestResult(true, already);
                }

                Debug.LogError($"Could not load asset bundle {name} from {source}: {path}");
                return new StreamingAssetBundleRequestResult(false, null);
            }

            Log.Write(LogLevel.Log, $"Loaded asset bundle {name} from {source}: {path}");
            Diag("ABM.GetAssetBundle", $"LOCAL {source} {name} from {path}");
            return new StreamingAssetBundleRequestResult(true, bundle);
        }

        private static async Task<StreamingAssetBundleRequestResult> TryGetAssetBundleStreamingAssets(string name)
        {
            Log.Write(LogLevel.Log, $"Attempting to retrieve asset " +
                $"{name} local copy from streaming assets");

            var settings = AssetBundleManagerSettings.Get();

            if (!settings.KeepLocalCopy)
                return new StreamingAssetBundleRequestResult(false, null);

            if (string.IsNullOrWhiteSpace(name))
            {
                Debug.LogError("Attempted to get empty asset bundle name");
                return new StreamingAssetBundleRequestResult(false, null);
            }

            var assetPath = Path.Combine(
                Application.streamingAssetsPath,
                "AssetBundles",
                name
            );

            if (Application.platform == RuntimePlatform.WebGLPlayer ||
                Application.platform == RuntimePlatform.Android)
            {
                using var request = UnityWebRequestAssetBundle.GetAssetBundle(assetPath);

                while (!request.isDone)
                {
                    await Task.Yield();
                    if (!Application.isPlaying) return null;
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(request);
                    return new StreamingAssetBundleRequestResult(true, bundle);
                }
                else
                {
                    Debug.LogError($"Could not retrieve asset {name} from StreamingAssets - web request failed");
                    return new StreamingAssetBundleRequestResult(false, null);
                }
            }
            else
            {
                if (!File.Exists(assetPath))
                {
                    Debug.LogError($"Could not retrieve asset {name} from StreamingAssets - file does not exist");
                    return new StreamingAssetBundleRequestResult(false, null);
                }

                var bundle = AssetBundle.LoadFromFile(assetPath);
                return new StreamingAssetBundleRequestResult(true, bundle);
            }
        }

        /// <summary>
        /// Downloads a scene asset bundle from the Unity 
        /// Content Delivery cloud and immediately loads it
        /// </summary>
        /// <param name="name">The name of the AssetBundle</param>
        /// <param name="progress">Trackable progress reporter</param>
        /// <param name="waitForInitialize">The task will wait for the Asset Bundle 
        /// Manager to initialize (retrieve its manifest for dependencies) before 
        /// requesting the asset. Strongly recommended to leave this as default (true)
        /// unless you have a very special case (like retrieving the manifest 
        /// manually)</param>
        /// <param name="onSuccess">Action which is invoked on successful retrieval</param>
        /// <param name="onFailure">Action which is invoked on failed retrieval</param>
        /// <returns>A <see cref="Task"/> object</returns>
        public static async Task LoadSceneAsssetBundle(
            string name,
            IProgress<AssetRetrievalProgress> progress = null,
            Action onSuccess = null,
            Action<AssetRetrievalResult> onFailure = null,
            bool waitForInitialize = true
        )
        {
            if (waitForInitialize)
            {
                if (!await WaitUntilInitializedOrAborted()) return;
            }

            SceneAssetRetrievalStarted?.Invoke(name);

#if UNITY_EDITOR
            // Editor Play Mode: load scenes from Assets/ like prefabs (no CDN / StreamingAssets).
            if (AssetBundleManagerSettings.Get().UseEditorAssetsIfAble)
            {
                string[] assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(name);
                string scenePath = assetPaths?.FirstOrDefault(p =>
                    p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(scenePath))
                {
                    await LoadSceneFromPath(name, scenePath, progress, onSuccess, onFailure);
                    return;
                }
            }
#endif

            if (_assetBundleData.TryGetValue(name, out AssetBundleData data))
            {
                data.DownloadStarted = true;
            }

            var bundleTimer = System.Diagnostics.Stopwatch.StartNew();
            var getBundleTask = GetAssetBundle(name, progress);
            await getBundleTask;
            bundleTimer.Stop();
            Diag("SceneLoad.CDN", $"bundle '{name}' fetched in {bundleTimer.ElapsedMilliseconds}ms");

            var bundle = getBundleTask.Result;
            if (bundle == null)
            {
                Debug.LogError($"Scene asset bundle '{name}' could not be loaded.");
                onFailure?.Invoke(new AssetRetrievalResult(404, UnityWebRequest.Result.ProtocolError));
                return;
            }

            LoadSceneAssetBundle(name, bundle, progress, onSuccess, onFailure, bundleTimer.ElapsedMilliseconds);
        }

        private static async Task LoadSceneFromPath(
            string name,
            string scenePath,
            IProgress<AssetRetrievalProgress> progress,
            Action onSuccess,
            Action<AssetRetrievalResult> onFailure)
        {
            try
            {
                AsyncOperation operation = SceneManager.LoadSceneAsync(scenePath);
                if (operation == null)
                {
                    Debug.LogError($"Could not load scene '{scenePath}' (bundle {name}).");
                    onFailure?.Invoke(new AssetRetrievalResult(404, UnityWebRequest.Result.ProtocolError));
                    return;
                }

                while (!operation.isDone)
                {
                    progress?.Report(new AssetRetrievalProgress(
                        AssetRetrievalStatus.Loading, 0.4f + operation.progress * 0.4f));
                    await Task.Yield();
                    if (!Application.isPlaying) return;
                }

                progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
                onSuccess?.Invoke();
                SceneAssetLoaded?.Invoke(name);
            }
            catch
            {
                onFailure?.Invoke(new AssetRetrievalResult(500, UnityWebRequest.Result.DataProcessingError));
                throw;
            }
        }

        private static async void LoadSceneAssetBundle(
            string name,
            AssetBundle bundle,
            IProgress<AssetRetrievalProgress> progress,
            Action onSuccess,
            Action<AssetRetrievalResult> onFailure,
            long bundleFetchMs = 0)
        {
            string[] paths = bundle.GetAllScenePaths();

            if (paths.Length < 1)
            {
                onFailure?.Invoke(new AssetRetrievalResult(200, UnityWebRequest.Result.Success));
                throw new Exception($"No scenes in asset bundle");
            }

            try
            {
                var sceneLoadTimer = System.Diagnostics.Stopwatch.StartNew();
                AsyncOperation operation = SceneManager.LoadSceneAsync(paths[0]);

                //Wait until we are done loading the scene
                float lastProgress = 0;
                while (!operation.isDone)
                {
                    if (operation.progress != lastProgress)
                    {
                        progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Loading, 0.4f + operation.progress * 0.4f));
                    }

                    await Task.Yield();
                    if (!Application.isPlaying) return;
                }

                sceneLoadTimer.Stop();
                Diag("SceneLoad.CDN",
                    $"scene '{paths[0]}' async load {sceneLoadTimer.ElapsedMilliseconds}ms | bundle '{name}' fetch {bundleFetchMs}ms | total {bundleFetchMs + sceneLoadTimer.ElapsedMilliseconds}ms");

                Log.Write(LogLevel.Log, $"Loaded scene {paths[0]}");
                progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
                onSuccess?.Invoke();
                SceneAssetLoaded?.Invoke(name);
            }
            catch
            {
                onFailure?.Invoke(new AssetRetrievalResult(200, UnityWebRequest.Result.Success));
                throw;
            }
        }

        /// <summary>
        /// Downloads all of an AssetBundle's dependencies and keeps them cached. Good for loading ahead to make downloading a later asset bundle quicker
        /// </summary>
        /// <param name="assetBundleName">The name of the AssetBundle</param>
        /// <param name="progress">Trackable progress reporter</param>
        /// <returns>A <see cref="Task"/> object</returns>
        public static async Task DownloadAndCacheDependencies(string assetBundleName, IProgress<AssetRetrievalProgress> progress = null)
        {
            var tasks = new List<Task<AssetBundle>>();
            if (_assetBundleData.TryGetValue(assetBundleName, out AssetBundleData data) && data.Dependencies.Count > 0)
            {
                for (int i = 0; i < data.Dependencies.Count; i++)
                {
                    var dependency = data.Dependencies[i];
                    if (_assetBundleData.TryGetValue(dependency, out AssetBundleData dependencyData) && !dependencyData.Loaded && !dependencyData.DownloadStarted)
                    {
                        dependencyData.DownloadStarted = true;
                        var progress2 = new Progress<AssetRetrievalProgress>();
                        void progressChanged(object s, AssetRetrievalProgress e)
                        {
                            float prog = (i + e.Progress) / data.Dependencies.Count;
                            progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Downloading, prog));
                        }
                        progress2.ProgressChanged += progressChanged;
                        var task = GetAssetBundle(dependency, progress2);
                        tasks.Add(task);
                    }
                }
            }

            while (tasks.FirstOrDefault(x => !x.IsCompleted) != default)
            {
                await Task.Yield();
                if (!Application.isPlaying) return;
            }
            progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
        }

        /// <summary>
        /// Returns all of an asset's dependencies. 
        /// Does not check <see cref="Initialized"/>.
        /// </summary>
        /// <param name="assetBundleName"></param>
        /// <returns>A list of AssetBundle names as strings</returns>
        public static async Task<List<string>> GetDependencies(string assetBundleName)
        {
            if (!await WaitUntilInitializedOrAborted())
                throw new Exception("App quit during task");

            var result = new List<string>();

            if (_assetBundleData.TryGetValue(assetBundleName, 
            out AssetBundleData data) && data.Dependencies.Count > 0)
            {
                for (int i = 0; i < data.Dependencies.Count; i++)
                {
                    var dependency = data.Dependencies[i];
                    result.Add(dependency);

                    var task2 = GetDependencies(dependency);
                    await task2;

                    result.Concat(task2.Result);
                }
            }

            return result;
        }

        /// <summary>
        /// Gets all <see cref="ScriptableObject"/> assets of a 
        /// certain type from the CDN.
        /// </summary>
        /// <typeparam name="T">A class deriving from <see cref="ScriptableObject"/></typeparam>
        /// <returns>A list of ScriptableObjects of the specified type</returns>
        public static async Task<List<T>> GetAllAssetsOfType<T>(IProgress<AssetRetrievalProgress> progress = null) where T : ScriptableObject 
        {
            // get the scriptable objects
            var task = GetAssetBundleNames(typeof(T));

            await task;
            if (!Application.isPlaying)
                throw new Exception("App quit during task");

            if (task.Result.Length == 0) 
            {
                progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
                return new List<T>();
            }
                
            var tasks = new List<Task<T>>();

            for (int i = 0; i < task.Result.Length; i++)
            {
                string assetBundleName = task.Result[i];

                var assetProgress = new Progress<AssetRetrievalProgress>();
                void progressChanged(object s, AssetRetrievalProgress e)
                {
                    float prog = (i + e.Progress) / task.Result.Length;
                    progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Downloading, prog));
                }
                assetProgress.ProgressChanged += progressChanged;
                var assetTask = GetAsset<T>(assetBundleName, assetProgress);

                tasks.Add(assetTask);
            }

            while (tasks.Any(x => !x.IsCompleted))
            {
                await Task.Yield();

                if (!Application.isPlaying)
                    throw new Exception("App quit during task");
            }

            progress?.Report(new AssetRetrievalProgress(AssetRetrievalStatus.Done, 1));
            return tasks.Select(x => x.Result).ToList();
        }
    }
}