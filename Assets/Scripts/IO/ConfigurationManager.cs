using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;
using System;
using Stopwatch = System.Diagnostics.Stopwatch;
using SplenSoft.UnityUtilities;
using UnityEngine.Events;
using RTG;
using SplenSoft.AssetBundles;
using UnityEditor;
using UnityEngine.UI;

public class ConfigurationManager : MonoBehaviour
{
    public static ConfigurationManager Instance { get; private set; }

    public string CurrentRoomSaveName { get; private set; }

    public static string GetCurrentRoomSaveName()
        => Instance != null ? Instance.CurrentRoomSaveName : null;

    public static string GetSavedRoomsFolder()
        => Path.Combine(Application.persistentDataPath, "Saved");

    public static string GetSavedConfigsFolder()
        => Path.Combine(Application.persistentDataPath, "Saved", "Configs");

    public static UnityEvent OnRoomLoadComplete { get; } = new();
    public static UnityEvent<GameObject> OnConfigurationLoadComplete { get; } = new();

    [Tooltip("Contextual display of GUIDs in hierarchy for easier debugging")] public bool isDebug = false;

    private List<TrackedObject> _newObjects;
    private List<AttachmentPoint> _newPoints;
    DuplicateRoom duplicateRoom;
    public static bool IsLoading { get; private set; }

    private const string _attachPointGUID = "_AP"; // legacy

    private Tracker _tracker;
    private RoomConfiguration _roomConfiguration;

    private readonly string _lastNukedSavesPlayerPrefsKey = "lastNukedSaves";
    private readonly string _nukeBelowVersion = "1.0.0";

    // Helper to normalize stored path (leading '/') for GameObject.Find
    private static string NormalizeFindPath(string raw)
        => string.IsNullOrEmpty(raw) ? raw : (raw[0] == '/' ? raw.Substring(1) : raw);

    /// <summary>
    /// Resolves a saved parent path against loaded (possibly inactive) room objects.
    /// GameObject.Find skips inactive objects, so hierarchy restore must search _newObjects.
    /// </summary>
    private Transform FindLoadedParentTransform(TrackedObject.Data data)
    {
        string rawPath = !string.IsNullOrEmpty(data.parentPath) ? data.parentPath : data.parent;
        if (string.IsNullOrEmpty(rawPath))
            return null;

        GameObject found = FindInLoadedObjects(rawPath);
        if (found != null)
            return found.transform;

        GameObject active = GameObject.Find(NormalizeFindPath(rawPath));
        return active != null ? active.transform : null;
    }

    /// <summary>
    /// Paths are saved without RoomLoadSandbox. While objects are parented under the sandbox
    /// during load, strip that prefix so exact path compares still work. Never suffix-match —
    /// boom trees have many nodes named AttachPoint and a suffix match parents to the wrong one.
    /// </summary>
    private static string GetLoadComparablePath(GameObject obj)
    {
        string path = GetGameObjectPath(obj);
        const string sandboxPrefix = "/RoomLoadSandbox";
        if (path.StartsWith(sandboxPrefix, StringComparison.Ordinal))
            path = path.Substring(sandboxPrefix.Length);
        if (string.IsNullOrEmpty(path))
            path = "/";
        else if (path[0] != '/')
            path = "/" + path;
        return path;
    }

    private void Awake()
    {
        if (Instance != null)
            Destroy(this.gameObject);

        Instance = this;
        CreateTracker();
        NewRoomSave();
        HandleBackwardsCompatibility();

    }

    private const string OperatingTableGuid = "gameobject_839a064b625fb724ab496f21e46986e7";

    private void Start()
    {
        duplicateRoom = FindObjectOfType<DuplicateRoom>();
        _ = SpawnDefaultOperatingTableIfMissingAsync();
    }

    /// <summary>
    /// Default new-room table comes from the selectable catalog (same path as LoadRoom /
    /// ObjectMenu), not a Main-scene PrefabInstance. The table prefab nests a .blend mesh
    /// that is itself an asset bundle; scene instances of that prefab resolve without a
    /// usable mesh, while catalog loads do not.
    /// </summary>
    private async Task SpawnDefaultOperatingTableIfMissingAsync()
    {
        while (Application.isPlaying && !SelectableAssetBundles.Initialized)
            await Task.Yield();
        if (!Application.isPlaying || IsLoading)
            return;

        bool alreadyPresent = FindObjectsByType<TrackedObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Any(IsOperatingTableObject);
        if (alreadyPresent)
            return;

        if (!SelectableAssetBundles.TryGetSelectableData(OperatingTableGuid, out SelectableData data))
        {
            Debug.LogError("[OR_Table] Catalog missing default operating table guid; new room has no table.");
            return;
        }

        // Same full-screen loader as LoadRoom (GetPrefab defaults to the compact Item indicator).
        GameObject prefab = await data.GetPrefab(loadingToken: Loading.GetLoadingToken());
        if (!Application.isPlaying || prefab == null || IsLoading)
            return;

        Transform room = duplicateRoom != null && duplicateRoom.currentRoom != null
            ? duplicateRoom.currentRoom.transform
            : GameObject.Find("Room1")?.transform;

        GameObject go = Instantiate(prefab);
        go.name = "OR_Table_0";
        if (room != null)
            go.transform.SetParent(room, false);

        go.transform.localPosition = new Vector3(0f, 0.05715f, 0f);
        go.transform.localRotation = new Quaternion(-0.5f, 0.5f, 0.5f, 0.5f);
        go.transform.localScale = Vector3.one;

        GameObject floor = GameObject.Find("RoomBoundary_Floor");
        if (floor != null && go.TryGetComponent<KeepRelativePosition>(out var keepRel))
            keepRel.VirtualParentChanged(floor.transform);

        foreach (DestroyOnLoad dol in go.GetComponentsInChildren<DestroyOnLoad>(true))
        {
            if (dol != null)
                Destroy(dol.gameObject);
        }

        PlacementLoadOptimizer.FinalizeInstanceColliders(go);
        PlacementLoadOptimizer.RestoreInstanceCollidersAfterLoad(go);
        go.SetActive(true);
    }

