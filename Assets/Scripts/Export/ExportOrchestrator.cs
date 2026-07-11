using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs selected export deliverables with one progress screen and a clear completion summary.
/// </summary>
public class ExportOrchestrator : MonoBehaviour
{
    public static ExportOrchestrator Instance { get; private set; }
    public static bool SuppressIndividualDialogs { get; set; }

    private readonly List<string> _completedSteps = new();
    private readonly List<string> _failedSteps = new();
    private bool _cancelled;
    private ExportScope _activeScope = ExportScope.Room;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static void ExportDefaults()
    {
        Run(ExportRequest.CreateDefaults());
    }

    public static void Run(ExportRequest request)
    {
        EnsureInstance();
        Instance.StartCoroutine(Instance.RunExportCoroutine(request));
    }

    private static void EnsureInstance()
    {
        if (Instance != null)
            return;

        var go = new GameObject(nameof(ExportOrchestrator));
        go.AddComponent<ExportOrchestrator>();
    }

    private IEnumerator RunExportCoroutine(ExportRequest request)
    {
        var activeRequest = request ?? ExportRequest.CreateDefaults();
        _activeScope = activeRequest.Scope;
        _completedSteps.Clear();
        _failedSteps.Clear();
        _cancelled = false;

        ExportPaths.EnsureDirectories();
        SuppressIndividualDialogs = true;

        UI_GeneralLoadingScreen.instance.ShowLoadingScreen();
        UI_GeneralLoadingScreen.instance.OnCancel += OnCancelRequested;

        try
        {
            int stepCount = CountSteps(activeRequest);
            int stepIndex = 0;

            if (activeRequest.IncludeObj)
            {
                if (_cancelled) yield break;
                UI_GeneralLoadingScreen.instance.SetStatus("Exporting 3D model (OBJ)...");
                UI_GeneralLoadingScreen.instance.SetProgress((float)stepIndex / stepCount);
                yield return ExportObj(activeRequest.ObjOptions);
                if (_cancelled) yield break;
                stepIndex++;
            }

            if (activeRequest.IncludeElevations)
            {
                if (_cancelled) yield break;
                UI_GeneralLoadingScreen.instance.SetStatus("Exporting elevation sheets (PDF)...");
                UI_GeneralLoadingScreen.instance.SetProgress((float)stepIndex / stepCount);
                yield return ExportElevations(activeRequest);
                if (_cancelled) yield break;
                stepIndex++;
            }

            if (activeRequest.IncludeProposal)
            {
                if (_cancelled) yield break;
                UI_GeneralLoadingScreen.instance.SetStatus("Exporting sales proposal (PDF)...");
                UI_GeneralLoadingScreen.instance.SetProgress((float)stepIndex / stepCount);
                yield return ExportProposal();
                if (_cancelled) yield break;
                stepIndex++;
            }

            if (activeRequest.IncludeSnapshots)
            {
                if (_cancelled) yield break;
                UI_GeneralLoadingScreen.instance.SetStatus("Exporting presentation snapshots...");
                UI_GeneralLoadingScreen.instance.SetProgress((float)stepIndex / stepCount);
                yield return ExportSnapshots();
                if (_cancelled) yield break;
            }

            UI_GeneralLoadingScreen.instance.SetProgress(1f);
            ShowCompletionDialog();
        }
        finally
        {
            UI_GeneralLoadingScreen.instance.OnCancel -= OnCancelRequested;
            UI_GeneralLoadingScreen.instance.HideLoadingScreen();
            SuppressIndividualDialogs = false;
        }
    }

    private void OnCancelRequested()
    {
        // Only set the flag — do not StopAllCoroutines or the finally block never runs.
        _cancelled = true;
    }

    private static int CountSteps(ExportRequest request)
    {
        int count = 0;
        if (request.IncludeObj) count++;
        if (request.IncludeElevations) count++;
        if (request.IncludeProposal) count++;
        if (request.IncludeSnapshots) count++;
        return Math.Max(count, 1);
    }

    private IEnumerator ExportObj(ObjExportOptions options)
    {
        bool finished = false;
        UnityEngine.Events.UnityAction onFinished = () => finished = true;
        ObjExporter.OnExportFinished.AddListener(onFinished);

        var opts = options ?? ObjExportOptions.CreateDefaults();

        if (activeRequestScopeIsSelection())
        {
            var selected = Selectable.SelectedSelectables[0];
            if (selected.TryGetArmAssemblyRoot(out GameObject root))
                UI_ObjExportOptions.DoExport(true, root, opts);
            else
                UI_ObjExportOptions.DoExport(true, selected.gameObject, opts);
        }
        else
        {
            UI_ObjExportOptions.DoExport(true, Selectable.ActiveSelectables, opts);
        }

        yield return new WaitUntil(() => finished || _cancelled);
        ObjExporter.OnExportFinished.RemoveListener(onFinished);

        if (!_cancelled)
            _completedSteps.Add(activeRequestScopeIsSelection()
                ? "Selected 3D model (OBJ)"
                : "Room 3D model (OBJ)");
    }

