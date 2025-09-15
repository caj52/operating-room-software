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

    [Tooltip("Contextual display of GUIDs in hierarchy for easier debugging")]
    public bool isDebug = false;

    private List<TrackedObject> _newObjects;

    private List<AttachmentPoint> _newPoints;
    DuplicateRoom duplicateRoom;
    public static bool IsLoading { get; private set; }

    /// <summary>
    /// This is the prefab GUID for ALL attachment points. DO NOT CHANGE.
    /// </summary>
    private const string _attachPointGUID = "_AP";

    /// <summary>
    /// The tracker for individual configurations
    /// </summary>
    private Tracker _tracker;

    /// <summary>
    /// overall room configuration, contains collection of trackers
    /// </summary>
    private RoomConfiguration _roomConfiguration;

    private readonly string _lastNukedSavesPlayerPrefsKey = "lastNukedSaves";

    private readonly string _nukeBelowVersion = "1.0.0";

    private void Awake()
    {
        if (Instance != null)
            Destroy(this.gameObject);

        Instance = this;

        CreateTracker();
        NewRoomSave();
        HandleBackwardsCompatibility();


    }

    private void Start()
    {
        duplicateRoom = FindObjectOfType<DuplicateRoom>();
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

        //future use
        //string[] files = Directory.GetFiles(path);
        //foreach (string file in files.Where(x => x.EndsWith(".json")))
        //{
        //    if (File.Exists(file))
        //    {
        //        string json = File.ReadAllText(file);
        //        var roomConfiguration = JsonConvert.DeserializeObject<RoomConfiguration>(json);
        //    }
        //}
    }

    /// <summary>
    /// Replace later with a system that checks indiviudal serialized json versions, and even when that happens we should move the deprecated versions into a folder called "Deprecated" just in case
    /// </summary>
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
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException ex) // file is in use, or theres an open handle on the file
                    {
                        Debug.LogException(ex);
                    }
                    catch
                    {
                        throw;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Creates a new tracker to be used with a fresh configuration load
    /// </summary>
    private Tracker CreateTracker()
    {
        _tracker = new Tracker
        {
            objects = new List<TrackedObject.Data>()
        };
        return _tracker;
    }

    /// <summary>
    /// Create a new room configuration to be used with a fresh room load
    /// </summary>
    private RoomConfiguration NewRoomSave()
    {
        _roomConfiguration = new RoomConfiguration()
        {
            collections = new List<Tracker>(),
            version = Application.version
        };
        return _roomConfiguration;
    }

    /// <summary>
    /// Public, clearer API for saving a full scenario (room)
    /// </summary>
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

    /// <summary>
    /// Public, clearer API for loading a full scenario (room)
    /// </summary>
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

    /// <summary>
    /// Saves a configuration (collection of selectable objects from the transform.root).
    /// </summary>
    /// <param name="title">The title/fileName for this grouping</param>
    public void SaveConfiguration(string title)
    {
        CreateTracker();

        // finds all the Selectable & AttachmentPoints for this object
        TrackedObject[] foundObjects = Selectable.SelectedSelectables[0]
            .transform.root.GetComponentsInChildren<TrackedObject>();

        foreach (TrackedObject obj in foundObjects)
        {
            if (obj.TryGetComponent(out AttachmentPoint attachmentPoint))
            {
                attachmentPoint.SetToOriginalParent(); // for multi-arm configurations
            }
        }

        foreach (TrackedObject obj in foundObjects)
        {
            _tracker.objects.Add(obj.GetData()); // Add each tracked object, add to our local tracker instance
        }

        //====== SAVING JSON =======
        string json = JsonConvert.SerializeObject(_tracker, new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.Indented// allows Newtonsoft to go through the loop to serialize entire Position and Quaternion Rotation
        });

        string folder = Application.persistentDataPath + $"/Saved/Configs/";
        //string folder = Path.Combine(FullRoomSave.GetRoomPath() , $"Saved/Configs");
        string configName = title.Replace(" ", "_") + ".json"; // remove spaces and replace with underscores
        configName = ReplaceInvalidChars(configName);

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        string path = Path.Combine(folder, configName); // ensure proper pathing

        //Overwrite data
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.WriteAllText(path, json);
        Debug.Log($"Saved Config: {path}");

        ObjectMenu.Instance.AddCustomMenuItem(path); // add this configuration to the ObjectMenu
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
            {
                attachmentPoint.SetToProperParent(); // for multi-arm configurations
            }
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

        _roomConfiguration.roomDimension = RoomSize.Instance.CurrentDimensions; // grabs the current dimensions of the RoomSize to be applied on load

        TrackedObject[] foundObjects = FindObjectsOfType<TrackedObject>();

        foreach (TrackedObject obj in foundObjects) // We need to go through each object
        {
            if (obj.TryGetComponent(out AttachmentPoint attachmentPoint))
            {
                attachmentPoint.SetToOriginalParent(); // for multi-arm configurations
            }
        }

        await Task.Delay(1000);
        token.SetProgress(0.33f);

        // We need to go through each object
        foreach (TrackedObject obj in foundObjects)
        {
            // Get the top-most parent transform of this object
            Transform topParent = obj.transform;
            while (topParent.parent != null)
            {
                topParent = topParent.parent;
            }
            if (obj.transform == obj.transform.root || topParent.name == "Room1")
            {
                // creating trackers as we go
                CreateTracker();

                // and finding all embedded/attached selectables along with attachment points
                TrackedObject[] temps = obj.transform.GetComponentsInChildren<TrackedObject>();

                foreach (TrackedObject to in temps)
                {
                    // add them to their respective tracker
                    _tracker.objects.Add(to.GetData());
                }

                // and add them to the room tracker collection
                _roomConfiguration.collections.Add(_tracker);
            }
        }

        await Task.Delay(1000);
        token.SetProgress(0.66f);

        // ======SAVING JSON=========
        string json = JsonConvert.SerializeObject(_roomConfiguration, new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            Formatting = Formatting.Indented,
        });
        string folder = Application.persistentDataPath + $"/Saved/";
        string configName = title.Replace(" ", "_") + ".json";
        configName = ReplaceInvalidChars(configName);

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        string path = Path.Combine(folder, configName);

        //Overwrite data
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.WriteAllText(path, json);
        Debug.Log($"Saved Room: {path}");



        await Task.Delay(1000);
        token.SetProgress(1);

        RoomConfigLoader.Instance.GenerateRoomItem(path);

        foreach (TrackedObject obj in foundObjects) // We need to go through each object
        {
            if (obj.TryGetComponent(out AttachmentPoint attachmentPoint))
            {
                attachmentPoint.SetToProperParent(); // for multi-arm configurations
            }
        }

        UI_DialogPrompt.Open(
          $"Success! Enhanced screenshots saved to {folder}",
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

                // Apply saved active/enabled/component states after transforms
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

        //we need to clear the current room (default objects in scene) to load our new one
        List<TrackedObject> existingObjects = FindObjectsOfType<TrackedObject>().ToList();

        foreach (TrackedObject to in existingObjects)
        {
            Transform topParent = to.transform;
            while (topParent.parent != null)
            {
                topParent = topParent.parent;
            }
            if (to == null) continue;
            if (to.transform == to.transform.root || topParent.name == duplicateRoom.currentRoom.name && !IsBaseboard(to.GetData()) && !IsWallProtector(to.GetData()) && !IsRoomBoundary(to.GetData())) Destroy(to.gameObject);
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

            LoadRoom();
        }
    }

    private async void LoadRoom()
    {
        IsLoading = true;
        var token = Loading.GetLoadingToken();

        try
        {
            // apply the saved room dimensions from the json to the RoomSize
            //RoomSize.RoomSizeChanged?.Invoke(_roomConfiguration.roomDimension); 
            RoomSize.SetDimensions(_roomConfiguration.roomDimension);

            float progressionTicks = 1f / _roomConfiguration.collections.Count;
            float progression = 0;
            foreach (Tracker t in _roomConfiguration.collections) // iterate though each tracker in the collection creating new objects. 
            {
                _newPoints = new List<AttachmentPoint>();
                _newObjects = new List<TrackedObject>();
                await LoadAllObjectsIntoCache(t.objects);
                await Task.Yield();
                await ProcessTrackedObjects(t.objects);
                await Task.Yield();
                await SetObjectProperties(_newObjects);
                await Task.Yield();

                // Apply saved active/enabled/component states after transforms
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

    private Dictionary<string, GameObject> _guidToGameObject = new();
    private Queue<(GameObject obj, TrackedObject.Data data)> _pendingSetup = new();
    private List<TrackedObject.Data> _pendingEmbedded = new();
    private List<TrackedObject.Data> _pendingAttachmentPoints = new();

    private async Task ProcessTrackedObjects(List<TrackedObject.Data> trackedObjects)
    {
        _guidToGameObject.Clear();
        _pendingSetup.Clear();
        _pendingEmbedded.Clear();
        _pendingAttachmentPoints.Clear();
        _newObjects = new List<TrackedObject>();
        _newPoints = new List<AttachmentPoint>();

        // Pass 1: instantiate prefabs and register GUIDs
        foreach (TrackedObject.Data data in trackedObjects)
        {
            GameObject go = null;

            if (IsRoomBoundary(data) || IsBaseboard(data) || IsWallProtector(data))
            {
                go = IsRoomBoundary(data) ? GetRoomBoundary(data) : GetGameObjectWithGuidName(data);
                if (go != null && go.GetComponent<Selectable>() != null)
                {
                    LogData(go.GetComponent<Selectable>(), data);
                    var existingTrackedObj = go.GetComponent<TrackedObject>();
                    if (existingTrackedObj != null)
                    {
                        //ResetScaleLevels(existingTrackedObj);
                       // ResetMaterialPalettes(existingTrackedObj);
                    }
                }
                continue;
            }

            // Attachment points deferred until after parents exist
            if (data.global_guid == _attachPointGUID)
            {
                _pendingAttachmentPoints.Add(data);
                continue;
            }

            // Embedded selectable (no global guid) - resolve after instantiation
            if (string.IsNullOrEmpty(data.global_guid))
            {
                _pendingEmbedded.Add(data);
                continue;
            }

            // Instantiate selectable prefab (do not set final transform yet)
            var task = InstantiateObject(data);
            await task;
            if (!Application.isPlaying) throw new AppQuitInTaskException();

            go = task.Result;
            if (go == null)
            {
                Debug.LogError($"Failed to instantiate object with guid: {data.global_guid}");
                continue;
            }

            // Register GUIDs: root instance_guid and all child selectables
            if (!string.IsNullOrEmpty(data.instance_guid))
                _guidToGameObject[data.instance_guid] = go;

            var childSelectables = go.GetComponentsInChildren<Selectable>(true);
            foreach (var sel in childSelectables)
            {
                if (!string.IsNullOrEmpty(sel.guid))
                {
                    _guidToGameObject[sel.guid] = sel.gameObject;

                }
            }

            _pendingSetup.Enqueue((go, data));

            var trackedObj = go.GetComponent<TrackedObject>();
            if (trackedObj != null)
            {
                _newObjects.Add(trackedObj);
            }
        }

        // Pass 2: establish hierarchy and restore transforms
        while (_pendingSetup.Count > 0)
        {
            var (go, data) = _pendingSetup.Dequeue();

            var trackedObj = go.GetComponent<TrackedObject>();
            if (trackedObj != null)
            {
                trackedObj.StoreValues(data);
            }

            // Resolve parent by GUID first, then by path
            Transform parent = null;
            if (!string.IsNullOrEmpty(data.parentGuid))
            {
                if (_guidToGameObject.TryGetValue(data.parentGuid, out var parentGO))
                    parent = parentGO.transform;
            }

            if (parent == null && !string.IsNullOrEmpty(data.parentPath))
            {
                var parentGO = GameObject.Find(data.parentPath);
                if (parentGO != null) parent = parentGO.transform;
            }

            if (parent != null)
            {

                go.transform.SetParent(parent, false);
                // Restore local transform
                trackedObj?.RestoreTransform(isRoot: false);

            }
            else
            {
                // root object - restore world transform
                trackedObj?.RestoreTransform(isRoot: true);

            }

            if (!string.IsNullOrEmpty(data.keepRelativePositionParentName))
            {
                if (go.TryGetComponent<KeepRelativePosition>(out var comp))
                    comp.ParentName = data.keepRelativePositionParentName;
            }

            // If this object contains attachment points that need normalization, defer to SetObjectProperties which will reposition them
        }

        // Resolve embedded selectables (now that parents exist)
        foreach (var emb in _pendingEmbedded)
        {
            GameObject containerGO = null;
            if (!string.IsNullOrEmpty(emb.parentGuid))
            {
                _guidToGameObject.TryGetValue(emb.parentGuid, out containerGO);
            }
            if (containerGO == null && !string.IsNullOrEmpty(emb.parentPath))
            {
                containerGO = GameObject.Find(emb.parentPath);
            }

            if (containerGO == null)
            {
                Debug.LogWarning($"Could not resolve embedded selectable parent for {emb.parentPath} (GUID: {emb.instance_guid})");
                continue;
            }

            // Find the embedded selectable under the container
            Selectable sel = containerGO.GetComponent<Selectable>();
            if (sel == null)
            {
                // maybe the embedded selectable is a child object; try find by path suffix
                var candidates = containerGO.GetComponentsInChildren<Selectable>(true);
                sel = candidates.FirstOrDefault(c => GetGameObjectPath(c.gameObject).EndsWith(emb.parentPath, StringComparison.Ordinal));
            }

            if (sel != null)
            {
                LogData(sel, emb);

                var t = sel.transform;

   

                // ✅ Don't parse strings; use the actual vector and wrap it
                Vector3 raw = emb.localRotation.eulerAngles;       // e.g. (0, 324, 0)
                Vector3 signed = Angle180.Signed(raw);             // -> (0, -36, 0)

                t.localRotation = Quaternion.Euler(raw.x,-raw.y,raw.z);


                var trackedObj = sel.GetComponent<TrackedObject>();
                if (trackedObj != null && !_newObjects.Contains(trackedObj)) _newObjects.Add(trackedObj);
            }
            else
            {
                Debug.LogWarning($"Embedded selectable not found under container {containerGO.name} for path {emb.parentPath}");
            }
        }

        // Resolve attachment points now (they depend on parents and children being created)
        foreach (var apData in _pendingAttachmentPoints)
        {
            ProcessAttachmentPoint(apData);
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
        if (!Application.isPlaying)
            throw new AppQuitInTaskException();

        if (task.Result == null)
        {
            Debug.LogError($"AssetBundle returned null prefab for guid {trackedObject.global_guid}");
            return null;
        }

        GameObject go = Instantiate(task.Result);

        // Remove DestroyOnLoad children
        var dolComps = go.GetComponentsInChildren<DestroyOnLoad>();
        Array.ForEach(dolComps, comp => { if (comp != null) Destroy(comp.gameObject); });

        // Do NOT set final transform here. We'll set transforms in second pass after parenting.
        // But set PositionToRestore if component exists to worldPosition as fallback
        if (go.TryGetComponent<RestorePositionOnLoad>(out var compRestore))
        {
            compRestore.PositionToRestore = trackedObject.worldPosition;
        }

        // Assign instance GUID/name onto selectable if present
        if (!string.IsNullOrEmpty(trackedObject.instance_guid))
            go.name = trackedObject.instance_guid;

        var selectable = go.GetComponent<Selectable>();
        if (selectable != null)
        {
            selectable.guid = trackedObject.instance_guid;
            selectable.UIButtonName = trackedObject.UIButtonname;
            LogData(selectable, trackedObject);

            if (selectable.SpecialTypes != null && selectable.SpecialTypes.Count > 0 && selectable.SpecialTypes[0] == SpecialSelectableType.Door)
            {
                var wc = selectable.GetComponentInChildren<WallCutter>(); if (wc != null) wc.UpdateCuts();
            }

            ObjectMenu.Instance.HandleOutletAndPricing(go, trackedObject.UIButtonname);
        }

        // Register child selectables will be done by caller after instantiation
        return go;
    }

    private void ProcessAttachmentPoint(TrackedObject.Data to)
    {
        GameObject apGO = null;
        Selectable child = null;
        if (!string.IsNullOrEmpty(to.instance_guid))
            _guidToGameObject.TryGetValue(to.instance_guid, out apGO);

        if (apGO == null && !string.IsNullOrEmpty(to.parentPath))
            apGO = GameObject.Find(to.parentPath);

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
            if (childSelectable != null)
            {
                attPoint.AttachedSelectable.Add(childSelectable);
            }
            _newPoints.Add(attPoint);
        }
    }

    private GameObject ProcessEmbeddedSelectable(TrackedObject.Data to)
    {
        // Try GUID lookup first
        if (!string.IsNullOrEmpty(to.instance_guid) && _guidToGameObject.TryGetValue(to.instance_guid, out var go))
        {
            var sel = go.GetComponent<Selectable>();
            if (sel != null) LogData(sel, to);
            go.transform.localRotation = to.localRotation;
            return go;
        }

        // Fallback to path
        var fallback = GameObject.Find(to.parentPath);
        if (fallback != null)
        {
            var sel = fallback.GetComponent<Selectable>();
            if (sel != null) LogData(sel, to);
            fallback.transform.localRotation = to.localRotation;
            return fallback;
        }

        Debug.LogError($"ProcessEmbeddedSelectable failed to find {to.parentPath} (GUID: {to.instance_guid})");
        return null;
    }

    /// <summary>
    /// Resets the objects position and rotation to match with the JSON strucutre, after a frame to allow other logic to process the correct information
    /// </summary>
    /// <param name="newObjects">The tracked list of new objects that have been created during loading</param>
    private async Task SetObjectProperties(List<TrackedObject> newObjects)
    {
        newObjects.Reverse(); // The list needs to be reversed so that the hierarchy is root downwards. 
        foreach (TrackedObject obj in newObjects)
        {
            //Debug.Log(obj.gameObject.name);
            //ResetScaleLevels(obj);
        }

        // Allow time for scaling values to be applied in Selectable
        await Task.Yield();
        if (!Application.isPlaying)
            throw new AppQuitInTaskException();

        foreach (TrackedObject obj in newObjects)
        {
         //  ResetLocalPosition(obj);
         //  ResetMaterialPalettes(obj);
        }

      
    }

    /// <summary>
    /// Randomizes the instance GUIDs of the tracked objects within the configuration so that double loading doesn't have conflicts with GameObject.Find
    /// </summary>
    private void RandomizeInstanceGUIDs()
    {
        foreach (TrackedObject to in _newObjects)
        {
            if (to.transform.root == to.transform)
            {
                to.gameObject.GetComponent<Selectable>().guid = Guid.NewGuid().ToString();
                to.gameObject.name = to.gameObject.GetComponent<Selectable>().guid;
            }
        }
    }

    /// <summary>
    /// Sets the scale of the object
    /// </summary>
    /// <param name="obj">The JSON structure of the object</param>
    private void ResetScaleLevels(TrackedObject obj)
    {
        var storedScaleLevel = obj.GetScaleLevel();
        if (storedScaleLevel != null)
        {
            var selectable = obj.GetComponent<Selectable>();
            //selectable.ScaleLevels.ForEach((item) => item.Selected = false);
            //storedScaleLevel.Selected = true;

            obj.transform.localScale = new Vector3(
                obj.GetScale().x,
                obj.GetScale().y,
                obj.GetScale().z
            );
            selectable.SetScaleLevel(storedScaleLevel, true);
        }
        else
        {
            //Debug.Log($"No scale level found for {obj.name}, applying default scale of {obj.GetScale()}.");
            obj.transform.localScale = obj.GetScale();
        }
    }

    /// <summary>
    /// Sets the Local Position & "OriginalLocalPosition" of the object
    /// </summary>
    /// <param name="obj">The JSON structure of the object</param>
    private void ResetLocalPosition(TrackedObject obj)
    {
        if (obj == null)
        {
            Debug.LogWarning("Attempted to reset local position of a missing TrackedObject reference.");
            return;
        }

        if (obj.IsDecal())
        {
            return;
        }

        if (!string.IsNullOrEmpty(obj.GetComponent<Selectable>().GUID) && obj.transform != obj.transform.root)
        {
            obj.transform.localPosition = Vector3.zero;
            obj.GetComponent<Selectable>().OriginalLocalPosition = obj.transform.localPosition;
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

    /// <summary>
    /// Logs the JSON structure of the object scale to be applied 
    /// at a later point in execution by <see cref="ResetScaleLevels"/>
    /// </summary>
    /// <param name="s">The object's selectable component</param>
    /// <param name="to">The JSON structure of this object</param>
    private void LogData(Selectable s, TrackedObject.Data to)
    {
        s.GetComponent<TrackedObject>().StoreValues(to);
    }

    /// <summary>
    /// Finds the root transform of a generated configuration
    /// </summary>
    /// <returns>Generated configuration's transform.root</returns>
    private GameObject GetRoot()
    {
        return _newObjects.Single(x => x.transform == x.transform.root).gameObject;
    }

    public static bool IsRoomBoundary(string guid)
    {
        return
            guid == "Wall_N" ||
            guid == "Wall_S" ||
            guid == "Wall_E" ||
            guid == "Wall_W" ||
            guid == "Ceil" ||
            guid == "Floor";
    }

    public static bool IsBaseboard(string guid)
        => guid.StartsWith("Baseboard");

    public static bool IsWallProtector(string guid)
        => guid.StartsWith("WallProtector");

    private static bool IsBaseboard(TrackedObject.Data to)
        => IsBaseboard(to.global_guid);

    public static bool IsWallProtector(TrackedObject.Data to)
        => IsWallProtector(to.global_guid);

    public static bool IsRoomBoundary(TrackedObject.Data to)
        => IsRoomBoundary(to.global_guid);

    private static GameObject GetRoomBoundary(TrackedObject.Data to)
        => GameObject.Find("RoomBoundary_" + to.global_guid);

    /// <returns>A permanent scene <see cref="GameObject"/> with 
    /// <see cref="UnityEngine.Object.name"/> == 
    /// <see cref="TrackedObject.Data.global_guid"/></returns>
    private static GameObject GetGameObjectWithGuidName(TrackedObject.Data to)
        => GameObject.Find(to.global_guid);

    /// <summary>
    /// Gets the hierachy PATH for a GameObject
    /// </summary>
    /// <param name="obj">The object whoms path you are needing</param>
    /// <returns>string value containing entire editor & engine pathing</returns>
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

    /// <summary>
    /// Loads all referenced selectable prefabs' asset bundles so instantiation is fast and reliable.
    /// </summary>
    private async Task LoadAllObjectsIntoCache(List<TrackedObject.Data> trackedObjects)
    {
        var missingGuids = new List<string>();
        foreach (TrackedObject.Data to in trackedObjects)
        {
            if (IsRoomBoundary(to) || IsBaseboard(to) || IsWallProtector(to))
                continue;

            // Skip attachment points
            if (to.global_guid == _attachPointGUID || string.IsNullOrEmpty(to.global_guid))
                continue;

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
            Debug.LogWarning($"Load cache completed with missing selectable data for {missingGuids.Count} GUID(s).\nFirst missing: {missingGuids.First()}");
    }
}

// 1) Keep this utility somewhere (robust wrap using Mathf.DeltaAngle)
public static class Angle180
{
    // Wrap any degree value into (-180, 180]
    public static float Signed(float deg) => Mathf.DeltaAngle(0f, deg);

    public static Vector3 Signed(Vector3 eulerDeg)
        => new Vector3(Signed(eulerDeg.x), Signed(eulerDeg.y), Signed(eulerDeg.z));
}