    private static bool IsOperatingTableObject(TrackedObject to)
    {
        if (to == null) return false;
        string blob = (to.name ?? "") + " " + (to.GetComponent<Selectable>()?.MetaData?.Name ?? "");
        return blob.IndexOf("OR_Table", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void HandleBackwardsCompatibility()
    {
        Version nukeBelowVersion = Version.Parse(_nukeBelowVersion);
        if (!PlayerPrefs.HasKey(_lastNukedSavesPlayerPrefsKey))
        {
            DeleteAllSaves();
        }
        else
        {
            string lastNukedString = PlayerPrefs.GetString(_lastNukedSavesPlayerPrefsKey);
            Version lastNukedVersion = Version.Parse(lastNukedString);
            if (lastNukedVersion.Major < nukeBelowVersion.Major)
            {
                DeleteAllSaves();
            }
        }
    }

    private void DeleteAllSaves()
    {
        string path = Application.persistentDataPath + "/Saved/";
        DeleteAllInDirectory(path);
        Debug.Log("Nuked all saved rooms");
        string configsPath = path + "Configs/";
        DeleteAllInDirectory(configsPath);
        Debug.Log("Nuked all saved arm configurations");
        PlayerPrefs.SetString(_lastNukedSavesPlayerPrefsKey, AppVersion.Number);
    }

    private void DeleteAllInDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            string[] files = Directory.GetFiles(path);
            foreach (string file in files.Where(x => x.EndsWith(".json")))
            {
                if (File.Exists(file))
                {
                    try { File.Delete(file); }
                    catch (IOException ex) { Debug.LogException(ex); }
                    catch { throw; }
                }
            }
        }
    }

    private Tracker CreateTracker()
    {
        _tracker = new Tracker { objects = new List<TrackedObject.Data>() };
        return _tracker;
    }

    private RoomConfiguration NewRoomSave()
    {
        _roomConfiguration = new RoomConfiguration() { collections = new List<Tracker>(), version = AppVersion.Number };
        return _roomConfiguration;
    }

    public async Task<bool> SaveScenario(string title, IProgress<float> progress = null)
    {
        try
        {
            progress?.Report(0f);
            await Task.Yield();
            SaveRoom(title);
            progress?.Report(1f);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveScenario] Failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    public async Task<bool> LoadScenario(string path, IProgress<float> progress = null)
    {
        try
        {
            progress?.Report(0f);
            await Task.Yield();
            LoadRoom(path);
            progress?.Report(1f);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LoadScenario] Failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    public void SaveConfiguration(string title)
    {
        string folder = GetSavedConfigsFolder();
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        string fileName = ReplaceInvalidChars(title.Replace(" ", "_"));
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        SaveConfigurationToPath(Path.Combine(folder, fileName));
    }

    /// <summary>Saves the selected assembly configuration to an explicit path (OS save dialog).</summary>
    public void SaveConfigurationToPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (Selectable.SelectedSelectables == null || Selectable.SelectedSelectables.Count == 0)
            throw new InvalidOperationException("Nothing selected to save as a configuration.");

        CreateTracker();
        TrackedObject[] foundObjects = Selectable.SelectedSelectables[0]
            .transform.root.GetComponentsInChildren<TrackedObject>();

        foreach (TrackedObject obj in foundObjects)
        {
            if (obj.TryGetComponent(out AttachmentPoint attachmentPoint))
                attachmentPoint.SetToOriginalParent();
        }

        foreach (TrackedObject obj in foundObjects)
            _tracker.objects.Add(obj.GetData());

        string json = JsonConvert.SerializeObject(_tracker, new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.Indented
        });

        string folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        if (File.Exists(path))
            File.Delete(path);

        File.WriteAllText(path, json);
        Debug.Log($"Saved Config: {path}");

        ObjectMenu.Instance?.AddCustomMenuItem(path);

        foreach (TrackedObject obj in foundObjects)
        {
            if (obj.TryGetComponent(out Selectable selectable))
            {
                if (!string.IsNullOrEmpty(selectable.guid))
                {
                    selectable.guid = Guid.NewGuid().ToString();
                    selectable.name = selectable.guid;
                }
            }
            if (obj.TryGetComponent(out AttachmentPoint attachmentPoint))
                attachmentPoint.SetToProperParent();
        }
    }

    public string ReplaceInvalidChars(string filename)
    {
        return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
    }



    public async void SaveRoom(string title, bool showSuccessDialog = true)
    {
        string folder = GetSavedRoomsFolder();
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        string fileName = ReplaceInvalidChars(title.Replace(" ", "_"));
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        SaveRoomToPath(Path.Combine(folder, fileName), showSuccessDialog);
    }

    /// <summary>Saves the room JSON to an explicit path (from the OS save dialog).</summary>
    public async void SaveRoomToPath(string path, bool showSuccessDialog = true)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string title = Path.GetFileNameWithoutExtension(path).Replace("_", " ");
        CurrentRoomSaveName = title;
        CreateTracker();
        NewRoomSave();
        var token = Loading.GetLoadingToken();

        _roomConfiguration.roomDimension = RoomSize.Instance.CurrentDimensions;
        // capture client metadata
        try
        {
            _roomConfiguration.clientAccountName = UI_ClientMetaData.AccountName;
            _roomConfiguration.clientAccountAddressLine1 = UI_ClientMetaData.AccountAddressLine1;
            _roomConfiguration.clientAccountAddressLine2 = UI_ClientMetaData.AccountAddressLine2;
            _roomConfiguration.clientProjectName = UI_ClientMetaData.ProjectName;
            _roomConfiguration.clientProjectNumber = UI_ClientMetaData.ProjectNumber;
            _roomConfiguration.clientOrderReferenceNumber = UI_ClientMetaData.OrderReferenceNumber;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not capture client metadata: {e.Message}");
        }

        TrackedObject[] foundObjects = FindObjectsOfType<TrackedObject>();

        foreach (TrackedObject obj in foundObjects)
        {
            if (obj.TryGetComponent(out AttachmentPoint attachmentPoint))
                attachmentPoint.SetToOriginalParent();
        }

        await Task.Delay(1000);
        token.SetProgress(0.33f);

        foreach (TrackedObject obj in foundObjects)
        {
            Transform topParent = obj.transform;
            while (topParent.parent != null) topParent = topParent.parent;
            if (obj.transform == obj.transform.root || topParent.name == "Room1")
            {
                CreateTracker();
                TrackedObject[] temps = obj.transform.GetComponentsInChildren<TrackedObject>();
                foreach (TrackedObject to in temps) _tracker.objects.Add(to.GetData());
                _roomConfiguration.collections.Add(_tracker);
            }
        }

        await Task.Delay(1000);
        token.SetProgress(0.66f);

