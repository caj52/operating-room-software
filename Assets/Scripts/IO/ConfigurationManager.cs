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

    /// <summary>
    /// Once a room/config load starts, never destroy/reseed bed+lights from the cold-start
    /// path — that race was wiping fixtures that LoadRoom just restored.
    /// </summary>
    private bool _coldStartFixturesSuppressed;

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
        return NormalizeLoadPathNames(path);
    }

    /// <summary>
    /// Bundled FBX meshes still use the typo "BoomSegement"; project prefabs / saves may
    /// already say "BoomSegment". Normalize before path compares so service heads parent.
    /// </summary>
    private static string NormalizeLoadPathNames(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        return path.Replace("Segement", "Segment");
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
    private const string CeilingLightGuid = "gameobject_ada2137b3a434a54fbc11fd0fb37f568";
    /// <summary>Scene name of the ceiling RoomBoundary (VirtualParent for ceiling fixtures).</summary>
    private const string CeilingBoundaryName = "RoomBoundary_Ceil";
    private const string FloorBoundaryName = "RoomBoundary_Floor";

    private static readonly Vector3[] DefaultCeilingLightPositions =
    {
        new Vector3(0f, 3.048f, 2f),
        new Vector3(-2f, 3.048f, 0f),
        new Vector3(2f, 3.048f, 0f),
        new Vector3(0f, 3.048f, -2f),
    };

    private void Start()
    {
        duplicateRoom = FindObjectOfType<DuplicateRoom>();
        _ = EnsureBaseRoomFixturesFromCatalogAsync();
    }

    /// <summary>
    /// Cold start: Main-scene CeilingLight / OR_Table PrefabInstances nest asset-bundled
    /// .blend meshes that resolve with no usable mesh. Catalog instances work — same path
    /// LoadRoom uses after <see cref="EnsureBaseRoomDefaultsInSaveData"/>.
    /// </summary>
    private async Task EnsureBaseRoomFixturesFromCatalogAsync()
    {
        while (Application.isPlaying && !SelectableAssetBundles.Initialized)
            await Task.Yield();
        if (!ShouldSeedColdStartFixtures())
            return;

        // Drop broken scene fixtures before seeding from catalog.
        // Destroy() is deferred — do not re-query presence afterward; always seed
        // when still on a fresh cold start (no load has begun).
        foreach (TrackedObject to in FindObjectsByType<TrackedObject>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (to == null) continue;
            if (IsCeilingLightObject(to) || IsOperatingTableObject(to))
                Destroy(to.gameObject);
        }

        if (!ShouldSeedColdStartFixtures())
            return;

        Transform room = GetCurrentRoomTransform();
        await SpawnDefaultCeilingLightsAsync(room);
        if (!ShouldSeedColdStartFixtures())
            return;
        await SpawnDefaultOperatingTableAsync(room);
    }

    private bool ShouldSeedColdStartFixtures()
    {
        return Application.isPlaying
               && !IsLoading
               && !_coldStartFixturesSuppressed
               && string.IsNullOrEmpty(CurrentRoomSaveName);
    }

    private Transform GetCurrentRoomTransform()
    {
        if (duplicateRoom != null && duplicateRoom.currentRoom != null)
            return duplicateRoom.currentRoom.transform;
        return GameObject.Find("Room1")?.transform;
    }

    private async Task SpawnDefaultCeilingLightsAsync(Transform room)
    {
        if (!SelectableAssetBundles.TryGetSelectableData(CeilingLightGuid, out SelectableData data))
        {
            Debug.LogError("[CeilingLight] Catalog missing default ceiling light guid; new room has no lights.");
            return;
        }

        GameObject prefab = await data.GetPrefab(loadingToken: Loading.GetLoadingToken());
        if (!ShouldSeedColdStartFixtures() || prefab == null)
            return;

        // Match scene fixtures: virtual-parent to the ceiling so height changes and
        // Show Ceiling Objects work. Place on the *current* ceiling underside first —
        // if we bake relative offset while the ceiling is still at the scene default
        // (~1.4m) and room size is applied later, lights jump above the slab and vanish.
        Transform ceiling = FindBoundaryTransform(CeilingBoundaryName);
        float ceilingY = ceiling != null
            ? ceiling.position.y - (RoomBoundary.DefaultWallThickness * 0.5f)
            : DefaultCeilingLightPositions[0].y;

        Quaternion rot = new Quaternion(0.7071068f, 0f, 0f, 0.7071068f);
        for (int i = 0; i < DefaultCeilingLightPositions.Length; i++)
        {
            if (!ShouldSeedColdStartFixtures())
                return;
            GameObject go = Instantiate(prefab);
            go.name = i == 0 ? "CeilingLightFixture" : $"CeilingLightFixture ({i})";
            if (room != null)
                go.transform.SetParent(room, false);

            Vector3 xz = DefaultCeilingLightPositions[i];
            go.transform.position = new Vector3(xz.x, ceilingY, xz.z);
            go.transform.localRotation = rot;
            go.transform.localScale = Vector3.one;

            AttachKeepRelativeToBoundary(go, CeilingBoundaryName);
            FinalizeSpawnedBaseFixture(go);
        }
    }

    private async Task SpawnDefaultOperatingTableAsync(Transform room)
    {
        if (!SelectableAssetBundles.TryGetSelectableData(OperatingTableGuid, out SelectableData data))
        {
            Debug.LogError("[OR_Table] Catalog missing default operating table guid; new room has no table.");
            return;
        }

        GameObject prefab = await data.GetPrefab(loadingToken: Loading.GetLoadingToken());
        if (!ShouldSeedColdStartFixtures() || prefab == null)
            return;

        GameObject go = Instantiate(prefab);
        go.name = "OR_Table_0";
        if (room != null)
            go.transform.SetParent(room, false);

        go.transform.localPosition = new Vector3(0f, 0.05715f, 0f);
        go.transform.localRotation = new Quaternion(-0.5f, 0.5f, 0.5f, 0.5f);
        go.transform.localScale = Vector3.one;

        AttachKeepRelativeToBoundary(go, FloorBoundaryName);
        FinalizeSpawnedBaseFixture(go);
    }

    /// <summary>
    /// Wire <see cref="KeepRelativePosition"/> like Selectable placement does after a
    /// raycast drop: VirtualParent + relative offset. Ceiling fixtures need this for
    /// room-height follow and Show Ceiling Objects hide.
    /// </summary>
    private static void AttachKeepRelativeToBoundary(GameObject go, string boundaryObjectName)
    {
        if (go == null || string.IsNullOrEmpty(boundaryObjectName))
            return;

        Transform surface = FindBoundaryTransform(boundaryObjectName);
        if (surface == null || !go.TryGetComponent<KeepRelativePosition>(out var keepRel))
            return;

        keepRel.VirtualParentChanged(surface);
        keepRel.SelectablePositionChanged();
    }

    private static Transform FindBoundaryTransform(string boundaryObjectName)
    {
        RoomBoundaryType? want = null;
        if (boundaryObjectName == CeilingBoundaryName)
            want = RoomBoundaryType.Ceiling;
        else if (boundaryObjectName == FloorBoundaryName)
            want = RoomBoundaryType.Floor;

        if (want.HasValue)
        {
            foreach (RoomBoundary rb in RoomBoundary.Instances)
            {
                if (rb != null && rb.RoomBoundaryType == want.Value)
                    return rb.transform;
            }
        }

        return GameObject.Find(boundaryObjectName)?.transform;
    }

    private static void FinalizeSpawnedBaseFixture(GameObject go)
    {
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

    private static bool IsCeilingLightObject(TrackedObject to)
    {
        if (to == null) return false;
        string blob = (to.name ?? "") + " " + (to.GetComponent<Selectable>()?.MetaData?.Name ?? "")
                      + " " + (to.GetComponent<Selectable>()?.UIButtonName ?? "");
        return blob.IndexOf("CeilingLight", StringComparison.OrdinalIgnoreCase) >= 0
               || blob.IndexOf("Ceiling Light", StringComparison.OrdinalIgnoreCase) >= 0;
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

    /// <summary>Writes the selected assembly configuration JSON to <paramref name="path"/>.</summary>
    public void SaveConfigurationToPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (Selectable.SelectedSelectables == null || Selectable.SelectedSelectables.Count == 0)
            throw new InvalidOperationException("Nothing selected to save as a configuration.");

        CreateTracker();
        // Include inactive boom-head rows — same as room save. Gases on deactivated
        // panels are otherwise omitted from the config JSON.
        TrackedObject[] foundObjects = Selectable.SelectedSelectables[0]
            .transform.root.GetComponentsInChildren<TrackedObject>(true);

        ScaleAuditLog.Event("SaveConfig.begin", $"path={path} tracked={foundObjects.Length}");
        ScaleAuditLog.Hierarchy("SaveConfig.preDetach", Selectable.SelectedSelectables[0].transform.root,
            "live MoveUp hierarchy before SetToOriginalParent");

        foreach (TrackedObject obj in foundObjects)
        {
            if (obj.TryGetComponent(out AttachmentPoint attachmentPoint))
                attachmentPoint.SetToOriginalParent();
        }

        ScaleAuditLog.Hierarchy("SaveConfig.postDetach", Selectable.SelectedSelectables[0].transform.root,
            "canonical under-tube hierarchy after SetToOriginalParent");

        // Persist AP inverse Z. Do NOT reapply mesh isolation here — live SetScaleLevel
        // already has the correct untracked child scales; a re-derive can diverge and
        // bake the wrong inverse into the save.
        foreach (TrackedObject obj in foundObjects)
        {
            if (obj != null && obj.TryGetComponent(out Selectable sel) && sel != null)
                sel.EnsureAttachChainScaleCompensation(reapplyMeshIsolation: false);
        }

        ScaleAuditLog.Hierarchy("SaveConfig.postCompensate", Selectable.SelectedSelectables[0].transform.root,
            "after EnsureAttachChainScaleCompensation — this is what GetData serializes");

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
        ScaleAuditLog.Event("SaveConfig.written", $"path={path} objects={_tracker.objects.Count}");

        ObjectMenu.Instance?.AddCustomMenuItem(path);

        foreach (TrackedObject obj in foundObjects)
        {
            // Only re-id/rename the free-placed assembly root. Attached outlets/plates keep
            // human-readable names (outlet detection, cover plates) and stable child IDs.
            if (obj.TryGetComponent(out Selectable selectable) &&
                obj.transform.parent == null &&
                !string.IsNullOrEmpty(selectable.guid))
            {
                string newGuid = Guid.NewGuid().ToString();
                selectable.guid = newGuid;
                selectable.name = newGuid;
                obj.ApplyRuntimeInstanceId(newGuid);
            }
            if (obj.TryGetComponent(out AttachmentPoint attachmentPoint))
                attachmentPoint.SetToProperParent();
        }

        ScaleAuditLog.Hierarchy("SaveConfig.postRestoreMoveUp", Selectable.SelectedSelectables[0].transform.root,
            "live hierarchy after SetToProperParent restore");
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
        var totalTimer = Stopwatch.StartNew();
        string lastPhase = "start";
        string lastObject = null;
        bool completed = false;

        Debug.Log($"[SaveRoom] START path=\"{path}\" title=\"{title}\" loadingActive={Loading.LoadingActive}");

        try
        {
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
                Debug.LogWarning($"[SaveRoom] Could not capture client metadata: {e.Message}");
            }

            lastPhase = "find_tracked_objects";
            TrackedObject[] foundObjects = FindObjectsOfType<TrackedObject>();
            Debug.Log($"[SaveRoom] Found {foundObjects.Length} TrackedObject(s); roomDims={_roomConfiguration.roomDimension}");

            lastPhase = "detach_attachment_points";
            foreach (TrackedObject obj in foundObjects)
            {
                if (obj.TryGetComponent(out AttachmentPoint attachmentPoint))
                    attachmentPoint.SetToOriginalParent();
            }

            ScaleAuditLog.Event("SaveRoom.postDetach", $"tracked={foundObjects.Length}");

            // Same under-tube inverse Z contract as config save / SetScaleLevel.
            // Load (not save) re-derives untracked mesh inverses.
            lastPhase = "attach_chain_scale_compensation";
            foreach (TrackedObject obj in foundObjects)
            {
                if (obj != null && obj.TryGetComponent(out Selectable sel) && sel != null)
                    sel.EnsureAttachChainScaleCompensation(reapplyMeshIsolation: false);
            }

            ScaleAuditLog.Event("SaveRoom.postCompensate", "EnsureAttachChainScaleCompensation complete");

            lastPhase = "delay_before_33";
            await Task.Delay(1000);
            token.SetProgress(0.33f);
            Debug.Log("[SaveRoom] PROGRESS 33% — collecting object data");

            lastPhase = "collect_getdata";
            Transform roomRoot = GetCurrentRoomTransform();
            Debug.Log($"[SaveRoom] roomRoot={(roomRoot != null ? roomRoot.name : "null")}");

            var collectTimer = Stopwatch.StartNew();
            int rootCount = 0;
            int objectCount = 0;
            foreach (TrackedObject obj in foundObjects)
            {
                if (obj == null) continue;
                // One collection per placed root — not every nested TrackedObject under the room.
                // Nested boom/light parts are captured via GetComponentsInChildren on the root.
                if (!IsRoomSaveCollectionRoot(obj, roomRoot))
                    continue;

                rootCount++;
                CreateTracker();
                TrackedObject[] temps = obj.transform.GetComponentsInChildren<TrackedObject>(true);
                Debug.Log($"[SaveRoom] root[{rootCount}] \"{obj.name}\" path={GetGameObjectPath(obj.gameObject)} childrenTracked={temps.Length}");

                foreach (TrackedObject to in temps)
                {
                    if (to == null) continue;
                    lastObject = $"{to.name} path={GetGameObjectPath(to.gameObject)}";
                    try
                    {
                        _tracker.objects.Add(to.GetData());
                        objectCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(
                            $"[SaveRoom] GetData FAILED on \"{to.name}\" path={GetGameObjectPath(to.gameObject)} " +
                            $"selectable={to.GetComponent<Selectable>() != null} " +
                            $"attachmentPoint={to.GetComponent<AttachmentPoint>() != null} " +
                            $"materialPalette={to.GetComponent<MaterialPalette>() != null}\n{ex}");
                        throw;
                    }
                }
                _roomConfiguration.collections.Add(_tracker);
            }
            collectTimer.Stop();
            Debug.Log($"[SaveRoom] Collect done — roots={rootCount} objects={objectCount} ms={collectTimer.ElapsedMilliseconds}");

            lastPhase = "delay_before_66";
            lastObject = null;
            await Task.Delay(1000);
            token.SetProgress(0.66f);
            Debug.Log("[SaveRoom] PROGRESS 66% — serializing JSON");

            lastPhase = "serialize";
            var serializeTimer = Stopwatch.StartNew();
            string json = JsonConvert.SerializeObject(_roomConfiguration, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Formatting = Formatting.Indented,
            });
            serializeTimer.Stop();
            Debug.Log($"[SaveRoom] Serialize done — chars={json.Length} ms={serializeTimer.ElapsedMilliseconds}");

            lastPhase = "write_file";
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            if (File.Exists(path))
                File.Delete(path);

            File.WriteAllText(path, json);
            Debug.Log($"[SaveRoom] Wrote file \"{path}\" ({new FileInfo(path).Length} bytes)");

            // Clear the loading token as soon as the file is durable — attachment
            // restore below can take a while and must not keep the wait-screen up.
            token.SetProgress(1);
            completed = true;
            Debug.Log($"[SaveRoom] PROGRESS 100% — complete totalMs={totalTimer.ElapsedMilliseconds}");

            // Only list saves that live in the app's Saved folder.
            string savedRoot = Path.GetFullPath(GetSavedRoomsFolder())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(savedRoot, StringComparison.OrdinalIgnoreCase))
                RoomConfigLoader.Instance?.RefreshOrAddRoomItem(path);

            lastPhase = "restore_attachment_points";
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
        catch (Exception ex)
        {
            Debug.LogError(
                $"[SaveRoom] ABORTED phase={lastPhase} lastObject={lastObject ?? "(none)"} " +
                $"progress={token.Progress:0.##} loadingActive={Loading.LoadingActive} " +
                $"elapsedMs={totalTimer.ElapsedMilliseconds}\n{ex}");
        }
        finally
        {
            if (!completed)
            {
                Debug.LogError(
                    $"[SaveRoom] EXIT WITHOUT COMPLETION phase={lastPhase} lastObject={lastObject ?? "(none)"} " +
                    $"progress={token.Progress:0.##} — clearing wait screen");
                token.Done();
            }
        }
    }

    /// <summary>
    /// Room JSON stores one <see cref="Tracker"/> per placed assembly root (direct child of
    /// the current room, or a scene-root selectable). Nested TrackedObjects must not each
    /// open their own collection — that duplicated whole subtrees on load.
    /// </summary>
    private static bool IsRoomSaveCollectionRoot(TrackedObject obj, Transform roomRoot)
    {
        if (obj == null)
            return false;

        Transform parent = obj.transform.parent;
        if (parent == null)
            return true;
        if (roomRoot != null && parent == roomRoot)
            return true;
        return false;
    }

    public async Task<GameObject> LoadArmAssembly(string file)
    {
        _coldStartFixturesSuppressed = true;
        IsLoading = true;
        AssetPipelineDiagnostics.RoomLoadQuietMode = true;
        BoomConfigLoadDiag.BeginSession($"LoadArmAssembly file={file}");

        try
        {
            if (File.Exists(file))
            {
                CreateTracker();
                string json = File.ReadAllText(file);
                _tracker = JsonConvert.DeserializeObject<Tracker>(json);
                BoomConfigLoadDiag.Event("PARSE",
                    $"objects={_tracker?.objects?.Count ?? 0}");

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
                BoomConfigLoadDiag.Event("ROOT",
                    $"chosen={(gameObject != null ? gameObject.name : "null")} " +
                    $"newObjects={_newObjects?.Count ?? 0} " +
                    $"guidMap={_guidToGameObject?.Count ?? 0} " +
                    $"sandboxRoots={CountTrackedUnderSandbox()} trueRoots={CountTrueTrackedRoots()}");

                foreach (var to in _newObjects)
                {
                    try { to.ApplySavedState(); }
                    catch (Exception ex) { Debug.LogWarning($"[LoadArmAssembly] ApplySavedState failed on {to.name}: {ex.Message}"); }
                }

                FinalizeLoadedInstanceColliders();
                // Compensate under-tube (inside RestoreAllLoadedTransforms), then MoveUp
                // with world-scale-preserving reparent — do not re-apply inverse after MoveUp.
                ScaleAuditLog.Event("LoadArm.beginRestore", $"objects={_newObjects?.Count ?? 0}");
                RestoreAllLoadedTransforms();
                ScaleAuditLog.Event("LoadArm.afterRestore", "pre-MoveUp transforms restored + compensated");
                if (_newObjects != null)
                {
                    foreach (var to in _newObjects)
                    {
                        if (to != null)
                            ScaleAuditLog.Hierarchy("LoadArm.preMoveUp", to.transform, to.name);
                    }
                }
                FinalizeLoadedAttachmentPoints();
                if (_newObjects != null)
                {
                    foreach (var to in _newObjects)
                    {
                        if (to != null)
                            ScaleAuditLog.Hierarchy("LoadArm.postMoveUp", to.transform, to.name);
                    }
                }
                BatchActivateLoadedObjects();
                CompleteDeferredSelectableInitialization();
                RestoreLoadedInstanceColliders();
                SettleLoadedBoomAssembly();
                // Scale repair before accessory presentation — face/cover must see final poses.
                FixLoadedNonUniformDropTubeScales(applyAttachChain: false);
                FinalizeLoadedBoomAccessoryAttach();
                if (_newObjects != null)
                {
                    foreach (var to in _newObjects)
                    {
                        if (to == null) continue;
                        ScaleAuditLog.Hierarchy("LoadArm.final", to.transform, to.name);
                        foreach (Selectable rail in to.GetComponentsInChildren<Selectable>(true))
                        {
                            if (rail == null) continue;
                            LengthScaleKind k = rail.GetLengthScaleKind();
                            if (k == LengthScaleKind.AuthoredIdentity
                                || k == LengthScaleKind.SkuAxisStretch)
                                RailScaleDiag.Dump("LoadArm.final.sh", rail, $"kind={k}");
                        }
                    }
                }

                PricingManager.RebuildPricingFromTrackedObjects();

                if (_newObjects == null || _newObjects.Count == 0)
                {
                    UI_DialogPrompt.Open(
                        "Could not reopen this configuration.\n"
                        + "Required catalog items are missing or offline.\n"
                        + "Check the console for missing GUID details.",
                        new ButtonAction("OK"));
                    if (gameObject != null)
                        Destroy(gameObject);
                    BoomConfigLoadDiag.EndSession("no tracked objects after load");
                    return null;
                }

                // Clear before completion callbacks so any place/attach in listeners
                // gets the normal (!IsLoading) face/cover contract.
                IsLoading = false;
                OnConfigurationLoadComplete?.Invoke(gameObject);
                BoomConfigLoadDiag.EndSession($"ok root={(gameObject != null ? gameObject.name : "null")}");
                return gameObject;
            }
            else
            {
                UI_DialogPrompt.Open(
                    "This configuration file no longer exists.\n"
                    + "Saved configs must live under Saved/Configs to reopen after restart.",
                    new ButtonAction("OK"));
                Debug.LogError($"File at {file} no longer exists");
                BoomConfigLoadDiag.EndSession("file missing");
                return null;
            }
        }
        finally
        {
            BoomConfigLoadDiag.Flush();
            AssetPipelineDiagnostics.RoomLoadQuietMode = false;
            IsLoading = false;
            DestroyRoomLoadSandbox();
        }
    }

    public void LoadRoom(string file)
    {
        AssetPipelineDiagnostics.RoomLoadQuietMode = true;
        AssetPipelineDiagnostics.Log("RoomLoad", $"LoadRoom file='{file}' exists={File.Exists(file)}");

        if (!File.Exists(file))
        {
            AssetPipelineDiagnostics.RoomLoadQuietMode = false;
            UI_DialogPrompt.Open(
                "This room save file no longer exists.\n"
                + "Room saves must live under the AppData Saved folder to reopen.",
                new ButtonAction("OK"));
            return;
        }

        // Stop cold-start bed/light reseeding from racing this load.
        _coldStartFixturesSuppressed = true;
        IsLoading = true;

        CurrentRoomSaveName = Path.GetFileNameWithoutExtension(file).Replace("_", " ");
        CreateTracker();
        string json = File.ReadAllText(file);
        _roomConfiguration = JsonConvert.DeserializeObject<RoomConfiguration>(json);

        // Blank / incomplete saves omit base fixtures. Seed them into the load data so
        // bed + ceiling lights come through the normal ProcessTrackedObjects path —
        // same as any other saved selectable — instead of one-off scene spawners.
        EnsureBaseRoomDefaultsInSaveData();

        List<TrackedObject> existingObjects = FindObjectsOfType<TrackedObject>().ToList();
        Transform roomRoot = GetCurrentRoomTransform();
        string roomName = roomRoot != null ? roomRoot.name : "Room1";

        foreach (TrackedObject to in existingObjects)
        {
            if (to == null) continue;
            Transform topParent = to.transform;
            while (topParent.parent != null) topParent = topParent.parent;
            if (to.transform == to.transform.root
                || topParent.name == roomName
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

    /// <summary>
    /// New/empty rooms are defined as surgical bed + 4 ceiling lights. If a save omits
    /// either, add the default tracked rows so LoadRoom instantiates them normally.
    /// </summary>
    private void EnsureBaseRoomDefaultsInSaveData()
    {
        if (_roomConfiguration.collections == null)
            _roomConfiguration.collections = new List<Tracker>();

        bool hasLights = RoomSaveContainsNameToken("CeilingLight");
        bool hasBed = RoomSaveContainsNameToken("OR_Table");
        if (hasLights && hasBed)
            return;

        Tracker bucket = null;
        foreach (Tracker t in _roomConfiguration.collections)
        {
            if (t?.objects != null)
            {
                bucket = t;
                break;
            }
        }

        if (bucket == null)
        {
            bucket = new Tracker { objects = new List<TrackedObject.Data>() };
            _roomConfiguration.collections.Add(bucket);
        }
        else if (bucket.objects == null)
        {
            bucket.objects = new List<TrackedObject.Data>();
        }

        if (!hasLights)
        {
            Quaternion lightRot = new Quaternion(0.7071068f, 0f, 0f, 0.7071068f);
            foreach (Vector3 pos in DefaultCeilingLightPositions)
            {
                bucket.objects.Add(MakeBaseRoomFixture(
                    "CeilingLightFixture",
                    CeilingLightGuid,
                    pos,
                    lightRot,
                    keepRelativeParentName: CeilingBoundaryName));
            }

            Debug.LogWarning("[LoadRoom] Save omitted ceiling lights — seeding 4 defaults into load data");
        }

        if (!hasBed)
        {
            bucket.objects.Add(MakeBaseRoomFixture(
                "OR_Table_0",
                OperatingTableGuid,
                new Vector3(0f, 0.05715f, 0f),
                new Quaternion(-0.5f, 0.5f, 0.5f, 0.5f),
                keepRelativeParentName: FloorBoundaryName));

            Debug.LogWarning("[LoadRoom] Save omitted OR table — seeding default into load data");
        }
    }

    private static TrackedObject.Data MakeBaseRoomFixture(
        string objectName,
        string globalGuid,
        Vector3 localPosition,
        Quaternion localRotation,
        string keepRelativeParentName = null)
    {
        return new TrackedObject.Data
        {
            objectName = objectName,
            global_guid = globalGuid,
            parentPath = "/Room1",
            parent = "/Room1",
            localPosition = localPosition,
            worldPosition = localPosition,
            localRotation = localRotation,
            worldRotation = localRotation,
            localScale = Vector3.one,
            activeSelf = true,
            keepRelativePositionParentName = keepRelativeParentName,
        };
    }

    private bool RoomSaveContainsNameToken(string token)
    {
        if (_roomConfiguration.collections == null || string.IsNullOrEmpty(token))
            return false;

        foreach (Tracker t in _roomConfiguration.collections)
        {
            if (t?.objects == null) continue;
            foreach (TrackedObject.Data d in t.objects)
            {
                if ((!string.IsNullOrEmpty(d.objectName)
                     && d.objectName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(d.UIButtonname)
                        && d.UIButtonname.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }
        }

        return false;
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

                // MoveUp must run AFTER canonical transforms + under-tube compensation
                // (FixLoadedNonUniformDropTubeScales runs at end of RestoreAllLoadedTransforms).
                // Scale-safe MoveUp preserves world scale so inverse Z does not escape as squash.
                var attachmentTimer = Stopwatch.StartNew();
                FinalizeLoadedAttachmentPoints();
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
                // Scale repair before accessory presentation — face/cover must see final poses.
                FixLoadedNonUniformDropTubeScales(applyAttachChain: false);
                FinalizeLoadedBoomAccessoryAttach();
                LogLoadedArmScaleSnapshot("after SettleLoadedBoomAssembly");

                var pricingTimer = Stopwatch.StartNew();
                int pricedCount = PricingManager.RebuildPricingFromTrackedObjects();
                pricingTimer.Stop();
                AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "rebuildPricing", pricingTimer.ElapsedMilliseconds,
                    $"{pricedCount} priced SelectablePrice(s)");

                progression += progressionTicks;
                token.SetProgress(progression);
            }

            var completeTimer = Stopwatch.StartNew();
            Selectable.NotifyActiveSelectablesInSceneChanged();
            // Clear before completion callbacks (same contract as LoadArmAssembly).
            IsLoading = false;
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
        int bound = BindAttachmentPointInstanceIds();
        BoomConfigLoadDiag.Event("PASS1",
            $"instantiated={instantiateCount} pendingSetup={_pendingSetup.Count} " +
            $"pendingAP={_pendingAttachmentPoints.Count} pendingEmbedded={_pendingEmbedded.Count} " +
            $"apBound={bound} guidMap={_guidToGameObject.Count}");

        pass1Timer.Stop();
        AssetPipelineDiagnostics.LogPhase("RoomLoad.Phase", "pass1_instantiate", pass1Timer.ElapsedMilliseconds,
            $"{instantiateCount} object(s)");

        // Pass 2: multi-wave hierarchy. Sphere APs under the head only become uniquely
        // path-addressable after the head is parented — re-bind APs each wave.
        var pass2Timer = Stopwatch.StartNew();
        var pending = new List<(GameObject go, TrackedObject.Data data)>(_pendingSetup);
        var parented = new HashSet<GameObject>();
        const int maxWaves = 8;
        for (int wave = 0; wave < maxWaves; wave++)
        {
            int newlyParented = 0;
            int orphansThisWave = 0;

            foreach ((GameObject go, TrackedObject.Data data) in pending)
            {
                if (go == null || parented.Contains(go))
                    continue;

                // Roots with no parent links are done once placed in sandbox.
                bool wantsParent = !string.IsNullOrEmpty(data.parentGuid)
                    || !string.IsNullOrEmpty(data.parentPath)
                    || !string.IsNullOrEmpty(data.parent);
                if (!wantsParent)
                {
                    // Leave RoomLoadSandbox so GetRoot / RandomizeInstanceGUIDs see a true root
                    // (legacy single-pass did SetParent(null) when parent resolve returned null).
                    go.transform.SetParent(null, true);
                    parented.Add(go);
                    var rootTracked = go.GetComponent<TrackedObject>();
                    if (rootTracked != null)
                    {
                        rootTracked.StoreValues(data);
                        ResetMaterialPalettes(rootTracked);
                        rootTracked.RestoreTransform(isRoot: true);
                    }
                    if (!string.IsNullOrEmpty(data.keepRelativePositionParentName) &&
                        go.TryGetComponent<KeepRelativePosition>(out var krpRoot))
                        krpRoot.ParentName = data.keepRelativePositionParentName;
                    BoomConfigLoadDiag.Event("PARENT",
                        $"wave={wave} ROOT '{data.objectName}' name={go.name} inst={data.instance_guid} " +
                        $"sceneRoot={go.transform.root.name}");
                    continue;
                }

                string how = null;
                Transform parent = null;
                if (!string.IsNullOrEmpty(data.parentGuid) &&
                    _guidToGameObject.TryGetValue(data.parentGuid, out var parentGO) &&
                    parentGO != null)
                {
                    parent = parentGO.transform;
                    how = "parentGuid";
                }

                if (parent == null)
                {
                    parent = FindLoadedParentTransform(data);
                    if (parent != null)
                        how = "path";
                }

                if (parent == null)
                {
                    orphansThisWave++;
                    continue;
                }

                go.transform.SetParent(parent, false);
                parented.Add(go);
                newlyParented++;

                var trackedObj = go.GetComponent<TrackedObject>();
                if (trackedObj != null)
                {
                    trackedObj.StoreValues(data);
                    ResetMaterialPalettes(trackedObj);
                    trackedObj.RestoreTransform(isRoot: false);
                }

                if (!string.IsNullOrEmpty(data.keepRelativePositionParentName) &&
                    go.TryGetComponent<KeepRelativePosition>(out var krp))
                    krp.ParentName = data.keepRelativePositionParentName;

                bool isAccessory = !string.IsNullOrEmpty(data.objectName) &&
                    (data.objectName.IndexOf("BoomHeadAttachment_", StringComparison.Ordinal) >= 0
                     || data.objectName.IndexOf("GasOutlet", StringComparison.Ordinal) >= 0
                     || data.objectName.IndexOf("Outlet_HV", StringComparison.Ordinal) >= 0
                     || data.objectName.IndexOf("BlankOutlet", StringComparison.Ordinal) >= 0
                     || data.objectName.IndexOf("EthernetOutlet", StringComparison.Ordinal) >= 0
                     || data.objectName.IndexOf("NewBoomHead", StringComparison.Ordinal) >= 0);

                if (isAccessory || how == "parentGuid")
                {
                    BoomConfigLoadDiag.Event("PARENT",
                        $"wave={wave} via={how} '{data.objectName}' -> '{parent.name}' " +
                        $"inst={data.instance_guid} parentGuid={data.parentGuid}");
                }
            }

            int rebound = BindAttachmentPointInstanceIds();
            BoomConfigLoadDiag.Event("WAVE",
                $"wave={wave} newlyParented={newlyParented} stillWaiting={pending.Count - parented.Count} " +
                $"orphansSeen={orphansThisWave} apRebound={rebound} guidMap={_guidToGameObject.Count}");

            if (newlyParented == 0)
                break;
        }

        // Anything still unresolved becomes an orphan (same as legacy single-pass failure).
        foreach ((GameObject go, TrackedObject.Data data) in pending)
        {
            if (go == null || parented.Contains(go))
                continue;

            go.transform.SetParent(null, true);
            BoomConfigLoadDiag.Event("ORPHAN",
                $"'{data.UIButtonname ?? data.objectName}' inst={data.instance_guid} " +
                $"parentGuid={data.parentGuid} parentPath={data.parentPath}");
            Debug.LogError(
                $"[RoomLoad] Orphaned '{data.UIButtonname ?? data.objectName}' — " +
                $"could not resolve parentPath='{data.parentPath}' parent='{data.parent}' parentGuid='{data.parentGuid}'");

            var trackedObj = go.GetComponent<TrackedObject>();
            if (trackedObj != null)
            {
                trackedObj.StoreValues(data);
                ResetMaterialPalettes(trackedObj);
                trackedObj.RestoreTransform(isRoot: true);
            }
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

        string pathWithSlash = NormalizeLoadPathNames(rawPath[0] == '/' ? rawPath : "/" + rawPath);
        GameObject loose = null;
        int looseSegments = 0;
        bool looseTied = false;

        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;

            foreach (Transform t in to.GetComponentsInChildren<Transform>(true))
            {
                // GetLoadComparablePath already normalizes Segement→Segment.
                string live = GetLoadComparablePath(t.gameObject);
                if (live == pathWithSlash)
                    return t.gameObject;

                // During pass 2, later parts are still sandbox siblings — their live path is
                // rooted at their own prefab (`/BoomSegment...(Clone)/...`) while the save
                // path includes the full assembly prefix. Accept a unique-enough suffix match
                // (never bare `/AttachPoint` — many boom nodes share that name).
                // Two boom heads can share the same panel/AP tail — only accept a unique winner.
                if (!TryGetUniquePathSuffixMatch(pathWithSlash, live, out int segments))
                    continue;

                if (segments > looseSegments)
                {
                    looseSegments = segments;
                    loose = t.gameObject;
                    looseTied = false;
                }
                else if (segments == looseSegments && loose != null && t.gameObject != loose)
                {
                    looseTied = true;
                }
            }
        }

        return looseTied ? null : loose;
    }

    /// <summary>
    /// True when <paramref name="saved"/> ends with <paramref name="live"/> (or vice versa)
    /// and the shorter path has enough segments to be unique in a boom tree.
    /// </summary>
    private static bool TryGetUniquePathSuffixMatch(string saved, string live, out int segments)
    {
        segments = 0;
        if (string.IsNullOrEmpty(saved) || string.IsNullOrEmpty(live))
            return false;

        string shorter;
        string longer;
        if (saved.Length >= live.Length)
        {
            longer = saved;
            shorter = live;
        }
        else
        {
            longer = live;
            shorter = saved;
        }

        segments = CountPathSegments(shorter);
        if (segments < 3)
            return false;

        if (!longer.EndsWith(shorter, StringComparison.Ordinal))
            return false;

        if (longer.Length == shorter.Length)
            return true;

        // Paths from GetLoadComparablePath always start with '/'. EndsWith(shorter)
        // already anchors on that slash; requiring longer[pos-1]=='/' rejects every
        // valid suffix (char before is the previous segment's last character).
        if (shorter[0] == '/')
            return true;

        return longer[longer.Length - shorter.Length - 1] == '/';
    }

    private static int CountPathSegments(string path)
    {
        if (string.IsNullOrEmpty(path))
            return 0;
        int count = 0;
        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] == '/')
                continue;
            count++;
            while (i < path.Length && path[i] != '/')
                i++;
        }
        return count;
    }

    private GameObject ProcessEmbeddedSelectable(TrackedObject.Data to)
    {
        try
        {
            // Try to find the embedded object by selfPath, parent, or parentPath
            GameObject go = null;
            string how = null;
            if (!string.IsNullOrEmpty(to.selfPath))
            {
                go = FindInLoadedObjects(to.selfPath) ?? GameObject.Find(NormalizeFindPath(to.selfPath));
                if (go != null) how = "selfPath";
            }
            if (go == null && !string.IsNullOrEmpty(to.parent))
            {
                go = FindInLoadedObjects(to.parent) ?? GameObject.Find(NormalizeFindPath(to.parent));
                if (go != null) how = "parent";
            }
            if (go == null && !string.IsNullOrEmpty(to.parentPath))
            {
                go = FindInLoadedObjects(to.parentPath) ?? GameObject.Find(NormalizeFindPath(to.parentPath));
                if (go != null) how = "parentPath";
            }
            if (go == null)
            {
                BoomConfigLoadDiag.Event("EMBEDDED_FAIL",
                    $"name={to.objectName} selfTail={TrimPathTail(to.selfPath, 6)}");
                throw new NullReferenceException($"Could not find embedded selectable for path: {to.selfPath} or parent: {to.parent}");
            }

            BoomConfigLoadDiag.Event("EMBEDDED", $"via={how} name={to.objectName} live={go.name}");

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
    /// Pre-MoveUp length isolation for loaded objects. Sole discrete writer is
    /// Selectable.SetScaleLevel (via ReapplyLengthScaleIsolation). Ensure only cleans
    /// polluted AP XY first. Runs as ONE global parent-first pass across all loaded
    /// roots — per-root FixLoaded was reapplying arms under still-polluted drop-tube
    /// APs (bad .002 like 2.46,0.89,0.89) and leaving tips slightly low.
    /// Note: Simeon light mesh roots are intentionally ~0.001 (Blender units).
    /// </summary>
    private void FixLoadedNonUniformDropTubeScales(bool applyAttachChain = true)
    {
        if (_newObjects == null)
            return;

        if (applyAttachChain)
        {
            var unique = new HashSet<Selectable>();
            var ordered = new List<Selectable>();
            foreach (TrackedObject to in _newObjects)
            {
                if (to == null) continue;
                foreach (Selectable sel in to.GetComponentsInChildren<Selectable>(true))
                {
                    if (sel == null || !unique.Add(sel)) continue;
                    ordered.Add(sel);
                }
            }

            // Parents before children so drop-tube AP (1,1,1/z) exists before arm Reapply.
            ordered.Sort((a, b) =>
            {
                int da = HierarchyDepth(a.transform);
                int db = HierarchyDepth(b.transform);
                return da != db ? da.CompareTo(db) : string.CompareOrdinal(a.name, b.name);
            });

            foreach (Selectable sel in ordered)
            {
                try { sel.EnsureAttachChainScaleCompensation(reapplyMeshIsolation: false); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FixLoadedNonUniformDropTubeScales] {sel.name}: {ex.Message}");
                }
            }
            foreach (Selectable sel in ordered)
            {
                try { sel.ReapplyLengthScaleIsolation(); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FixLoadedNonUniformDropTubeScales] reapply {sel.name}: {ex.Message}");
                }
            }
        }

        // FBX parent shells (no Selectable) often restore non-unit — BoomDropTube z=0.8,
        // BoomSegment_3 (0.83,1.25,1.25). Unitize + re-bake so world mesh length == Size.
        // Runs after Reapply so a second pass catches mesh≠Size left by stale ScaleZ.
        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;
            foreach (Selectable sel in to.GetComponentsInChildren<Selectable>(true))
            {
                if (sel == null)
                    continue;
                try { sel.FixLengthOwnerParentShellAfterLoad(); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FixLoadedNonUniformDropTubeScales] lengthShell {sel.name}: {ex.Message}");
                }
            }
        }

        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;

            foreach (Transform t in to.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                    continue;
                // True zero only — 0.001 blender mesh roots must stay.
                if (t.localScale.sqrMagnitude >= 1e-8f)
                    continue;
                t.localScale = Vector3.one;
                AssetPipelineDiagnostics.Log("RoomLoad.Scale",
                    $"Repaired zero localScale on {GetLoadComparablePath(t.gameObject)}");
            }
        }
    }

    private static int HierarchyDepth(Transform t)
    {
        int depth = 0;
        while (t != null)
        {
            depth++;
            t = t.parent;
        }
        return depth;
    }

    /// <summary>
    /// After MoveUp + activation: reassemble boom heads and refresh UVs.
    /// Does not re-apply saved locals — that would undo MoveUp parent changes.
    /// </summary>
    private void SettleLoadedBoomAssembly()
    {
        if (_newObjects == null)
            return;

        var heads = new HashSet<BoomHeadScaleHandler>();
        foreach (TrackedObject to in _newObjects)
        {
            if (to == null)
                continue;

            foreach (BoomHeadScaleHandler boomHead in to.GetComponentsInChildren<BoomHeadScaleHandler>(true))
            {
                if (boomHead == null) continue;
                if (!heads.Add(boomHead))
                    continue;
                if (!boomHead.TryGetComponent(out Selectable sel) || sel.CurrentScaleLevel == null)
                    continue;
                try { boomHead.ReassembleRows(sel.CurrentScaleLevel); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SettleLoadedBoomAssembly] ReassembleRows failed on {boomHead.name}: {ex.Message}");
                }
                boomHead.RowsSettledByConfigLoad = true;
                BoomConfigLoadDiag.Event("SETTLE",
                    $"ReassembleRows on {boomHead.name} scaleSize={sel.CurrentScaleLevel.Size}");

                // Category settle: rails stay AuthoredIdentity; shelves re-get SKU X.
                foreach (Selectable child in boomHead.GetComponentsInChildren<Selectable>(true))
                {
                    if (child == null) continue;
                    try
                    {
                        switch (child.GetLengthScaleKind())
                        {
                            case LengthScaleKind.AuthoredIdentity:
                                child.EnsureAuthoredIdentityMountScale();
                                break;
                            case LengthScaleKind.SkuAxisStretch:
                                Selectable.ApplyServiceHeadShelfSkuScale(child.gameObject);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[SettleLoadedBoomAssembly] SH scale {child.name}: {ex.Message}");
                    }
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

    /// <summary>
    /// After boom rows are active: same ApplyBoomAccessoryPresentation as menu place
    /// for every AP under each service head.
    /// </summary>
    private void FinalizeLoadedBoomAccessoryAttach()
    {
        if (_newObjects == null)
            return;

        var heads = new HashSet<BoomHeadScaleHandler>();
        foreach (TrackedObject to in _newObjects)
        {
            if (to == null) continue;
            foreach (var head in to.GetComponentsInChildren<BoomHeadScaleHandler>(true))
            {
                if (head != null)
                    heads.Add(head);
            }
        }

        var seenAps = new HashSet<AttachmentPoint>();
        int finalized = 0;
        int withAttached = 0;

        foreach (BoomHeadScaleHandler head in heads)
        {
            if (head == null) continue;
            foreach (AttachmentPoint ap in head.GetComponentsInChildren<AttachmentPoint>(true))
            {
                if (ap == null || !seenAps.Add(ap))
                    continue;
                finalized++;
                if (ap.AttachedSelectable != null && ap.AttachedSelectable.Count > 0)
                    withAttached++;
                try { ap.FinalizeLoadedAccessoryPresentation(); }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[FinalizeLoadedBoomAccessoryAttach] failed on {ap.name}: {ex.Message}");
                }
            }
        }

        BoomConfigLoadDiag.Event("ATTACH_PRESENT_DONE",
            $"heads={heads.Count} aps={finalized} withAttached={withAttached}");
        DumpBoomHeadAccessoryState(heads);
    }

    /// <summary>
    /// Post-load truth for "missing / inverted / slots dead": cover on/off, attached vs
    /// children, active flags, renderer enabled.
    /// </summary>
    void DumpBoomHeadAccessoryState(HashSet<BoomHeadScaleHandler> heads)
    {
        Camera cam = Camera.main;
        Vector3 towardCam = cam != null ? -cam.transform.forward : Vector3.forward;

        foreach (BoomHeadScaleHandler head in heads)
        {
            if (head == null) continue;
            BoomConfigLoadDiag.Event("HEAD", $"name={head.name} active={head.gameObject.activeInHierarchy}");

            // Empty panel spheres / plate attach slots — interactable diagnosis
            foreach (AttachmentPoint ap in head.GetComponentsInChildren<AttachmentPoint>(true))
            {
                if (ap == null) continue;
                bool isPanelSphere = ap.name.StartsWith("Sphere", StringComparison.Ordinal);
                bool isPlateSlot = ap.name.StartsWith("AttachPoint", StringComparison.Ordinal);
                if (!isPanelSphere && !isPlateSlot)
                    continue;

                int wired = ap.AttachedSelectable?.Count ?? 0;
                int childSel = 0;
                for (int i = 0; i < ap.transform.childCount; i++)
                {
                    if (ap.transform.GetChild(i).GetComponent<Selectable>() != null)
                        childSel++;
                }

                Collider col = ap.GetComponentInChildren<Collider>(true);
                bool colOn = col != null && col.enabled;
                bool activeHier = ap.gameObject.activeInHierarchy;
                bool expectInteractable = wired == 0 && activeHier;
                string flag = "";
                if (expectInteractable && !colOn)
                    flag = " DEAD_SLOT";
                if (wired == 0 && childSel > 0)
                    flag += " WIRE_MISS";
                if (wired > 0 && childSel == 0)
                    flag += " ORPHAN_REF";

                if (!string.IsNullOrEmpty(flag) || isPanelSphere || (isPlateSlot && (wired > 0 || childSel > 0)))
                {
                    BoomConfigLoadDiag.Event("SLOT",
                        $"{ap.name} pathTail={ap.transform.parent?.name}/{ap.name} " +
                        $"activeHier={activeHier} wired={wired} childSel={childSel} " +
                        $"col={(col != null ? col.GetType().Name : "null")}:{(colOn ? "on" : "off")}{flag}");
                }
            }

            foreach (Transform plate in head.GetComponentsInChildren<Transform>(true))
            {
                if (plate == null || !plate.name.StartsWith("BoomHeadAttachment_", StringComparison.Ordinal))
                    continue;
                if (plate.GetComponent<Selectable>() == null)
                    continue;

                bool coverOn = false;
                bool coverColOn = false;
                float coverVsCam = 0f;
                if (plate.TryGetComponent(out MeshRenderer cover))
                {
                    coverOn = cover.enabled;
                    coverVsCam = Vector3.Dot(plate.forward, towardCam);
                    foreach (var col in plate.GetComponents<Collider>())
                    {
                        if (col != null && col.enabled)
                            coverColOn = true;
                    }
                }

                Vector3 lossy = plate.lossyScale;
                bool negScale = lossy.x < 0f || lossy.y < 0f || lossy.z < 0f;

                var aps = plate.GetComponentsInChildren<AttachmentPoint>(true);
                int attached = 0;
                int childSel = 0;
                var detail = new System.Text.StringBuilder();
                foreach (var ap in aps)
                {
                    if (ap == null) continue;
                    int n = ap.AttachedSelectable?.Count ?? 0;
                    attached += n;
                    for (int i = 0; i < ap.transform.childCount; i++)
                    {
                        var ch = ap.transform.GetChild(i);
                        if (!ch.TryGetComponent(out Selectable sel)) continue;
                        childSel++;
                        bool rendOn = false;
                        string mat0 = "-";
                        var mrs = sel.GetComponentsInChildren<MeshRenderer>(true);
                        foreach (var mr in mrs)
                        {
                            if (mr == null) continue;
                            if (mr.enabled && mr.gameObject.activeInHierarchy)
                                rendOn = true;
                            if (mat0 == "-" && mr.sharedMaterial != null)
                                mat0 = mr.sharedMaterial.name.Replace(" (Instance)", "");
                        }
                        Vector3 ls = sel.transform.lossyScale;
                        bool neg = ls.x < 0f || ls.y < 0f || ls.z < 0f;
                        detail.Append(
                            $" [{sel.name} activeHier={sel.gameObject.activeInHierarchy} " +
                            $"apWired={n > 0} rend={rendOn} mat0={mat0} " +
                            $"lossy=({ls.x:F2},{ls.y:F2},{ls.z:F2}) neg={neg}]");
                    }
                }

                string plateFlag = "";
                if (coverOn && childSel > 0)
                    plateFlag += " COVER_HIDES_CHILDREN";
                if (attached > 0 && childSel == 0)
                    plateFlag += " ATTACHED_BUT_NO_CHILDREN";
                if (childSel > 0 && !plate.gameObject.activeInHierarchy)
                    plateFlag += " PLATE_INACTIVE";
                if (negScale)
                    plateFlag += " NEG_SCALE";
                if (coverColOn && !coverOn)
                    plateFlag += " COVER_COL_BLOCKS";
                if (coverOn && coverColOn && childSel == 0)
                    plateFlag += " COVER_BLOCKS_EMPTY";

                BoomConfigLoadDiag.Event("PLATE",
                    $"{plate.name} activeHier={plate.gameObject.activeInHierarchy} coverOn={coverOn} " +
                    $"coverCol={coverColOn} coverVsCam={coverVsCam:F2} " +
                    $"lossy=({lossy.x:F2},{lossy.y:F2},{lossy.z:F2}) " +
                    $"aps={aps.Length} attachedRefs={attached} childSelectables={childSel}{plateFlag}{detail}");
            }
        }
    }

    private void LogLoadedArmScaleSnapshot(string phase)
    {
        if (_newObjects == null)
            return;

        ScaleAuditLog.Event("LoadRoom.phase", phase);
        foreach (TrackedObject root in _newObjects)
        {
            if (root == null)
                continue;
            ScaleAuditLog.Hierarchy("LoadRoom." + phase, root.transform, root.name);
        }

        if (AssetPipelineDiagnostics.RoomLoadQuietMode)
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
                    $"{phase}: {GetLoadComparablePath(t.gameObject)} " +
                    $"localScale={t.localScale.ToString("G4")} lossyScale={t.lossyScale.ToString("G4")} " +
                    $"localRot={t.localEulerAngles.ToString("G4")}{selectedScale}{saved}");
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

        // Load naming contract:
        // - Assembly root: GO name = saved instance_guid (existing path system for embedded parts).
        // - Catalog accessories: keep prefab/clone names; id lives on TrackedObject/Selectable.guid only.
        if (go.TryGetComponent(out TrackedObject tracked) &&
            !string.IsNullOrEmpty(trackedObject.instance_guid))
            tracked.ApplyRuntimeInstanceId(trackedObject.instance_guid);
        else if (go.TryGetComponent(out Selectable selectableOnly) &&
                 !string.IsNullOrEmpty(trackedObject.instance_guid))
            selectableOnly.guid = trackedObject.instance_guid;

        if (IsSaveAssemblyRoot(trackedObject) && !string.IsNullOrEmpty(trackedObject.instance_guid))
        {
            go.name = trackedObject.instance_guid;
            BoomConfigLoadDiag.Event("INST_ROOT",
                $"named root '{trackedObject.objectName}' -> {go.name}");
        }

        if (go.TryGetComponent(out Selectable selectable))
            selectable.UIButtonName = trackedObject.UIButtonname;

        return go;
    }

    /// <summary>
    /// Free-placed assembly root in a save (no parent links). Roots use guid-as-name for paths;
    /// accessories must not.
    /// </summary>
    private static bool IsSaveAssemblyRoot(TrackedObject.Data data)
    {
        if (data.isAttachmentPoint)
            return false;
        return string.IsNullOrEmpty(data.parentGuid)
            && string.IsNullOrEmpty(data.parentPath)
            && string.IsNullOrEmpty(data.parent);
    }

    private void RegisterGuidsForLoadedObjects()
    {
        foreach ((GameObject go, TrackedObject.Data data) in _pendingSetup)
        {
            if (!string.IsNullOrEmpty(data.instance_guid))
                _guidToGameObject[data.instance_guid] = go;

            if (go.TryGetComponent(out TrackedObject rootTracked))
            {
                string id = rootTracked.GetRuntimeInstanceId();
                if (string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(data.instance_guid))
                {
                    rootTracked.ApplyRuntimeInstanceId(data.instance_guid);
                    id = data.instance_guid;
                }
                if (!string.IsNullOrEmpty(id))
                    _guidToGameObject[id] = go;
            }

            foreach (Selectable sel in go.GetComponentsInChildren<Selectable>(true))
            {
                if (!string.IsNullOrEmpty(sel.guid))
                    _guidToGameObject[sel.guid] = sel.gameObject;
            }
        }
    }

    /// <summary>
    /// Stamps saved attachment-point instance ids onto live APs under already-instantiated
    /// plates/heads so pass-2 can parent outlets by parentGuid.
    /// Prefer parentGuid + local AP name (unique under one plate); fall back to path match.
    /// Returns how many APs newly registered this call.
    /// </summary>
    private int BindAttachmentPointInstanceIds()
    {
        if (_pendingAttachmentPoints == null || _pendingAttachmentPoints.Count == 0)
            return 0;

        int newlyBound = 0;
        int failed = 0;

        foreach (TrackedObject.Data apData in _pendingAttachmentPoints)
        {
            if (string.IsNullOrEmpty(apData.instance_guid))
                continue;

            if (_guidToGameObject.ContainsKey(apData.instance_guid))
                continue;

            GameObject apGO = null;
            string how = null;

            // Prefer: find parent selectable/plate by parentGuid, then child AP by leaf name.
            // Require a unique leaf match — duplicate names under one parent must use path.
            if (!string.IsNullOrEmpty(apData.parentGuid) &&
                _guidToGameObject.TryGetValue(apData.parentGuid, out GameObject parentGO) &&
                parentGO != null)
            {
                string leaf = GetPathLeafName(!string.IsNullOrEmpty(apData.selfPath) ? apData.selfPath : apData.parent);
                if (!string.IsNullOrEmpty(leaf))
                {
                    AttachmentPoint unique = null;
                    int hits = 0;
                    foreach (AttachmentPoint ap in parentGO.GetComponentsInChildren<AttachmentPoint>(true))
                    {
                        if (ap == null || ap.name != leaf)
                            continue;
                        hits++;
                        unique = ap;
                        if (hits > 1)
                            break;
                    }
                    if (hits == 1 && unique != null)
                    {
                        apGO = unique.gameObject;
                        how = $"parentGuid+leaf:{leaf}";
                    }
                }
            }

            if (apGO == null && !string.IsNullOrEmpty(apData.selfPath))
            {
                apGO = FindInLoadedObjects(apData.selfPath);
                if (apGO != null) how = "selfPath";
            }
            if (apGO == null && !string.IsNullOrEmpty(apData.parent))
            {
                apGO = FindInLoadedObjects(apData.parent);
                if (apGO != null) how = "parent";
            }
            if (apGO == null && !string.IsNullOrEmpty(apData.parentPath))
            {
                apGO = FindInLoadedObjects(apData.parentPath);
                if (apGO != null) how = "parentPath";
            }

            if (apGO == null)
            {
                failed++;
                if (!string.IsNullOrEmpty(apData.attachedObject) ||
                    (!string.IsNullOrEmpty(apData.selfPath) &&
                     apData.selfPath.IndexOf("BoomHead", StringComparison.Ordinal) >= 0))
                {
                    BoomConfigLoadDiag.Event("AP_BIND_FAIL",
                        $"inst={apData.instance_guid} attached={apData.attachedObject} " +
                        $"parentGuid={apData.parentGuid} leaf={GetPathLeafName(apData.selfPath)} " +
                        $"selfTail={TrimPathTail(apData.selfPath, 5)}");
                }
                continue;
            }

            AttachmentPoint attPoint = apGO.GetComponent<AttachmentPoint>()
                ?? apGO.GetComponentInChildren<AttachmentPoint>(true);
            if (attPoint == null || !attPoint.TryGetComponent(out TrackedObject tracked))
            {
                failed++;
                BoomConfigLoadDiag.Event("AP_BIND_FAIL",
                    $"inst={apData.instance_guid} foundGO={apGO.name} but no AttachmentPoint/TrackedObject");
                continue;
            }

            tracked.ApplyRuntimeInstanceId(apData.instance_guid);
            _guidToGameObject[apData.instance_guid] = attPoint.gameObject;
            newlyBound++;
            BoomConfigLoadDiag.Event("AP_BIND",
                $"via={how} inst={apData.instance_guid} live={attPoint.name} attached={apData.attachedObject}");
        }

        if (failed > 0)
            BoomConfigLoadDiag.Event("AP_BIND_SUMMARY", $"newlyBound={newlyBound} stillUnbound={failed}");

        return newlyBound;
    }

    private static string TrimPathTail(string path, int segments)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= segments)
            return path;
        return string.Join("/", parts, parts.Length - segments, segments);
    }

    private static string GetPathLeafName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        int slash = path.LastIndexOf('/');
        return slash >= 0 && slash < path.Length - 1 ? path.Substring(slash + 1) : path;
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

        if (go.TryGetComponent(out TrackedObject tracked) &&
            !string.IsNullOrEmpty(trackedObject.instance_guid))
            tracked.ApplyRuntimeInstanceId(trackedObject.instance_guid);

        // Same root-name contract as InstantiateObjectForLoad (path lookups during load).
        if (IsLoading && IsSaveAssemblyRoot(trackedObject) &&
            !string.IsNullOrEmpty(trackedObject.instance_guid))
            go.name = trackedObject.instance_guid;

        var selectable = go.GetComponent<Selectable>();
        if (selectable != null)
        {
            if (!string.IsNullOrEmpty(trackedObject.instance_guid))
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
                to.ApplyRuntimeInstanceId(newGuid);
                to.gameObject.name = newGuid;
                _guidToGameObject[newGuid] = to.gameObject;
            }
        }
    }

    private void LogData(Selectable s, TrackedObject.Data to) => s.GetComponent<TrackedObject>().StoreValues(to);

    int CountTrueTrackedRoots()
    {
        if (_newObjects == null) return 0;
        int n = 0;
        foreach (TrackedObject x in _newObjects)
        {
            if (x != null && x.transform == x.transform.root)
                n++;
        }
        return n;
    }

    int CountTrackedUnderSandbox()
    {
        if (_newObjects == null) return 0;
        int n = 0;
        foreach (TrackedObject x in _newObjects)
        {
            if (x == null) continue;
            Transform r = x.transform.root;
            if (r != null && r.name == "RoomLoadSandbox")
                n++;
        }
        return n;
    }

    /// <summary>
    /// Returns the loaded assembly root. Arm configs often orphan parts when parent paths
    /// cannot resolve (missing catalog items / prefab renames); never throw on multiples.
    /// </summary>
    private GameObject GetRoot()
    {
        if (_newObjects == null || _newObjects.Count == 0)
            return null;

        var roots = new List<TrackedObject>();
        foreach (TrackedObject x in _newObjects)
        {
            if (x != null && x.transform == x.transform.root)
                roots.Add(x);
        }

        // Fallback: still parented under load sandbox (should be rare after root unparent).
        if (roots.Count == 0)
        {
            foreach (TrackedObject x in _newObjects)
            {
                if (x == null || x.transform.parent == null)
                    continue;
                if (x.transform.parent.name == "RoomLoadSandbox" && x.GetComponent<Selectable>() != null)
                    roots.Add(x);
            }
            BoomConfigLoadDiag.Event("ROOT_FALLBACK",
                $"no true roots; sandbox-direct candidates={roots.Count}");
        }

        if (roots.Count == 0)
            return null;
        if (roots.Count == 1)
            return roots[0].gameObject;

        TrackedObject best = null;
        int bestScore = int.MinValue;
        foreach (TrackedObject r in roots)
        {
            int score = r.GetComponentsInChildren<TrackedObject>(true).Length;
            if (r.GetComponent<Selectable>() != null)
                score += 100;
            if (r.HasStoredValues
                && string.IsNullOrEmpty(r.data.parentGuid)
                && string.IsNullOrEmpty(r.data.parentPath)
                && string.IsNullOrEmpty(r.data.parent))
                score += 10000;

            if (score > bestScore)
            {
                bestScore = score;
                best = r;
            }
        }

        Debug.LogWarning(
            $"[LoadArmAssembly] Expected one root, found {roots.Count}. Using '{best?.name}'. " +
            "Check console for [RoomLoad] Orphaned parent-resolve failures.");
        return best != null ? best.gameObject : null;
    }

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
