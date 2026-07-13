using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(FullScreenMenu))]
public class UI_ObjExportOptions : MonoBehaviour
{
    private static Loading.LoadingToken _loadingTokenOverall;
    private static Loading.LoadingToken _loadingTokenCombiningMeshes;
    private static Loading.LoadingToken _loadingTokenUpload;
    private static Loading.LoadingToken _loadingTokenWaitForResponse;
    private static Loading.LoadingToken _loadingTokenWriteObj;

    private static UI_ObjExportOptions Instance { get; set; }

    [field: SerializeField]
    private Toggle ToggleIncludeFloor { get; set; }

    [field: SerializeField]
    private Toggle ToggleIncludeFloorObjects { get; set; }

    [field: SerializeField]
    private Toggle ToggleIncludeCeiling { get; set; }

    [field: SerializeField]
    private Toggle ToggleIncludeCeilingObjects { get; set; }

    [field: SerializeField]
    private Toggle ToggleIncludeWalls { get; set; }

    [field: SerializeField]
    private Toggle ToggleIncludeWallObjects { get; set; }

    [field: SerializeField]
    private Toggle ToggleIncludeArmAssemblies { get; set; }

    [field: SerializeField]
    private Toggle ToggleIncludeAssemblyHeads { get; set; }

    [field: SerializeField]
    private Button ButtonExportSelectedObject { get; set; }

    private UnityEventManager _eventManager = new();

    private void Awake()
    {
        // Don't steal the singleton if another live instance already owns it
        // (e.g. temporary clones used as UI chrome templates).
        if (Instance == null || Instance == this)
            Instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.SetActive(false);

        _eventManager.RegisterEvents
            ((ObjExporter.ExportFinishedSuccessfully, OnExportFinishedSuccessfully));

        _eventManager.RegisterEvents
            ((ObjExporter.OnExportFinished, FinishAllLoadingTokens),
            (ObjExporter.OnMeshCombineSuccess, OnMeshCombineSuccess),
            (ObjExporter.OnExportStarted, OnExportStarted));

        _eventManager.RegisterEvents
            ((ObjExporter.OnSubMeshProcessed, OnSubMeshProcessed),
            (ObjExporter.OnMeshCombiningUpdate, OnMeshCombiningUpdate));

        _eventManager.AddListeners();
    }

    private void OnDestroy()
    {
        _eventManager.RemoveListeners();
    }

    private void OnEnable()
    {
        if (ButtonExportSelectedObject == null)
            return;

        if (_customizeMode)
        {
            ButtonExportSelectedObject.gameObject.SetActive(false);
            return;
        }

        ButtonExportSelectedObject.gameObject.SetActive(Selectable.SelectedSelectables.Count > 0);
    }

    private void OnSubMeshProcessed(float progress)
    {
        _loadingTokenWriteObj?.SetProgress(progress);
    }

    private void OnMeshDataWritten()
    {
        _loadingTokenOverall?.SetProgress(0.75f);
    }

    private void OnExportStarted()
    {
        // Unified export already owns the progress UI — don't also open the legacy loading screen.
        if (ExportOrchestrator.SuppressIndividualDialogs)
            return;

        _loadingTokenOverall = Loading.GetLoadingToken();
        _loadingTokenCombiningMeshes = Loading.GetLoadingToken();
        _loadingTokenUpload = Loading.GetLoadingToken();
        _loadingTokenWaitForResponse = Loading.GetLoadingToken();
        _loadingTokenWriteObj = Loading.GetLoadingToken();
    }

    private void OnMeshCombineSuccess()
    {
        _loadingTokenOverall?.SetProgress(0.25f);
    }

    private void OnExportFinishedSuccessfully(string path)
    {
        if (ExportOrchestrator.SuppressIndividualDialogs)
            return;

        UI_DialogPrompt.Open(
            $"3D model (GLB) saved.",
            new ButtonAction("Done"));
        ExportFolderUtility.RevealInFileManager(path);
    }

    private void OnMeshCombiningUpdate(float progress)
    {
        _loadingTokenCombiningMeshes?.SetProgress(progress);
    }

    private static Action<ObjExportOptions> _onCustomizeApplied;
    private static bool _customizeMode;
    private string _exportAllLabel;
    private string _exportSelectionLabel;

    public static void Open()
    {
        ExitCustomizeModeStatic();
        EnsureInstance();
        if (Instance == null)
        {
            UI_DialogPrompt.Open(
                "3D model options UI is missing from the scene.",
                new ButtonAction("OK"));
            return;
        }

        Instance.gameObject.SetActive(true);
    }

