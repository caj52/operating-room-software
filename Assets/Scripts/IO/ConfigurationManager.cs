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

public class ConfigurationManager : MonoBehaviour
{
    public static ConfigurationManager Instance { get; private set; }

    public static UnityEvent OnRoomLoadComplete { get; } = new();
    public static UnityEvent<GameObject> OnConfigurationLoadComplete { get; } = new();

    [Tooltip("Contextual display of GUIDs in hierarchy for easier debugging")]
    public bool isDebug = false;

    private List<TrackedObject> _newObjects;

    private List<AttachmentPoint> _newPoints;

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
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore // allows Newtonsoft to go through the loop to serialize entire Position and Quaternion Rotation
        });
        //  string folder = Application.persistentDataPath + $"/Saved/Configs/";
        string folder = Path.Combine(FullRoomSave.GetRoomPath() , $"Saved/Configs");
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
            if (obj.transform == obj.transform.root)
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
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        });
        string folder = Path.Combine(FullRoomSave.GetRoomPath(), $"Saved");
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

        // we need to clear the current room (default objects in scene) to load our new one
        //List<TrackedObject> existingObjects = FindObjectsOfType<TrackedObject>().ToList(); 

        //foreach (TrackedObject to in existingObjects)
        //{
        //    if (to == null) continue;
        //    if (to.transform == to.transform.root && !IsRoomBoundary(to.GetData())) Destroy(to.gameObject);
        //}
        //existingObjects.Clear();
        //existingObjects.TrimExcess();

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

    private async Task ProcessTrackedObjects(List<TrackedObject.Data> trackedObjects)
    {
        foreach (TrackedObject.Data data in trackedObjects)
        {
            GameObject go = null;

            if (IsRoomBoundary(data) || IsBaseboard(data) || IsWallProtector(data))
            {
                go = IsRoomBoundary(data) ?
                    GetRoomBoundary(data) :
                    GetGameObjectWithGuidName(data);

                LogData(go.GetComponent<Selectable>(), data);
                var existingTrackedObj = go.GetComponent<TrackedObject>();
                ResetScaleLevels(existingTrackedObj);
                ResetMaterialPalettes(existingTrackedObj);
                continue;
            }

            // if it is not an AttachPoint, we need to place the Selectable
            if (data.global_guid != _attachPointGUID && 
                !string.IsNullOrEmpty(data.global_guid))
            {
                var task = InstantiateObject(data);

                await task;
                if (!Application.isPlaying)
                    throw new AppQuitInTaskException();

                go = task.Result;
                if (!string.IsNullOrEmpty(data.keepRelativePositionParentName))
                {
                    if (go.TryGetComponent<KeepRelativePosition>(out var comp))
                    {
                        comp.ParentName = data.keepRelativePositionParentName;
                    }
                }
                _newObjects.Add(go.GetComponent<TrackedObject>());
            }

            if (data.parent != null)
            {
                if (data.global_guid == null || data.global_guid == "") 
                {
                    // embedded selectable component
                    go = ProcessEmbeddedSelectable(data);
                    _newObjects.Add(go.GetComponent<TrackedObject>());
                }
                else if (data.global_guid == _attachPointGUID) 
                {
                    // attachment point component
                    ProcessAttachmentPoint(data);
                }
                else 
                {
                    // selectable attached to an attachment point
                    ProcessAttachedSelectable(go, data);
                    _newObjects.Add(go.GetComponent<TrackedObject>());
                }
            }
            else
            {
                if (data.global_guid == null || data.global_guid == "") 
                {
                    // embedded selectable component
                    go = ProcessEmbeddedSelectable(data);
                    _newObjects.Add(go.GetComponent<TrackedObject>());
                }
            }
        }
    }

    /// <summary>
    /// Loads all objects into cache so they are easily handled by the loading process
    /// </summary>
    private async Task LoadAllObjectsIntoCache(List<TrackedObject.Data> trackedObjects)
    {
        foreach (TrackedObject.Data to in trackedObjects)
        {
            if (IsRoomBoundary(to) || IsBaseboard(to) || 
                IsWallProtector(to))
            {
                continue;
            }

            // if it is not an AttachPoint, we need to place the Selectable
            if (to.global_guid != _attachPointGUID &&
            !string.IsNullOrEmpty(to.global_guid))
            {
                if (!SelectableAssetBundles.TryGetSelectableData
                (to.global_guid, out SelectableData data))
                {
                    Debug.LogError($"Could not find selectable data for {to.objectName} with guid {to.global_guid}");
                    return;
                }

                await AssetBundleManager.GetAsset
                    <GameObject>(data.AssetBundleName);
            }
        }
    }

    /// <summary>
    /// Instantiates a object, applies position, rotation, guid, and logs the scale for use later.
    /// </summary>
    /// <param name="trackedObject">The JSON structure of this object</param>
    /// <returns>The gameobject of our newly instantiated and setup logic applied</returns>
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

        GameObject go = Instantiate(task.Result);

        // Check for DestroyOnLoad components
        var dolComps = go.GetComponentsInChildren<DestroyOnLoad>();
        Array.ForEach(dolComps, comp => Destroy(comp.gameObject));

        go.transform.SetPositionAndRotation(trackedObject.pos, trackedObject.rot);

        if (go.TryGetComponent<RestorePositionOnLoad>(out var comp))
        {
            comp.PositionToRestore = trackedObject.pos;
        }

        if (!string.IsNullOrEmpty(trackedObject.instance_guid))
            go.name = trackedObject.instance_guid;

        go.GetComponent<Selectable>().guid = trackedObject.instance_guid;
        LogData(go.GetComponent<Selectable>(), trackedObject);
        return go;
    }

    /// <summary>
    /// Finds and applies the tracked AttachmentPoint information to the prefab included version
    /// </summary>
    /// <param name="to">The JSON structure of this object</param>
    private void ProcessAttachmentPoint(TrackedObject.Data to)
    {
        GameObject myself = GameObject.Find(to.parent);
        //Debug.Log($"Logging attachment point path - {to.parent}");

        if (myself == null)
        {
            Debug.LogError($"Could not find game object at {to.parent}");
            return;
        }

        if (!myself.TryGetComponent<TrackedObject>(out var trackedObject))
        {
            Debug.LogError($"GameObject at {to.parent} did not have TrackedObject component");
            return;
        }
        trackedObject.StoreValues(to);
        var attPoint = myself.GetComponent<AttachmentPoint>();
        //Debug.Log($"AP actual path is {GetGameObjectPath(attPoint.gameObject)}");
        _newPoints.Add(attPoint);
    }

    /// <summary>
    /// Finds and applies the tracked EmbeddedSelectable (non-root selectable of a prefab) to the prefab included version
    /// </summary>
    /// <param name="to">The JSON structure of this object</param>
    /// <returns>The populated selectable's gameobject is returned</returns>
    private GameObject ProcessEmbeddedSelectable(TrackedObject.Data to)
    {
        try
        {
            GameObject go = GameObject.Find(to.parent);
            LogData(go.GetComponent<Selectable>(), to);
            go.transform.rotation = to.rot;
            return go;
        }
        catch (NullReferenceException nullException)
        {
            Debug.LogError($"ProcessEmbeddedSelectable failed to find {to.parent}");
            Debug.LogError(nullException);
            return null;
        }
    }

    /// <summary>
    /// Finds and applies the tracked AttachedSelectable (root
    /// selectable of a prefab attached to another Selectable 
    /// without attachment points [decals]) and sets it's parent transform
    /// </summary>
    /// <param name="go">Reference to the gameObject being applied data</param>
    /// <param name="to">The JSON structure of this object</param>
    private void ProcessAttachedSelectable(GameObject go, TrackedObject.Data to)
    {
        if (!string.IsNullOrEmpty(to.attachedTo))
        {
            GameObject parentGO = GameObject.Find(to.attachedTo);
            go.transform.SetParent(parentGO.transform);
            go.GetComponent<Selectable>().AttachedTo = parentGO.GetComponent<Selectable>();
            LogData(go.GetComponent<Selectable>(), to);
        }
        else
        {
            //Debug.Log($"Attachment point parent - to.parent = {to.parent}");
            AttachmentPoint ap = _newPoints
                .Single(s => GetGameObjectPath(s.gameObject) == to.parent);

            ap.SetAttachedSelectable(go.GetComponent<Selectable>());
            go.transform.SetParent(ap.gameObject.transform);
            go.GetComponent<Selectable>().ParentAttachmentPoint = ap;
            LogData(go.GetComponent<Selectable>(), to);
        }
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
            ResetScaleLevels(obj);
        }

        // Allow time for scaling values to be applied in Selectable
        await Task.Yield(); 
        if (!Application.isPlaying)
            throw new AppQuitInTaskException();

        foreach (TrackedObject obj in newObjects)
        {
            ResetLocalPosition(obj);
            ResetMaterialPalettes(obj);
        }

        foreach (AttachmentPoint ap in _newPoints)
        {
            if (ap == null)
            {
                Debug.LogWarning("Attempted to reset a missing Attachment Point reference.");
                return;
            }
            ap.gameObject.transform.position = ap.GetComponent<TrackedObject>().GetPosition();
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
                obj.transform.localScale.z
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

        if(obj.IsDecal())
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

}
