using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;
using System;
using SplenSoft.UnityUtilities;
using UnityEngine.Events;
using RTG;
using SplenSoft.AssetBundles;
using UnityEditor;
using UnityEngine.UI;

public class ConfigurationManager : MonoBehaviour
{
    public static ConfigurationManager Instance { get; private set; }

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

    private void Awake()
    {
        if (Instance != null)
            Destroy(this.gameObject);

        Instance = this;
        CreateTracker();
        NewRoomSave();
        HandleBackwardsCompatibility();

    }

    private void Start() => duplicateRoom = FindObjectOfType<DuplicateRoom>();

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
        PlayerPrefs.SetString(_lastNukedSavesPlayerPrefsKey, Application.version);
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
        _roomConfiguration = new RoomConfiguration() { collections = new List<Tracker>(), version = Application.version };
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
        string folder = Application.persistentDataPath + $"/Saved/Configs/";
        string configName = title.Replace(" ", "_") + ".json";
        configName = ReplaceInvalidChars(configName);

        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, configName);
        if (File.Exists(path)) File.Delete(path);

        File.WriteAllText(path, json);
        Debug.Log($"Saved Config: {path}");

        ObjectMenu.Instance.AddCustomMenuItem(path);
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



    public async void SaveRoom(string title)
    {
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
        string folder = Application.persistentDataPath + $"/Saved/";
        string configName = title.Replace(" ", "_") + ".json";
        configName = ReplaceInvalidChars(configName);

        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, configName);
        if (File.Exists(path)) File.Delete(path);

        File.WriteAllText(path, json);
        Debug.Log($"Saved Room: {path}");

        await Task.Delay(1000);
        token.SetProgress(1);

        RoomConfigLoader.Instance.GenerateRoomItem(path);

        foreach (TrackedObject obj in foundObjects)
        {
            if (obj.TryGetComponent(out AttachmentPoint attachmentPoint))
                attachmentPoint.SetToProperParent();
        }

        UI_DialogPrompt.Open(
          $"Success! Room saved to {folder}",
          new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = folder),
          new ButtonAction("Done"));
    }

    public async Task<GameObject> LoadArmAssembly(string file)
    {
        Debug.Log($"Loading config file at {file}");
        IsLoading = true;

        try
        {
            if (File.Exists(file))
            {
                CreateTracker();
                string json = File.ReadAllText(file);
                _tracker = JsonConvert.DeserializeObject<Tracker>(json);

                _newPoints = new List<AttachmentPoint>();
                _newObjects = new List<TrackedObject>();
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

                OnConfigurationLoadComplete?.Invoke(gameObject);
                return gameObject;
            }
            else
            {
                UI_DialogPrompt.Open("File no longer exists.");
                Debug.LogError($"File at {file} no longer exists");
                return null;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void LoadRoom(string file)
    {
        Debug.Log($"Clearing default room objects");
        List<TrackedObject> existingObjects = FindObjectsOfType<TrackedObject>().ToList();

        foreach (TrackedObject to in existingObjects)
        {
            Transform topParent = to.transform; while (topParent.parent != null) topParent = topParent.parent;
            if (to == null) continue;
            if (to.transform == to.transform.root || topParent.name == duplicateRoom.currentRoom.name && !IsBaseboard(to.GetData()) && !IsWallProtector(to.GetData()) && !IsRoomBoundary(to.GetData()))
                Destroy(to.gameObject);
        }
        existingObjects.Clear();
        existingObjects.TrimExcess();

        Selectable.DestroyAll();

        Debug.Log($"Loading Room at {file}");

        if (File.Exists(file))
        {
            CreateTracker();
            string json = File.ReadAllText(file);

            _roomConfiguration = JsonConvert
                .DeserializeObject<RoomConfiguration>(json);

            // restore client metadata into UI
            TryRestoreClientMetaData();

            LoadRoom();
        }
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
        var token = Loading.GetLoadingToken();

        try
        {
            RoomSize.SetDimensions(_roomConfiguration.roomDimension);

            float progressionTicks = 1f / _roomConfiguration.collections.Count;
            float progression = 0;
            foreach (Tracker t in _roomConfiguration.collections)
            {
                _newPoints = new List<AttachmentPoint>();
                _newObjects = new List<TrackedObject>();
                await LoadAllObjectsIntoCache(t.objects);
                await Task.Yield();
                await ProcessTrackedObjects(t.objects);
                await Task.Yield();
                await SetObjectProperties(_newObjects);
                await Task.Yield();

                foreach (var to in _newObjects)
                {
                    try { to.ApplySavedState(); }
                    catch (Exception ex) { Debug.LogWarning($"[LoadRoom] ApplySavedState failed on {to.name}: {ex.Message}"); }
                }

                RandomizeInstanceGUIDs();
                progression += progressionTicks;
                token.SetProgress(progression);
            }

            OnRoomLoadComplete?.Invoke();
        }
        catch { throw; }
        finally
        {
            IsLoading = false;
            token.SetProgress(1f);
        }
    }

    // --- Multi pass state ---
    private Dictionary<string, GameObject> _guidToGameObject = new();
    private Queue<(GameObject obj, TrackedObject.Data data)> _pendingSetup = new();
    public List<TrackedObject.Data> _pendingEmbedded = new();
    private List<TrackedObject.Data> _pendingAttachmentPoints = new();

    private async Task ProcessTrackedObjects(List<TrackedObject.Data> trackedObjects)
    {
        _guidToGameObject.Clear();
        _pendingSetup.Clear();
        _pendingEmbedded.Clear();
        _pendingAttachmentPoints.Clear();
        _newObjects = new List<TrackedObject>();
        _newPoints = new List<AttachmentPoint>();

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
            var task = InstantiateObject(data);
            await task;
            if (!Application.isPlaying) throw new AppQuitInTaskException();
            go = task.Result;
            if (go == null)
            {
                Debug.LogError($"Failed to instantiate object with guid: {data.global_guid}");
                continue;
            }

            // Register root instance and all child selectables by their instance_guid (guid field)
            if (!string.IsNullOrEmpty(data.instance_guid))
                _guidToGameObject[data.instance_guid] = go;

            var childSelectables = go.GetComponentsInChildren<Selectable>(true);
            foreach (var sel in childSelectables)
            {
                if (!string.IsNullOrEmpty(sel.guid))
                    _guidToGameObject[sel.guid] = sel.gameObject;
            }

            _pendingSetup.Enqueue((go, data));

            var trackedObj = go.GetComponent<TrackedObject>();
            if (trackedObj != null)
                _newObjects.Add(trackedObj);
        }

        // Pass 2: establish hierarchy & apply transforms
        while (_pendingSetup.Count > 0)
        {
            var (go, data) = _pendingSetup.Dequeue();

            var trackedObj = go.GetComponent<TrackedObject>();
            if (trackedObj != null)
                trackedObj.StoreValues(data);

            // Resolve parent by GUID first
            Transform parent = null;
            if (!string.IsNullOrEmpty(data.parentGuid) && _guidToGameObject.TryGetValue(data.parentGuid, out var parentGO))
                parent = parentGO.transform;

            // Fallback to parentPath
            if (parent == null && !string.IsNullOrEmpty(data.parentPath))
            {
                var parentGO2 = GameObject.Find(NormalizeFindPath(data.parentPath));
                if (parentGO2 != null)
                    parent = parentGO2.transform;
            }

            if (parent != null)
            {
                go.transform.SetParent(parent, false);
                trackedObj?.RestoreTransform(isRoot: false);
            }
            else
            {
                trackedObj?.RestoreTransform(isRoot: true);
            }

            if (!string.IsNullOrEmpty(data.keepRelativePositionParentName) && go.TryGetComponent<KeepRelativePosition>(out var comp))
                comp.ParentName = data.keepRelativePositionParentName;
        }

        // Pass 3: embedded selectables
        foreach (var emb in _pendingEmbedded)
        {
            ProcessEmbeddedSelectable(emb);
        }

        // Pass 4: attachment points
        foreach (var apData in _pendingAttachmentPoints)
            ProcessAttachmentPoint(apData);
    }

    private GameObject ProcessEmbeddedSelectable(TrackedObject.Data to)
    {
        try
        {
            // Try to find the embedded object by selfPath, parent, or parentPath
            GameObject go = null;
            if (!string.IsNullOrEmpty(to.selfPath))
                go = GameObject.Find(NormalizeFindPath(to.selfPath));
            if (go == null && !string.IsNullOrEmpty(to.parent))
                go = GameObject.Find(NormalizeFindPath(to.parent));
            if (go == null && !string.IsNullOrEmpty(to.parentPath))
                go = GameObject.Find(NormalizeFindPath(to.parentPath));
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

        if (obj.TryGetComponent(out MaterialPalette palette))
        {
            for (int i = 0; i < obj.GetMaterials().Count(); i++)
            {
                string modifiedName = obj.GetMaterials()[i].Replace(" (Instance)", "");
                palette.Assign(modifiedName, i);
            }
        }
    }
    private async Task<GameObject> InstantiateObject(TrackedObject.Data trackedObject)
    {
        if (!SelectableAssetBundles.TryGetSelectableData(trackedObject.global_guid, out SelectableData data))
        {
            Debug.LogError($"Could not find selectable data for {trackedObject.objectName} with guid {trackedObject.global_guid}");
            return null;
        }

        var task = data.GetPrefab();
        await task;
        if (!Application.isPlaying) throw new AppQuitInTaskException();
        if (task.Result == null)
        {
            Debug.LogError($"AssetBundle returned null prefab for guid {trackedObject.global_guid}");
            return null;
        }

        GameObject go = Instantiate(task.Result);
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

            if (selectable.SpecialTypes != null && selectable.SpecialTypes.Count > 0 && selectable.SpecialTypes[0] == SpecialSelectableType.Door)
            {
                var wc = selectable.GetComponentInChildren<WallCutter>();
                if (wc != null) wc.UpdateCuts();
            }
            ObjectMenu.Instance.HandleOutletAndPricing(go, trackedObject.UIButtonname);
        }
        return go;
    }

    private void ProcessAttachmentPoint(TrackedObject.Data to)
    {
        GameObject apGO = null;
        if (!string.IsNullOrEmpty(to.instance_guid))
            _guidToGameObject.TryGetValue(to.instance_guid, out apGO);

        if (apGO == null && !string.IsNullOrEmpty(to.parentPath))
            apGO = GameObject.Find(NormalizeFindPath(to.parentPath));

        if (apGO == null)
        {
            Debug.LogError($"Could not find attachment point for {to.parentPath} (GUID: {to.instance_guid})");
            return;
        }
        if (!apGO.TryGetComponent<TrackedObject>(out var trackedObject))
        {
            Debug.LogError($"GameObject at {to.parentPath} did not have TrackedObject component");
            return;
        }

        trackedObject.StoreValues(to);
        var attachmentPoints = apGO.GetComponentsInChildren<AttachmentPoint>(true);
        if (attachmentPoints == null || attachmentPoints.Length == 0)
        {
            Debug.LogError($"Expected AttachmentPoint on {to.parentPath} but none found.");
            return;
        }
        foreach (var attPoint in attachmentPoints)
        {
            if (attPoint == null) continue;
            var childSelectable = attPoint.GetComponentInChildren<Selectable>();
            if (childSelectable != null) attPoint.AttachedSelectable.Add(childSelectable);
            _newPoints.Add(attPoint);
        }
    }

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
        foreach (TrackedObject.Data to in trackedObjects)
        {
            if (IsRoomBoundary(to) || IsBaseboard(to) || IsWallProtector(to)) continue;
            if (to.isAttachmentPoint || to.global_guid == _attachPointGUID || string.IsNullOrEmpty(to.global_guid)) continue;
            if (!SelectableAssetBundles.TryGetSelectableData(to.global_guid, out SelectableData data))
            {
                Debug.LogError($"Could not find selectable data for {to.objectName} with guid {to.global_guid}");
                missingGuids.Add(to.global_guid);
                continue;
            }
            await AssetBundleManager.GetAsset<GameObject>(data.AssetBundleName);
            if (!Application.isPlaying) throw new AppQuitInTaskException();
        }
        if (missingGuids.Count > 0)
            Debug.LogWarning($"Load cache completed with missing selectable data for {missingGuids.Count} GUID(s). First missing: {missingGuids.First()}");
    }
}
