using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

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
        ButtonExportSelectedObject.gameObject.SetActive(Selectable.SelectedSelectables.Count > 0);
    }

    private void OnSubMeshProcessed(float progress)
    {
        _loadingTokenWriteObj.SetProgress(progress);
    }

    private void OnMeshDataWritten()
    {
        _loadingTokenOverall.SetProgress(0.75f);
    }

    private void OnExportStarted()
    {
        _loadingTokenOverall = Loading.GetLoadingToken();
        _loadingTokenCombiningMeshes = Loading.GetLoadingToken();
        _loadingTokenUpload = Loading.GetLoadingToken();
        _loadingTokenWaitForResponse = Loading.GetLoadingToken();
        _loadingTokenWriteObj = Loading.GetLoadingToken();
    }

    private void OnMeshCombineSuccess()
    {
        _loadingTokenOverall.SetProgress(0.25f);
    }

    private void OnExportFinishedSuccessfully(string path)
    {
        UI_DialogPrompt.Open(
            $"Success! OBJ saved to {path}",
            new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = path),
            new ButtonAction("Done"));

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        // Hack fix for macOS not liking Application.OpenURL
        string location = path;
        ProcessStartInfo startInfo = new ProcessStartInfo("/System/Library/CoreServices/Finder.app")
        {
            WindowStyle = ProcessWindowStyle.Normal,
            FileName = location.Trim()
        };
        Process.Start(startInfo);
#endif

        Application.OpenURL("file:///" + path);
    }

    private void OnMeshCombiningUpdate(float progress)
    {
        _loadingTokenCombiningMeshes.SetProgress(progress);
    }

    public static void Open()
    {
        Instance.gameObject.SetActive(true);
    }

    private ObjExportOptions GetOptions()
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
        DoExport(
            true, 
            Selectable.ActiveSelectables,
            GetOptions());

        gameObject.SetActive(false);
    }

    public void ExportSelectedObject()
    {
        if (Selectable.SelectedSelectables.Count == 0)
            return;

        if (Selectable.SelectedSelectables[0].TryGetArmAssemblyRoot(out GameObject obj))
        {
            DoExport(true, obj, GetOptions());
        }
        else
        {

            DoExport(true, Selectable.SelectedSelectables[0].gameObject, GetOptions());
        }

        gameObject.SetActive(false);
    }

    public static void DoExport(
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

                Debug.LogWarning($"Could not determine OBJ export category for {x.gameObject.name}");

                return false;
            })
            .SelectMany(x => x.GetComponentsInChildren<MeshRenderer>())
            .Where(x => FilterMeshRenderers(x, options))
            .ToList()
            .ConvertAll(x => x.gameObject.GetComponent<MeshFilter>())
            .ToArray();

        ObjExporter.DoExport(makeSubmeshes, meshFilters, "Scene");
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
        MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshRenderer>()
            .Where(x => FilterMeshRenderers(x, options))
            .ToList()
            .ConvertAll(item => item.gameObject.GetComponent<MeshFilter>())
            .ToArray();

        ObjExporter.DoExport(makeSubmeshes, meshFilters, obj.name);
    }

    private static void FinishAllLoadingTokens()
    {
        _loadingTokenOverall.Done();
        _loadingTokenCombiningMeshes.Done();
        _loadingTokenUpload.Done();
        _loadingTokenWaitForResponse.Done();
        _loadingTokenWriteObj.Done();
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
}