    /// <summary>
    /// Opens 3D include toggles so the user can refine what goes into a later orchestrated export.
    /// Does not export immediately — Apply returns the options to the caller.
    /// </summary>
    public static void OpenForCustomization(Action<ObjExportOptions> onApplied)
    {
        EnsureInstance();
        if (Instance == null)
        {
            UI_DialogPrompt.Open(
                "3D model options UI is missing from the scene.",
                new ButtonAction("OK"));
            return;
        }

        _onCustomizeApplied = onApplied;
        _customizeMode = true;
        Instance.ApplyCustomizeChrome(true);

        var title = Instance.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)
            .FirstOrDefault(t => t.text.Contains("OBJ Export") || t.text.Contains("3D Model") || t.text.Contains("Export Options"));
        if (title != null)
            title.text = "Room 3D Model Contents";

        Instance.gameObject.SetActive(true);
    }

    private static void EnsureInstance()
    {
        if (Instance != null)
            return;

        var prefab = Resources.Load<GameObject>("Prefabs/UI_ObjExportOptions");
        if (prefab == null)
        {
            Debug.LogError("Missing Resources/Prefabs/UI_ObjExportOptions.");
            return;
        }

        var go = Instantiate(prefab);
        go.name = nameof(UI_ObjExportOptions);
        if (go.GetComponent<FullScreenMenu>() == null)
            go.AddComponent<FullScreenMenu>();
        go.SetActive(false);
        DontDestroyOnLoad(go);
        // Awake assigns Instance.
    }

    private static void ExitCustomizeModeStatic()
    {
        _customizeMode = false;
        _onCustomizeApplied = null;
        if (Instance != null)
        {
            Instance.ApplyCustomizeChrome(false);
            var title = Instance.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)
                .FirstOrDefault(t => t.text.Contains("Room 3D Model Contents") || t.text.Contains("OBJ Export"));
            if (title != null)
                title.text = "3D Model Options";
        }
    }

    private void ApplyCustomizeChrome(bool customize)
    {
        foreach (var button in GetComponentsInChildren<Button>(true))
        {
            string n = button.gameObject.name;
            var label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (label == null)
                continue;

            if (n.Contains("ExportScene") || n.Contains("ExportAll"))
            {
                if (customize)
                {
                    if (string.IsNullOrEmpty(_exportAllLabel))
                        _exportAllLabel = label.text;
                    label.text = "Use these settings";
                }
                else if (!string.IsNullOrEmpty(_exportAllLabel))
                {
                    label.text = _exportAllLabel;
                }
            }
            else if (n.Contains("ExportSelection") || n.Contains("Selection"))
            {
                if (customize)
                {
                    if (string.IsNullOrEmpty(_exportSelectionLabel))
                        _exportSelectionLabel = label.text;
                    button.gameObject.SetActive(false);
                }
                else if (!string.IsNullOrEmpty(_exportSelectionLabel))
                {
                    label.text = _exportSelectionLabel;
                    button.gameObject.SetActive(Selectable.SelectedSelectables.Count > 0);
                }
            }
        }
    }

    private void OnDisable()
    {
        // Closed without Apply while refining options — return to the export hub.
        if (!_customizeMode)
            return;

        ExitCustomizeModeStatic();
        UI_ExportOptions.Reopen();
    }

    public ObjExportOptions GetOptions()
    {
        return new()
        {
            IncludeFloor = ToggleIncludeFloor.isOn,
            IncludeFloorObjects = ToggleIncludeFloorObjects.isOn,
            IncludeCeiling = ToggleIncludeCeiling.isOn,
            IncludeCeilingObjects = ToggleIncludeCeilingObjects.isOn,
            IncludeWalls = ToggleIncludeWalls.isOn,
            IncludeWallObjects = ToggleIncludeWallObjects.isOn,
            IncludeArmBoomAssemblies = ToggleIncludeArmAssemblies.isOn,
            IncludeArmBoomHeads = ToggleIncludeAssemblyHeads.isOn,
        };
    }

    public void ExportAllObjects()
    {
        if (_customizeMode)
        {
            var opts = GetOptions();
            var cb = _onCustomizeApplied;
            ExitCustomizeModeStatic();
            gameObject.SetActive(false);
            cb?.Invoke(opts);
            return;
        }

        var selectables = Selectable.ActiveSelectables;
        var options = GetOptions();
        ExportPaths.PromptForExportFolderThen(() =>
        {
            DoExport(true, selectables, options);
            ExportPaths.ClearExportBaseOverride();
            gameObject.SetActive(false);
        });
    }

    public void ExportSelectedObject()
    {
        if (_customizeMode)
        {
            ExportAllObjects();
            return;
        }

        if (Selectable.SelectedSelectables.Count == 0)
            return;

        GameObject target = Selectable.SelectedSelectables[0].TryGetArmAssemblyRoot(out GameObject obj)
            ? obj
            : Selectable.SelectedSelectables[0].gameObject;
        var options = GetOptions();

        ExportPaths.PromptForExportFolderThen(() =>
        {
            DoExport(true, target, options);
            ExportPaths.ClearExportBaseOverride();
            gameObject.SetActive(false);
        });
    }

    public static void DoExport(
    bool makeSubmeshes,
    List<Selectable> selectables,
    ObjExportOptions options)
    {
        TryDoExport(makeSubmeshes, selectables, options);
    }

    public static bool TryDoExport(
        bool makeSubmeshes,
        List<Selectable> selectables,
        ObjExportOptions options)
    {
        MeshFilter[] meshFilters = selectables
            //.Where(x => x.transform.root == x.transform)
            .Where(x =>
            {
                if (x.TryGetComponent<RoomBoundary>(out var roomBoundary))
                {
                    switch (roomBoundary.RoomBoundaryType)
                    {
                        case RoomBoundaryType.Ceiling:
                            return options.IncludeCeiling;
                        case RoomBoundaryType.Floor:
                            return options.IncludeFloor;
                        case RoomBoundaryType.WallSouth:
                        case RoomBoundaryType.WallNorth:
                        case RoomBoundaryType.WallEast:
                        case RoomBoundaryType.WallWest:
                            return options.IncludeWalls;
                    }
                }

                if (x.TryGetComponent<Selectable>(out var selectable))
                {
                    if (selectable.SpecialTypes.Contains(SpecialSelectableType.Mount))
                    {
                        return options.IncludeArmBoomAssemblies;
                    }

                    float angle = Vector3.Angle(selectable.transform.forward, Vector3.down);

                    if (angle < 5)
                    {
                        return options.IncludeCeilingObjects;
                    }

                    if (angle > 175)
                    {
                        return options.IncludeFloorObjects;
                    }

                    return options.IncludeWallObjects;
                }

                Debug.LogWarning($"Could not determine 3D export category for {x.gameObject.name}");

                return false;
            })
            .SelectMany(x => x.GetComponentsInChildren<MeshRenderer>())
            .Where(x => FilterMeshRenderers(x, options))
            .Select(x => x.gameObject.GetComponent<MeshFilter>())
            .Where(mf => mf != null)
            .ToArray();

        if (meshFilters.Length == 0)
            return false;

        string roomName = ExportPaths.SanitizeFolderName(ExportPaths.GetRoomExportName());
        ObjExporter.DoExport(makeSubmeshes, meshFilters, roomName, roomPackage: true);
        return true;
    }

    private static bool FilterMeshRenderers(MeshRenderer meshRenderer, ObjExportOptions options)
    {
        if (!meshRenderer.enabled)
            return false;

        if (options.IncludeArmBoomHeads)
        {
            return true;
        }

        Transform parent = meshRenderer.transform;

        while (parent != null)
        {
            // Best way to determine if something is
            // a boom/arm head
            if (parent.GetComponent<CCDIK>() != null)
            {
                return false;
            }

            parent = parent.parent;
        }

        return true;
    }

    public static void DoExport(bool makeSubmeshes, GameObject obj, ObjExportOptions options)
    {
        TryDoExport(makeSubmeshes, obj, options);
    }

    public static bool TryDoExport(bool makeSubmeshes, GameObject obj, ObjExportOptions options)
    {
        MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshRenderer>()
            .Where(x => FilterMeshRenderers(x, options))
            .Select(item => item.gameObject.GetComponent<MeshFilter>())
            .Where(mf => mf != null)
            .ToArray();

        if (meshFilters.Length == 0)
            return false;

        string exportName = ExportPaths.GetObjectExportName(obj, "Object");
        ObjExporter.DoExport(makeSubmeshes, meshFilters, exportName);
        return true;
    }

    private static void FinishAllLoadingTokens()
    {
        _loadingTokenOverall?.Done();
        _loadingTokenCombiningMeshes?.Done();
        _loadingTokenUpload?.Done();
        _loadingTokenWaitForResponse?.Done();
        _loadingTokenWriteObj?.Done();
        _loadingTokenOverall = null;
        _loadingTokenCombiningMeshes = null;
        _loadingTokenUpload = null;
        _loadingTokenWaitForResponse = null;
        _loadingTokenWriteObj = null;
    }
}

public class ObjExportOptions
{
    public bool IncludeFloor { get; set; }
    public bool IncludeFloorObjects { get; set; }
    public bool IncludeWalls { get; set; }
    public bool IncludeWallObjects { get; set; }
    public bool IncludeCeiling { get; set; }
    public bool IncludeCeilingObjects { get; set; }
    public bool IncludeArmBoomAssemblies { get; set; }
    public bool IncludeArmBoomHeads { get; set; }

    public static ObjExportOptions CreateDefaults()
    {
        return new ObjExportOptions
        {
            IncludeFloor = true,
            IncludeFloorObjects = true,
            IncludeWalls = true,
            IncludeWallObjects = true,
            IncludeCeiling = true,
            IncludeCeilingObjects = true,
            IncludeArmBoomAssemblies = true,
            IncludeArmBoomHeads = true
        };
    }
}