        string json = JsonConvert.SerializeObject(_roomConfiguration, new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.Indented,
        });

        string folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        if (File.Exists(path))
            File.Delete(path);

        File.WriteAllText(path, json);
        Debug.Log($"Saved Room: {path}");

        await Task.Delay(1000);
        token.SetProgress(1);

        // Only list saves that live in the app's Saved folder.
        string savedRoot = Path.GetFullPath(GetSavedRoomsFolder())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(savedRoot, StringComparison.OrdinalIgnoreCase))
            RoomConfigLoader.Instance?.RefreshOrAddRoomItem(path);

        foreach (TrackedObject obj in foundObjects)
        {
            if (obj.TryGetComponent(out AttachmentPoint attachmentPoint))
                attachmentPoint.SetToProperParent();
        }

        if (showSuccessDialog)
        {
            UI_DialogPrompt.Open(
              $"Room saved to:\n{path}",
              new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = path),
              new ButtonAction("Done"));
        }
    }

    public async Task<GameObject> LoadArmAssembly(string file)
    {
        Debug.Log($"Loading config file at {file}");
        IsLoading = true;
        AssetPipelineDiagnostics.RoomLoadQuietMode = true;

        try
        {
            if (File.Exists(file))
            {
                CreateTracker();
                string json = File.ReadAllText(file);
                _tracker = JsonConvert.DeserializeObject<Tracker>(json);

                _newPoints = new List<AttachmentPoint>();
                _newObjects = new List<TrackedObject>();
                EnsureRoomLoadSandbox();
                await LoadAllObjectsIntoCache(_tracker.objects);
                await Task.Yield();
                await ProcessTrackedObjects(_tracker.objects);
                await Task.Yield();
                await SetObjectProperties(_newObjects);
                await Task.Yield();
                RandomizeInstanceGUIDs();
                var gameObject = GetRoot();

                foreach (var to in _newObjects)
                {
                    try { to.ApplySavedState(); }
                    catch (Exception ex) { Debug.LogWarning($"[LoadArmAssembly] ApplySavedState failed on {to.name}: {ex.Message}"); }
                }

                FinalizeLoadedInstanceColliders();
                RestoreAllLoadedTransforms();
                FinalizeLoadedAttachmentPoints();
                FixLoadedNonUniformDropTubeScales();
                BatchActivateLoadedObjects();
                CompleteDeferredSelectableInitialization();
                RestoreLoadedInstanceColliders();
                SettleLoadedBoomAssembly();
                FixLoadedNonUniformDropTubeScales();

                if (_newObjects == null || _newObjects.Count == 0)
                {
                    UI_DialogPrompt.Open(
                        "Could not reopen this configuration.\n"
                        + "Required catalog items are missing or offline.\n"
                        + "Check the console for missing GUID details.",
                        new ButtonAction("OK"));
                    if (gameObject != null)
                        Destroy(gameObject);
                    return null;
                }

                OnConfigurationLoadComplete?.Invoke(gameObject);
                return gameObject;
            }
            else
            {
                UI_DialogPrompt.Open(
                    "This configuration file no longer exists.\n"
                    + "Saved configs must live under Saved/Configs to reopen after restart.",
                    new ButtonAction("OK"));
                Debug.LogError($"File at {file} no longer exists");
                return null;
            }
        }
        finally
        {
            AssetPipelineDiagnostics.RoomLoadQuietMode = false;
            IsLoading = false;
            DestroyRoomLoadSandbox();
        }
    }

    public void LoadRoom(string file)
    {
        AssetPipelineDiagnostics.Log("RoomLoad", $"LoadRoom file='{file}' exists={File.Exists(file)}");
        Debug.Log($"Loading Room at {file}");

        if (!File.Exists(file))
        {
            UI_DialogPrompt.Open(
                "This room save file no longer exists.\n"
                + "Room saves must live under the AppData Saved folder to reopen.",
                new ButtonAction("OK"));
            return;
        }

        CurrentRoomSaveName = Path.GetFileNameWithoutExtension(file).Replace("_", " ");
        CreateTracker();
        string json = File.ReadAllText(file);
        _roomConfiguration = JsonConvert.DeserializeObject<RoomConfiguration>(json);

        Debug.Log("Clearing default room objects");
        List<TrackedObject> existingObjects = FindObjectsOfType<TrackedObject>().ToList();

        foreach (TrackedObject to in existingObjects)
        {
            if (to == null) continue;
            Transform topParent = to.transform;
            while (topParent.parent != null) topParent = topParent.parent;
            if (to.transform == to.transform.root
                || topParent.name == duplicateRoom.currentRoom.name
                   && !IsBaseboard(to.GetData())
                   && !IsWallProtector(to.GetData())
                   && !IsRoomBoundary(to.GetData()))
            {
                Destroy(to.gameObject);
            }
        }
        existingObjects.Clear();
        existingObjects.TrimExcess();

        Selectable.DestroyAll();

        // Sticky proposal title from a prior room must not leak into this load.
        ProposalPreviewModel.ConfigNameOverride = null;

        TryRestoreClientMetaData();
        LoadRoom();
    }

    private void TryRestoreClientMetaData()
    {
        try
        {
            // We need to find the UI_ClientMetaData instance in scene (it sets itself DontDestroyOnLoad)
            var ui = FindObjectOfType<UI_ClientMetaData>(true);
            if (ui == null) return; // nothing to restore

            // Use reflection-safe approach since fields are private serialized
            // We directly set the TMP_InputField.text via serialized backing fields if accessible
            SetTMPField(ui, "InputFieldAccountName", _roomConfiguration.clientAccountName);
            SetTMPField(ui, "InputFieldAccountAddressLine1", _roomConfiguration.clientAccountAddressLine1);
            SetTMPField(ui, "InputFieldAccountAddressLine2", _roomConfiguration.clientAccountAddressLine2);
            SetTMPField(ui, "InputFieldProjectName", _roomConfiguration.clientProjectName);
            SetTMPField(ui, "InputFieldProjectNumber", _roomConfiguration.clientProjectNumber);
            SetTMPField(ui, "InputFieldOrderReferenceNumber", _roomConfiguration.clientOrderReferenceNumber);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to restore client meta data: {e.Message}");
        }
    }

    private void SetTMPField(UI_ClientMetaData ui, string fieldName, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var type = typeof(UI_ClientMetaData);
        var prop = type.GetProperty(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (prop != null)
        {
            var field = prop.GetValue(ui) as TMPro.TMP_InputField;
            if (field != null)
            {
                field.text = value;
            }
        }
        else
        {
            // fallback attempt for backing field
            var f = type.GetField("<" + fieldName + ">k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null)
            {
                var fieldObj = f.GetValue(ui) as TMPro.TMP_InputField;
                if (fieldObj != null) fieldObj.text = value;
            }
        }
    }

    private async void LoadRoom()
    {
        IsLoading = true;
        AssetPipelineDiagnostics.RoomLoadQuietMode = true;
        var token = Loading.GetLoadingToken();
        var loadTimer = Stopwatch.StartNew();
        AssetPipelineDiagnostics.Log("RoomLoad", $"LoadRoom async — {_roomConfiguration.collections.Count} collection(s), platform={Application.platform}");

        try
        {
            RoomSize.SetDimensions(_roomConfiguration.roomDimension);
            EnsureRoomLoadSandbox();

            var allTrackedObjects = _roomConfiguration.collections
                .SelectMany(c => c.objects)
                .ToList();

            var prefetchTimer = Stopwatch.StartNew();
            await LoadAllObjectsIntoCache(allTrackedObjects);
            prefetchTimer.Stop();
            AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "prefetchBundles", prefetchTimer.ElapsedMilliseconds,
                $"{allTrackedObjects.Count} tracked object(s) across {_roomConfiguration.collections.Count} collection(s)");

            float progressionTicks = 1f / _roomConfiguration.collections.Count;
            float progression = 0;
            foreach (Tracker t in _roomConfiguration.collections)
            {
                _newPoints = new List<AttachmentPoint>();
                _newObjects = new List<TrackedObject>();

                await Task.Yield();

                var processTimer = Stopwatch.StartNew();
                await ProcessTrackedObjects(t.objects);
                processTimer.Stop();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "processTrackedObjects", processTimer.ElapsedMilliseconds);

                long gapStartTick = Stopwatch.GetTimestamp();
                await Task.Yield();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "mainThread_gap_after_process",
                    ElapsedMsSince(gapStartTick));

                var propsTimer = Stopwatch.StartNew();
                await SetObjectProperties(_newObjects);
                propsTimer.Stop();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "setObjectProperties", propsTimer.ElapsedMilliseconds);

                await Task.Yield();

                var savedStateTimer = Stopwatch.StartNew();
                foreach (var to in _newObjects)
                {
                    try { to.ApplySavedState(); }
                    catch (Exception ex) { Debug.LogWarning($"[LoadRoom] ApplySavedState failed on {to.name}: {ex.Message}"); }
                }
                savedStateTimer.Stop();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "applySavedState", savedStateTimer.ElapsedMilliseconds,
                    $"{_newObjects.Count} object(s)");

                var colliderTimer = Stopwatch.StartNew();
                FinalizeLoadedInstanceColliders();
                colliderTimer.Stop();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "finalizeInstanceColliders", colliderTimer.ElapsedMilliseconds,
                    $"{_newObjects.Count} object(s)");

                var guidTimer = Stopwatch.StartNew();
                RandomizeInstanceGUIDs();
                guidTimer.Stop();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "randomizeGuids", guidTimer.ElapsedMilliseconds);

                // Re-apply every saved transform in the canonical (pre-MoveUp) hierarchy.
                // Pass2 restored children before pass4 fixed AP locals (e.g. drop-tube AP scaleZ=5).
                var transformTimer = Stopwatch.StartNew();
                RestoreAllLoadedTransforms();
                LogLoadedArmScaleSnapshot("after RestoreAllLoadedTransforms");
                transformTimer.Stop();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "restoreAllTransforms", transformTimer.ElapsedMilliseconds,
                    $"{_newObjects.Count} root(s)");

                // MoveUp must run AFTER canonical transforms, BEFORE activation/settle.
                var attachmentTimer = Stopwatch.StartNew();
                FinalizeLoadedAttachmentPoints();
                FixLoadedNonUniformDropTubeScales();
                LogLoadedArmScaleSnapshot("after FinalizeLoadedAttachmentPoints");
                attachmentTimer.Stop();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "finalizeAttachmentPoints", attachmentTimer.ElapsedMilliseconds);

                var activateTimer = Stopwatch.StartNew();
                BatchActivateLoadedObjects();
                LogLoadedArmScaleSnapshot("after BatchActivateLoadedObjects");
                activateTimer.Stop();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "batchActivate", activateTimer.ElapsedMilliseconds,
                    $"{_newObjects.Count} object(s)");

                var deferredInitTimer = Stopwatch.StartNew();
                CompleteDeferredSelectableInitialization();
                LogLoadedArmScaleSnapshot("after CompleteDeferredSelectableInitialization");
                deferredInitTimer.Stop();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "completeDeferredInit", deferredInitTimer.ElapsedMilliseconds,
                    $"{_newObjects.Count} object(s)");

                var restoreColliderTimer = Stopwatch.StartNew();
                int restoredColliderCount = RestoreLoadedInstanceColliders();
                restoreColliderTimer.Stop();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "restoreInstanceColliders", restoreColliderTimer.ElapsedMilliseconds,
                    $"{restoredColliderCount} mesh collider(s) on {_newObjects.Count} object(s)");

                SettleLoadedBoomAssembly();
                FixLoadedNonUniformDropTubeScales();
                LogLoadedArmScaleSnapshot("after SettleLoadedBoomAssembly");

                progression += progressionTicks;
                token.SetProgress(progression);
            }

            var completeTimer = Stopwatch.StartNew();
            Selectable.NotifyActiveSelectablesInSceneChanged();
            OnRoomLoadComplete?.Invoke();
            completeTimer.Stop();
            AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "onRoomLoadComplete", completeTimer.ElapsedMilliseconds);

            loadTimer.Stop();
            AssetPipelineDiagnostics.Log("RoomLoad", $"LoadRoom complete — total {loadTimer.ElapsedMilliseconds}ms");
        }
        catch { throw; }
        finally
        {
            AssetPipelineDiagnostics.RoomLoadQuietMode = false;
            IsLoading = false;
            DestroyRoomLoadSandbox();
            token.SetProgress(1f);
        }
    }

    // --- Multi pass state ---
    private Dictionary<string, GameObject> _guidToGameObject = new();
    private List<(GameObject obj, TrackedObject.Data data)> _pendingSetup = new();
    public List<TrackedObject.Data> _pendingEmbedded = new();
    private List<TrackedObject.Data> _pendingAttachmentPoints = new();
    private Transform _roomLoadSandbox;

    private Transform EnsureRoomLoadSandbox()
    {
        if (_roomLoadSandbox != null)
            return _roomLoadSandbox;

        var sandboxGo = new GameObject("RoomLoadSandbox");
        sandboxGo.SetActive(false);
        _roomLoadSandbox = sandboxGo.transform;
        return _roomLoadSandbox;
    }

    private void DestroyRoomLoadSandbox()
    {
        if (_roomLoadSandbox == null)
            return;

        Destroy(_roomLoadSandbox.gameObject);
        _roomLoadSandbox = null;
    }

    private async Task ProcessTrackedObjects(List<TrackedObject.Data> trackedObjects)
    {
        _guidToGameObject.Clear();
        _pendingSetup.Clear();
        _pendingEmbedded.Clear();
        _pendingAttachmentPoints.Clear();
        _newObjects = new List<TrackedObject>();
        _newPoints = new List<AttachmentPoint>();

        int instantiateCount = 0;
        var pass1Timer = Stopwatch.StartNew();

        // Pass 1: instantiate selectables (skip embedded & attachment points & room boundaries)
        foreach (TrackedObject.Data data in trackedObjects)
        {
            GameObject go = null;

            if (IsRoomBoundary(data) || IsBaseboard(data) || IsWallProtector(data))
            {
                go = IsRoomBoundary(data) ? GetRoomBoundary(data) : GetGameObjectWithGuidName(data);
                if (go != null && go.GetComponent<Selectable>() != null)
                {
                    LogData(go.GetComponent<Selectable>(), data);
                    ResetMaterialPalettes(go.GetComponent<TrackedObject>());
                }

                continue;
            }

            if (data.isAttachmentPoint || data.global_guid == _attachPointGUID)
            {
                _pendingAttachmentPoints.Add(data);
                continue;
            }

            // Embedded selectable (no global guid) - resolve later
            if (string.IsNullOrEmpty(data.global_guid))
            {
                _pendingEmbedded.Add(data);
                continue;
            }

            // Instantiate selectable prefab
            if (IsLoading)
            {
                go = InstantiateObjectForLoad(data);
            }
            else
            {
                var task = InstantiateObject(data);
                await task;
                if (!Application.isPlaying) throw new AppQuitInTaskException();
                go = task.Result;
            }

            if (go == null)
            {
                Debug.LogError($"Failed to instantiate object with guid: {data.global_guid}");
                continue;
            }

            instantiateCount++;

            _pendingSetup.Add((go, data));

            var trackedObj = go.GetComponent<TrackedObject>();
            if (trackedObj != null)
                _newObjects.Add(trackedObj);
        }

        RegisterGuidsForLoadedObjects();
        RemoveDestroyOnLoadFromLoadedObjects();

        pass1Timer.Stop();
        AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "pass1_instantiate", pass1Timer.ElapsedMilliseconds,
            $"{instantiateCount} object(s)");

        // Pass 2: establish hierarchy, restore saved values, apply transforms
        var pass2Timer = Stopwatch.StartNew();
        foreach ((GameObject go, TrackedObject.Data data) in _pendingSetup)
        {
            var trackedObj = go.GetComponent<TrackedObject>();

            Transform parent = null;
            if (!string.IsNullOrEmpty(data.parentGuid) && _guidToGameObject.TryGetValue(data.parentGuid, out var parentGO))
                parent = parentGO.transform;

            if (parent == null)
                parent = FindLoadedParentTransform(data);

            if (parent != null)
                go.transform.SetParent(parent, false);
            else
            {
                go.transform.SetParent(null, true);
                if (!string.IsNullOrEmpty(data.parentPath) || !string.IsNullOrEmpty(data.parent))
                {
                    Debug.LogError(
                        $"[RoomLoad] Orphaned '{data.UIButtonname ?? data.objectName}' — " +
                        $"could not resolve parentPath='{data.parentPath}' parent='{data.parent}' parentGuid='{data.parentGuid}'");
                }
            }

            if (trackedObj != null)
            {
                trackedObj.StoreValues(data);
                ResetMaterialPalettes(trackedObj);
            }

            if (parent != null)
                trackedObj?.RestoreTransform(isRoot: false);
            else
                trackedObj?.RestoreTransform(isRoot: true);

            if (!string.IsNullOrEmpty(data.keepRelativePositionParentName) && go.TryGetComponent<KeepRelativePosition>(out var comp))
                comp.ParentName = data.keepRelativePositionParentName;
        }
        _pendingSetup.Clear();
        pass2Timer.Stop();
        AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "pass2_hierarchy", pass2Timer.ElapsedMilliseconds,
            $"{instantiateCount} object(s)");

        // Pass 3: embedded selectables
        var pass3Timer = Stopwatch.StartNew();
        foreach (var emb in _pendingEmbedded)
        {
            ProcessEmbeddedSelectable(emb);
        }
        pass3Timer.Stop();
        if (_pendingEmbedded.Count > 0)
            AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "pass3_embedded", pass3Timer.ElapsedMilliseconds,
                $"{_pendingEmbedded.Count} object(s)");

        // Pass 4: attachment points
        var pass4Timer = Stopwatch.StartNew();
        foreach (var apData in _pendingAttachmentPoints)
            ProcessAttachmentPoint(apData);
        pass4Timer.Stop();
        if (_pendingAttachmentPoints.Count > 0)
            AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "pass4_attachmentPoints", pass4Timer.ElapsedMilliseconds,
                $"{_pendingAttachmentPoints.Count} object(s)");
    }

    // GameObject.Find only searches active objects, so embedded boom parts that are still
    // inactive at Pass 3 (e.g. unselected scale-level siblings) were never found. Search the
    // freshly-instantiated hierarchy (including inactive objects) first.
    private GameObject FindInLoadedObjects(string rawPath)
    {
        if (string.IsNullOrEmpty(rawPath) || _newObjects == null)
            return null;

        string pathWithSlash = rawPath[0] == '/' ? rawPath : "/" + rawPath;
        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;

            foreach (Transform t in to.GetComponentsInChildren<Transform>(true))
            {
                if (GetLoadComparablePath(t.gameObject) == pathWithSlash)
                    return t.gameObject;
            }
        }
        return null;
    }

    private GameObject ProcessEmbeddedSelectable(TrackedObject.Data to)
    {
        try
        {
            // Try to find the embedded object by selfPath, parent, or parentPath
            GameObject go = null;
            if (!string.IsNullOrEmpty(to.selfPath))
                go = FindInLoadedObjects(to.selfPath) ?? GameObject.Find(NormalizeFindPath(to.selfPath));
            if (go == null && !string.IsNullOrEmpty(to.parent))
                go = FindInLoadedObjects(to.parent) ?? GameObject.Find(NormalizeFindPath(to.parent));
            if (go == null && !string.IsNullOrEmpty(to.parentPath))
                go = FindInLoadedObjects(to.parentPath) ?? GameObject.Find(NormalizeFindPath(to.parentPath));
            if (go == null)
                throw new NullReferenceException($"Could not find embedded selectable for path: {to.selfPath} or parent: {to.parent}");

            // Store all values from data
            var trackedObj = go.GetComponent<TrackedObject>();
            if (trackedObj != null)
            {
                trackedObj.StoreValues(to);
                trackedObj.RestoreTransform();
            }


            return go;
        }
        catch (NullReferenceException nullException)
        {
            Debug.LogError($"ProcessEmbeddedSelectable failed to find {to.parent}");
            Debug.LogError(nullException);
            return null;
        }
    }
    private void ResetMaterialPalettes(TrackedObject obj)
    {
        if (obj == null)
        {
            Debug.LogWarning("Attempted to reset materials of a missing TrackedObject reference.");
            return;
        }

        obj.RestoreMaterials();
    }

    /// <summary>
    /// Applies saved local/world transforms to every TrackedObject under loaded roots,
    /// including embedded selectables and attachment points. Must run while the hierarchy
    /// is still in canonical (pre-MoveUp) form.
    /// </summary>
    private void RestoreAllLoadedTransforms()
    {
        if (_newObjects == null)
            return;

        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;

            foreach (TrackedObject childTo in to.GetComponentsInChildren<TrackedObject>(true))
            {
                if (childTo == null || !childTo.HasStoredValues)
                    continue;

                bool isRoot = childTo.transform.parent == null || childTo.transform == childTo.transform.root;
                try { childTo.RestoreTransform(isRoot: isRoot); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RestoreAllLoadedTransforms] failed on {childTo.name}: {ex.Message}");
                }
            }
        }

        // Light drop tubes save tube Z but often omit inverse AttachmentPoint Z.
        // Re-derive that compensation so arms don't inherit tube squash/shear.
        FixLoadedNonUniformDropTubeScales();
    }

    /// <summary>
    /// Boom drop tubes persist compensating AttachPoint.z (=1/tube.z). Light drop tubes
    /// usually do not. Without that inverse, the whole arm inherits tube.lossyScale.z
    /// and shears under rotation. Also repairs zeroed inner light mesh roots.
    /// </summary>
    private void FixLoadedNonUniformDropTubeScales()
    {
        if (_newObjects == null)
            return;

        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;

            foreach (Transform t in to.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                    continue;

                // Exact inner mesh container that logs showed stuck at (0,0,0).
                if (t.name == "Simeon_Light_7000" && t.localScale.sqrMagnitude < 1e-8f)
                {
                    t.localScale = Vector3.one;
                    AssetPipelineDiagnostics.Log("RoomLoad.Scale",
                        $"Repaired zero localScale on {GetLoadComparablePath(t.gameObject)}");
                }

                if (t.name.IndexOf("DropTube.001", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                float tubeZ = t.localScale.z;
                if (tubeZ <= 0.0001f || Mathf.Abs(tubeZ - 1f) < 0.0001f)
                    continue;

                float inv = 1f / tubeZ;
                for (int i = 0; i < t.childCount; i++)
                {
                    Transform child = t.GetChild(i);
                    bool isAttach =
                        child.GetComponent<AttachmentPoint>() != null
                        || child.name.Equals("AttachmentPoint", StringComparison.OrdinalIgnoreCase)
                        || child.name.Equals("AttachPoint", StringComparison.OrdinalIgnoreCase);
                    if (!isAttach)
                        continue;

                    Vector3 cls = child.localScale;
                    if (Mathf.Abs(cls.z * tubeZ - 1f) < 0.05f)
                        continue;

                    child.localScale = new Vector3(
                        Mathf.Abs(cls.x) < 1e-6f ? 1f : cls.x,
                        Mathf.Abs(cls.y) < 1e-6f ? 1f : cls.y,
                        inv);
                    AssetPipelineDiagnostics.Log("RoomLoad.Scale",
                        $"Compensate {child.name} under {t.name}: localScale.z {cls.z:F3} -> {inv:F3} (tubeZ={tubeZ:F3})");
                }
            }

            foreach (Selectable sel in to.GetComponentsInChildren<Selectable>(true))
            {
                if (sel == null) continue;
                try { sel.ReapplyInverseChildScalingAfterLoad(); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FixLoadedNonUniformDropTubeScales] {sel.name}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// After MoveUp + activation: reassemble boom heads and refresh UVs.
    /// Does not re-apply saved locals — that would undo MoveUp parent changes.
    /// </summary>
    private void SettleLoadedBoomAssembly()
    {
        if (_newObjects == null)
            return;

        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;

            foreach (BoomHeadScaleHandler boomHead in to.GetComponentsInChildren<BoomHeadScaleHandler>(true))
            {
                if (boomHead == null) continue;
                if (!boomHead.TryGetComponent(out Selectable sel) || sel.CurrentScaleLevel == null)
                    continue;
                try { boomHead.ReassembleRows(sel.CurrentScaleLevel); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SettleLoadedBoomAssembly] ReassembleRows failed on {boomHead.name}: {ex.Message}");
                }
            }
        }

        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;
            foreach (var uv in to.GetComponentsInChildren<SetUVToWorld>(true))
            {
                try { uv.RefreshUVs(); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SettleLoadedBoomAssembly] RefreshUVs failed on {to.name}: {ex.Message}");
                }
            }
        }
    }

    private void LogLoadedArmScaleSnapshot(string phase)
    {
        if (_newObjects == null)
            return;

        foreach (TrackedObject root in _newObjects)
        {
            if (root == null)
                continue;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                    continue;

                string n = t.name;
                bool relevant =
                    n.IndexOf("ArmDropTube", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("BoomDropTube", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("AttachmentPoint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.Equals("AttachPoint", StringComparison.OrdinalIgnoreCase) ||
                    n.IndexOf("ArmSegment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Cardanic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Simeon_Light", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("LightHead", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!relevant)
                    continue;

                Selectable selectable = t.GetComponent<Selectable>();
                string selectedScale = selectable?.CurrentScaleLevel != null
                    ? $" currentScale(size={selectable.CurrentScaleLevel.Size}, scaleZ={selectable.CurrentScaleLevel.ScaleZ})"
                    : "";
                string saved = t.TryGetComponent(out TrackedObject tracked) && tracked.HasStoredValues
                    ? $" savedLocalScale={tracked.data.localScale}"
                    : "";

                AssetPipelineDiagnostics.Log("RoomLoad.Scale",
                    $"{phase}: {GetLoadComparablePath(t.gameObject)} localScale={t.localScale} lossyScale={t.lossyScale} localRot={t.localEulerAngles}{selectedScale}{saved}");
            }
        }
    }
    private GameObject InstantiateObjectForLoad(TrackedObject.Data trackedObject)
    {
        if (!SelectableAssetBundles.TryGetSelectableData(trackedObject.global_guid, out SelectableData data))
        {
            Debug.LogError($"Could not find selectable data for {trackedObject.objectName} with guid {trackedObject.global_guid}");
            return null;
        }

        if (!AssetBundleManager.TryGetCachedAsset(data.AssetBundleName, out GameObject prefab) || prefab == null)
        {
            Debug.LogError($"Prefab not in cache for guid {trackedObject.global_guid} (bundle {data.AssetBundleName})");
            return null;
        }

        GameObject go = Instantiate(prefab);
        go.SetActive(false);
        go.transform.SetParent(EnsureRoomLoadSandbox(), false);

        if (go.TryGetComponent<RestorePositionOnLoad>(out var compRestore))
            compRestore.PositionToRestore = trackedObject.worldPosition;

        if (!string.IsNullOrEmpty(trackedObject.instance_guid))
            go.name = trackedObject.instance_guid;

        if (go.TryGetComponent<Selectable>(out var selectable))
        {
            selectable.guid = trackedObject.instance_guid;
            selectable.UIButtonName = trackedObject.UIButtonname;
        }

        return go;
    }

    private void RegisterGuidsForLoadedObjects()
    {
        foreach ((GameObject go, TrackedObject.Data data) in _pendingSetup)
        {
            if (!string.IsNullOrEmpty(data.instance_guid))
                _guidToGameObject[data.instance_guid] = go;

            foreach (Selectable sel in go.GetComponentsInChildren<Selectable>(true))
            {
                if (!string.IsNullOrEmpty(sel.guid))
                    _guidToGameObject[sel.guid] = sel.gameObject;
            }
        }
    }

    private void RemoveDestroyOnLoadFromLoadedObjects()
    {
        foreach ((GameObject go, _) in _pendingSetup)
        {
            foreach (DestroyOnLoad dol in go.GetComponentsInChildren<DestroyOnLoad>(true))
            {
                if (dol != null)
                    Destroy(dol.gameObject);
            }
        }
    }

    private async Task<GameObject> InstantiateObject(TrackedObject.Data trackedObject)
    {
        if (!AssetPipelineDiagnostics.RoomLoadQuietMode)
        {
            AssetPipelineDiagnostics.Log("RoomLoad.Instantiate",
                $"objectName='{trackedObject.objectName}' global_guid='{trackedObject.global_guid}' instance_guid='{trackedObject.instance_guid}'");
        }

        if (!SelectableAssetBundles.TryGetSelectableData(trackedObject.global_guid, out SelectableData data))
        {
            Debug.LogError($"Could not find selectable data for {trackedObject.objectName} with guid {trackedObject.global_guid}");
            return null;
        }

        GameObject prefab;
        if (IsLoading && AssetBundleManager.TryGetCachedAsset(data.AssetBundleName, out GameObject cachedPrefab))
        {
            prefab = cachedPrefab;
        }
        else
        {
            var task = data.GetPrefab();
            await task;
            if (!Application.isPlaying) throw new AppQuitInTaskException();
            prefab = task.Result;
        }

        if (prefab == null)
        {
            Debug.LogError($"AssetBundle returned null prefab for guid {trackedObject.global_guid}");
            AssetPipelineDiagnostics.Log("RoomLoad.Instantiate", $"GetPrefab NULL for guid {trackedObject.global_guid}");
            return null;
        }

        if (!AssetPipelineDiagnostics.RoomLoadQuietMode)
            AssetPipelineDiagnostics.LogPrefabSnapshot("RoomLoad.Instantiate", prefab, "prefab before Instantiate");

        int rowCount1 = 0;
        var instantiateTimer = Stopwatch.StartNew();
        GameObject go = Instantiate(prefab);
        instantiateTimer.Stop();

        if (!AssetPipelineDiagnostics.RoomLoadQuietMode)
        {
            AssetPipelineDiagnostics.LogElapsed("RoomLoad.Instantiate", "Instantiate", instantiateTimer);
            AssetPipelineDiagnostics.LogPrefabSnapshot("RoomLoad.Instantiate", go, $"instance '{trackedObject.objectName}'");
        }

        var dolComps = go.GetComponentsInChildren<DestroyOnLoad>(true);
        Array.ForEach(dolComps, comp => { if (comp != null) Destroy(comp.gameObject); });

        if (go.TryGetComponent<RestorePositionOnLoad>(out var compRestore))
            compRestore.PositionToRestore = trackedObject.worldPosition;

        if (!string.IsNullOrEmpty(trackedObject.instance_guid))
            go.name = trackedObject.instance_guid; // retain original until randomized later

        var selectable = go.GetComponent<Selectable>();
        if (selectable != null)
        {
            selectable.guid = trackedObject.instance_guid;
            selectable.UIButtonName = trackedObject.UIButtonname;
            LogData(selectable, trackedObject);

            if (!IsLoading &&
                selectable.SpecialTypes != null &&
                selectable.SpecialTypes.Count > 0 &&
                selectable.SpecialTypes[0] == SpecialSelectableType.Door)
            {
                var wc = selectable.GetComponentInChildren<WallCutter>();
                if (wc != null) wc.UpdateCuts();
            }


            if (selectable != null && selectable.MetaData != null && selectable.MetaData.Name == "NewBoomHead")
            {
                var boomHeadHandler = selectable.GetComponent<BoomHeadScaleHandler>();
                if (boomHeadHandler != null)
                {
                    var currentScale = selectable.CurrentPreviewScaleLevel;
                    if (currentScale != null && currentScale.TryGetValue("rows", out string rowsStr))
                    {
                        if (int.TryParse(rowsStr, out int rowCount))
                        {
                           rowCount1 = rowCount;
                        }
                    }
                    boomHeadHandler.ReassembleRowsCount(rowCount1);

                }
            }

            // Pricing is restored from save data via StoreValues; avoid 600 async pricing lookups during load.
            if (!IsLoading)
                ObjectMenu.Instance.HandleOutletAndPricing(go, trackedObject.UIButtonname);
        }
        return go;
    }

    private void ProcessAttachmentPoint(TrackedObject.Data to)
    {
        GameObject apGO = null;
        if (!string.IsNullOrEmpty(to.instance_guid))
            _guidToGameObject.TryGetValue(to.instance_guid, out apGO);

        if (apGO == null && !string.IsNullOrEmpty(to.selfPath))
            apGO = FindInLoadedObjects(to.selfPath) ?? GameObject.Find(NormalizeFindPath(to.selfPath));
        if (apGO == null && !string.IsNullOrEmpty(to.parent))
            apGO = FindInLoadedObjects(to.parent) ?? GameObject.Find(NormalizeFindPath(to.parent));
        if (apGO == null && !string.IsNullOrEmpty(to.parentPath))
            apGO = FindInLoadedObjects(to.parentPath) ?? GameObject.Find(NormalizeFindPath(to.parentPath));

        if (apGO == null)
        {
            Debug.LogError($"Could not find attachment point for {to.selfPath ?? to.parentPath} (GUID: {to.instance_guid})");
            return;
        }

        // Prefer the AttachmentPoint on the resolved node (selfPath), not every nested AP under a parent.
        AttachmentPoint attPoint = apGO.GetComponent<AttachmentPoint>();
        if (attPoint == null)
            attPoint = apGO.GetComponentInChildren<AttachmentPoint>(true);
        if (attPoint == null)
        {
            Debug.LogError($"Expected AttachmentPoint on {to.selfPath ?? to.parentPath} but none found.");
            return;
        }

        if (!attPoint.TryGetComponent<TrackedObject>(out var trackedObject))
        {
            Debug.LogError($"AttachmentPoint at {to.selfPath ?? to.parentPath} did not have TrackedObject component");
            return;
        }

        trackedObject.StoreValues(to);
        // Save captures AP locals after length/scale (e.g. drop-tube AP z=0.675, scaleZ=5).
        // Without this, children restore against prefab AP pose and the whole boom chain shifts.
        trackedObject.RestoreTransform(isRoot: false);
        AssetPipelineDiagnostics.Log("RoomLoad.AttachmentPoint",
            $"Restored AP '{attPoint.name}' localPos={to.localPosition} localScale={to.localScale}");

        // Boom parts stay inactive until BatchActivate — only wire direct selectable children
        // of this AP (nested APs are processed as their own saved rows).
        for (int i = 0; i < attPoint.transform.childCount; i++)
        {
            Transform child = attPoint.transform.GetChild(i);
            if (!child.TryGetComponent<Selectable>(out var childSelectable))
                continue;

            attPoint.SetAttachedSelectable(childSelectable);
            childSelectable.ParentAttachmentPoint = attPoint;
        }

        _newPoints.Add(attPoint);
    }

    private void FinalizeLoadedAttachmentPoints()
    {
        // Depth-first so parent APs MoveUp before children that depend on them.
        var moveUpPoints = new List<AttachmentPoint>();
        foreach (TrackedObject to in _newObjects)
        {
            if (to == null) continue;
            foreach (AttachmentPoint ap in to.GetComponentsInChildren<AttachmentPoint>(true))
            {
                if (ap != null && ap.MoveUpOnAttach)
                    moveUpPoints.Add(ap);
            }
        }

        moveUpPoints.Sort((a, b) => GetHierarchyDepth(a.transform).CompareTo(GetHierarchyDepth(b.transform)));
        foreach (AttachmentPoint ap in moveUpPoints)
            ap.ApplyProperParentImmediate();
    }

    private static int GetHierarchyDepth(Transform t)
    {
        int depth = 0;
        while (t != null)
        {
            depth++;
            t = t.parent;
        }
        return depth;
    }

    private void CompleteDeferredSelectableInitialization()
    {
        foreach (TrackedObject to in _newObjects)
        {
            if (to == null) continue;
            foreach (Selectable selectable in to.GetComponentsInChildren<Selectable>(true))
                selectable.CompleteDeferredLoadInitialization();
        }
    }

    private void FinalizeLoadedInstanceColliders()
    {
        if (_newObjects == null)
            return;

        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;

            PlacementLoadOptimizer.FinalizeInstanceColliders(to.gameObject);
        }
    }

    private int RestoreLoadedInstanceColliders()
    {
        if (_newObjects == null)
            return 0;

        int restored = 0;
        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;

            restored += PlacementLoadOptimizer.RestoreInstanceCollidersAfterLoad(to.gameObject);

            foreach (AttachmentPoint attachmentPoint in to.GetComponentsInChildren<AttachmentPoint>(true))
            {
                try { attachmentPoint.RefreshStatusForLoad(); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RestoreLoadedInstanceColliders] RefreshStatusForLoad failed on {attachmentPoint.name}: {ex.Message}");
                }
            }
        }

        return restored;
    }

    private void BatchActivateLoadedObjects()
    {
        if (_newObjects == null)
            return;

        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;

            if (to.data.activeSelf)
                to.gameObject.SetActive(true);
        }
    }

    private static long ElapsedMsSince(long startTimestamp)
        => (Stopwatch.GetTimestamp() - startTimestamp) * 1000 / Stopwatch.Frequency;

    private async Task SetObjectProperties(List<TrackedObject> newObjects)
    {
        newObjects.Reverse();
        await Task.Yield();
        if (!Application.isPlaying) throw new AppQuitInTaskException();
    }

    private void RandomizeInstanceGUIDs()
    {
        foreach (TrackedObject to in _newObjects)
        {
            if (to == null) continue;
            if (to.transform.root == to.transform && to.TryGetComponent(out Selectable sel))
            {
                string newGuid = Guid.NewGuid().ToString();
                sel.guid = newGuid;
                to.data.instance_guid = newGuid;
                to.gameObject.name = newGuid;
                _guidToGameObject[newGuid] = to.gameObject; // keep dictionary aligned
            }
        }
    }

    private void LogData(Selectable s, TrackedObject.Data to) => s.GetComponent<TrackedObject>().StoreValues(to);
    private GameObject GetRoot() => _newObjects.SingleOrDefault(x => x.transform == x.transform.root)?.gameObject;

    public static bool IsRoomBoundary(string guid) => guid == "Wall_N" || guid == "Wall_S" || guid == "Wall_E" || guid == "Wall_W" || guid == "Ceil" || guid == "Floor";
    public static bool IsBaseboard(string guid) => guid.StartsWith("Baseboard");
    public static bool IsWallProtector(string guid) => guid.StartsWith("WallProtector");
    private static bool IsBaseboard(TrackedObject.Data to) => IsBaseboard(to.global_guid);
    public static bool IsWallProtector(TrackedObject.Data to) => IsWallProtector(to.global_guid);
    public static bool IsRoomBoundary(TrackedObject.Data to) => IsRoomBoundary(to.global_guid);
    private static GameObject GetRoomBoundary(TrackedObject.Data to) => GameObject.Find("RoomBoundary_" + to.global_guid);
    private static GameObject GetGameObjectWithGuidName(TrackedObject.Data to) => GameObject.Find(to.global_guid);

    public static string GetGameObjectPath(GameObject obj)
    {
        string path = "/" + obj.name;
        while (obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;
            path = "/" + obj.name + path;
        }
        return path;
    }

    private async Task LoadAllObjectsIntoCache(List<TrackedObject.Data> trackedObjects)
    {
        var missingGuids = new List<string>();
        var bundleNames = new HashSet<string>();

        foreach (TrackedObject.Data to in trackedObjects)
        {
            if (IsRoomBoundary(to) || IsBaseboard(to) || IsWallProtector(to)) continue;
            if (to.isAttachmentPoint || to.global_guid == _attachPointGUID || string.IsNullOrEmpty(to.global_guid)) continue;

            if (!SelectableAssetBundles.TryGetSelectableData(to.global_guid, out SelectableData data))
            {
                await SelectableAssetBundles.EnsureCdnCatalogMerged();
                if (!Application.isPlaying) throw new AppQuitInTaskException();

                if (!SelectableAssetBundles.TryGetSelectableData(to.global_guid, out data))
                {
                    Debug.LogError($"Could not find selectable data for {to.objectName} with guid {to.global_guid}");
                    missingGuids.Add(to.global_guid);
                    continue;
                }
            }

            bundleNames.Add(data.AssetBundleName);
        }

        var loadTasks = bundleNames.Select(bundleName =>
            AssetBundleManager.GetAsset<GameObject>(bundleName)).ToArray();

        await Task.WhenAll(loadTasks);

        foreach (Task<GameObject> loadTask in loadTasks)
        {
            if (loadTask.Result != null)
                PlacementLoadOptimizer.PrepareCachedPrefab(loadTask.Result);
        }

        AssetPipelineDiagnostics.Log("RoomLoad.Cache",
            $"Preloaded {bundleNames.Count} unique prefab bundle(s) for {trackedObjects.Count} tracked object(s)");
        if (missingGuids.Count > 0)
        {
            Debug.LogWarning(
                $"Load cache completed with missing selectable data for {missingGuids.Count} GUID(s). First missing: {missingGuids.First()}");
            UI_DialogPrompt.Open(
                $"Could not resolve {missingGuids.Count} catalog item(s) while loading.\n"
                + "Some objects may be missing. Ensure the asset catalog/CDN is available.",
                new ButtonAction("OK"));
        }
    }
}