    private bool activeRequestScopeIsSelection() => _activeScope == ExportScope.SelectedObject;

    private IEnumerator ExportElevations(ExportRequest request)
    {
        string title = ExportPaths.GetRoomExportName();
        string subtitle = SafeAccountName();

        if (request.Scope == ExportScope.SelectedObject)
        {
            if (!ExportRequest.HasSelection())
            {
                _failedSteps.Add("Elevation sheet (nothing selected)");
                yield break;
            }

            var selected = Selectable.SelectedSelectables[0];
            if (!selected.TryGetArmAssemblyRoot(out GameObject root))
            {
                _failedSteps.Add("Elevation sheet (selection is not a boom assembly)");
                yield break;
            }

            var rootSelectable = root.GetComponent<Selectable>();
            string objectTitle = string.IsNullOrWhiteSpace(rootSelectable.MetaData?.Name)
                ? title
                : rootSelectable.MetaData.Name;

            yield return PdfBatchExporter.ExportSingleConfigToPdf(
                rootSelectable, objectTitle, subtitle, ExportPaths.ElevationsDir, suppressDialog: true);
            if (!_cancelled)
                _completedSteps.Add("Elevation sheet for selected boom (PDF)");
            yield break;
        }

        if (request.ElevationMode == ElevationExportMode.CombinedRoom)
        {
            yield return PdfBatchExporter.ExportAllConfigsToMultipagePdf(
                ExportPaths.ElevationsDir, title, subtitle, suppressDialog: true);
            if (!_cancelled)
                _completedSteps.Add("Elevation sheets (combined PDF)");
        }
        else
        {
            yield return PdfBatchExporter.ExportPerAssemblyPdfs(
                ExportPaths.ElevationsDir, title, subtitle, suppressDialog: true);
            if (!_cancelled)
                _completedSteps.Add("Elevation sheets (one PDF per boom)");
        }
    }

    private IEnumerator ExportProposal()
    {
        var generator = FindAnyObjectByType<ProposalPDFGenerator>();
        if (generator == null)
        {
            _failedSteps.Add("Sales proposal (generator not found in scene)");
            yield break;
        }

        generator.ApplyExportDefaults();
        generator.SuppressCompletionDialog = true;

        bool finished = false;
        bool hadError = false;
        string error = null;

        generator.GeneratePDFWithCallback((success, path, errorMessage) =>
        {
            finished = true;
            hadError = !success;
            error = errorMessage;
        });

        yield return new WaitUntil(() => finished || _cancelled);
        generator.SuppressCompletionDialog = false;

        if (_cancelled)
            yield break;

        if (hadError)
            _failedSteps.Add($"Sales proposal ({error})");
        else
            _completedSteps.Add("Sales proposal (PDF)");
    }

    private IEnumerator ExportSnapshots()
    {
        var capture = FindAnyObjectByType<ScreenshotCapture>();
        if (capture == null)
        {
            _failedSteps.Add("Snapshots (capture component not found)");
            yield break;
        }

        yield return capture.ExportPresentationSnapshots(ExportPaths.SnapshotsDir);
        if (!_cancelled)
            _completedSteps.Add("Presentation snapshots");
    }

    private void ShowCompletionDialog()
    {
        if (_cancelled)
        {
            UI_DialogPrompt.Open("Export cancelled.", new ButtonAction("OK"));
            return;
        }

        string exportBase = ExportPaths.GetExportBasePath();

        if (_failedSteps.Count > 0 && _completedSteps.Count == 0)
        {
            UI_DialogPrompt.Open(
                "Export failed:\n" + string.Join("\n", _failedSteps)
                + $"\n\nFolder:\n{exportBase}",
                new ButtonAction("Open Folder", () => ExportFolderUtility.RevealInFileManager(exportBase)),
                new ButtonAction("OK"));
            return;
        }

        string summary = _failedSteps.Count > 0
            ? "Export finished with some issues.\n\nCompleted:\n"
              + string.Join("\n", _completedSteps)
              + "\n\nIssues:\n"
              + string.Join("\n", _failedSteps)
            : "Export finished.\n\n"
              + string.Join("\n", _completedSteps)
              + $"\n\nSaved to:\n{exportBase}";

        UI_DialogPrompt.Open(
            summary,
            new ButtonAction("Open Folder", () => ExportFolderUtility.RevealInFileManager(exportBase)),
            new ButtonAction("Done"));
    }

    private static string SafeAccountName()
    {
        try
        {
            string name = UI_ClientMetaData.AccountName;
            return name == "N/A" ? string.Empty : name;
        }
        catch
        {
            return string.Empty;
        }
    }
}
