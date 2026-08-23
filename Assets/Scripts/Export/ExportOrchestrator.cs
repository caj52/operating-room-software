using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    private readonly List<string> _outputPaths = new();
    private bool _cancelled;
    private ExportScope _activeScope = ExportScope.Room;

    private bool _running;
    private ProposalPDFGenerator _activeProposal;

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
        if (request == null)
            return;

        EnsureInstance();
        if (Instance._running)
        {
            UI_DialogPrompt.Open(
                "An export is already running. Please wait for it to finish.",
                new ButtonAction("OK"));
            return;
        }

        // Every room/object export asks where files should go first.
        ExportPaths.PromptForExportFolderThen(() =>
            Instance.StartCoroutine(Instance.RunExportCoroutine(request)));
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
        _outputPaths.Clear();
        _cancelled = false;
        _activeProposal = null;

        if (UI_GeneralLoadingScreen.instance == null)
        {
            Debug.LogError("UI_GeneralLoadingScreen is missing — cannot run export.");
            UI_DialogPrompt.Open(
                "Export UI is missing from the scene.\nCannot show progress.",
                new ButtonAction("OK"));
            ExportPaths.ClearExportBaseOverride();
            yield break;
        }

        _running = true;
        ExportPaths.EnsureDirectories();
        SuppressIndividualDialogs = true;

        UI_GeneralLoadingScreen.instance.ShowLoadingScreen();
        UI_GeneralLoadingScreen.instance.OnCancel += OnCancelRequested;

        try
        {
            int stepCount = CountSteps(activeRequest);
            int stepIndex = 0;

            if (activeRequest.IncludeObj && !_cancelled)
            {
                UI_GeneralLoadingScreen.instance.SetStatus("Exporting 3D model (GLB + FBX)...");
                UI_GeneralLoadingScreen.instance.SetProgress((float)stepIndex / stepCount);
                yield return null;
                yield return ExportObj(activeRequest.ObjOptions);
                stepIndex++;
                yield return null;
            }

            if (activeRequest.IncludeElevations && !_cancelled)
            {
                if (activeRequest.Scope == ExportScope.Room && CountBoomAssemblies() == 0)
                {
                    // Soft-skip for Export All when the room has no booms.
                    stepIndex++;
                }
                else
                {
                    UI_GeneralLoadingScreen.instance.SetStatus("Exporting elevation sheets (PDF)...");
                    UI_GeneralLoadingScreen.instance.SetProgress((float)stepIndex / stepCount);
                    yield return null;
                    yield return ExportElevations(activeRequest);
                    stepIndex++;
                    yield return null;
                }
            }

            if (activeRequest.IncludeProposal && !_cancelled)
            {
                UI_GeneralLoadingScreen.instance.SetStatus("Exporting sales proposal (PDF)...");
                UI_GeneralLoadingScreen.instance.SetProgress((float)stepIndex / stepCount);
                yield return null;
                yield return ExportProposal();
                stepIndex++;
                yield return null;
            }

            if (activeRequest.IncludeSnapshots && !_cancelled)
            {
                UI_GeneralLoadingScreen.instance.SetStatus("Exporting presentation snapshots...");
                UI_GeneralLoadingScreen.instance.SetProgress((float)stepIndex / stepCount);
                yield return null;
                yield return ExportSnapshots();
            }

            if (!_cancelled)
            {
                UI_GeneralLoadingScreen.instance.SetProgress(1f);
                ShowCompletionDialog();
            }
            else
            {
                UI_DialogPrompt.Open("Export cancelled.", new ButtonAction("OK"));
            }
        }
        finally
        {
            if (UI_GeneralLoadingScreen.instance != null)
            {
                UI_GeneralLoadingScreen.instance.OnCancel -= OnCancelRequested;
                UI_GeneralLoadingScreen.instance.HideLoadingScreen();
            }
            SuppressIndividualDialogs = false;
            _activeProposal = null;
            _running = false;
            ExportPaths.ClearExportBaseOverride();
        }
    }

    private void OnCancelRequested()
    {
        // Only set the flag — do not StopAllCoroutines or the finally block never runs.
        _cancelled = true;
        _activeProposal?.RequestCancel();
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
        bool succeeded = false;
        string outputPath = null;
        UnityEngine.Events.UnityAction onFinished = () => finished = true;
        UnityEngine.Events.UnityAction<string> onSuccess = path =>
        {
            succeeded = true;
            outputPath = path;
        };
        ObjExporter.OnExportFinished.AddListener(onFinished);
        ObjExporter.ExportFinishedSuccessfully.AddListener(onSuccess);

        try
        {
            var opts = options ?? ObjExportOptions.CreateDefaults();

            bool started;
            if (activeRequestScopeIsSelection())
            {
                if (!ExportRequest.HasSelection())
                {
                    _failedSteps.Add("3D model (nothing selected)");
                    yield break;
                }

                var selected = Selectable.SelectedSelectables[0];
                if (selected.TryGetArmAssemblyRoot(out GameObject root))
                    started = UI_ObjExportOptions.TryDoExport(true, root, opts);
                else
                    started = UI_ObjExportOptions.TryDoExport(true, selected.gameObject, opts);
            }
            else
            {
                if (Selectable.ActiveSelectables == null || Selectable.ActiveSelectables.Count == 0)
                {
                    _failedSteps.Add("Room 3D model (nothing in the room to export)");
                    yield break;
                }

                started = UI_ObjExportOptions.TryDoExport(true, Selectable.ActiveSelectables, opts);
            }

            if (!started)
            {
                _failedSteps.Add(activeRequestScopeIsSelection()
                    ? "3D model (no meshes matched export filters)"
                    : "Room 3D model (no meshes matched export filters)");
                yield break;
            }

            yield return new WaitUntil(() => finished || _cancelled);

            // Keep SuppressIndividualDialogs until the in-flight 3D export finishes,
            // otherwise a late success dialog can pop after "Export cancelled".
            if (_cancelled && !finished)
                yield return new WaitUntil(() => finished);

            if (_cancelled)
                yield break;

            if (succeeded)
            {
                _completedSteps.Add(activeRequestScopeIsSelection()
                    ? "Selected 3D model (GLB + FBX)"
                    : "Room 3D model (GLB + FBX)");
                if (!string.IsNullOrWhiteSpace(outputPath))
                    _outputPaths.Add(outputPath);
            }
            else
            {
                _failedSteps.Add(activeRequestScopeIsSelection()
                    ? "3D model (export failed)"
                    : "Room 3D model (export failed)");
            }
        }
        finally
        {
            ObjExporter.OnExportFinished.RemoveListener(onFinished);
            ObjExporter.ExportFinishedSuccessfully.RemoveListener(onSuccess);
        }
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
            if (rootSelectable == null)
            {
                _failedSteps.Add("Elevation sheet (boom root has no Selectable)");
                yield break;
            }

            string objectTitle = ExportPaths.GetSelectableExportName(rootSelectable, title);

            yield return PdfBatchExporter.ExportSingleConfigToPdf(
                rootSelectable, objectTitle, subtitle, ExportPaths.ElevationsDir, suppressDialog: true,
                shouldCancel: () => _cancelled);
            if (_cancelled)
                yield break;
            if (PdfBatchExporter.LastSingleConfigExportOk)
            {
                _completedSteps.Add("Elevation sheet for selected boom (PDF)");
                RememberPdfOutput();
            }
            else
                _failedSteps.Add("Elevation sheet (capture produced no images)");
            yield break;
        }

        if (request.ElevationMode == ElevationExportMode.CombinedRoom)
        {
            int boomCount = CountBoomAssemblies();
            if (boomCount == 0)
            {
                _failedSteps.Add("Elevation sheets (no boom assemblies in the room)");
                yield break;
            }

            yield return PdfBatchExporter.ExportAllConfigsToMultipagePdf(
                ExportPaths.ElevationsDir, title, subtitle, suppressDialog: true,
                shouldCancel: () => _cancelled);
            if (_cancelled)
                yield break;
            if (PdfBatchExporter.LastMultipageExportOk)
            {
                _completedSteps.Add("Elevation sheets (combined PDF)");
                RememberPdfOutput();
            }
            else
                _failedSteps.Add("Elevation sheets (no pages could be generated)");
        }
        else
        {
            int boomCount = CountBoomAssemblies();
            if (boomCount == 0)
            {
                _failedSteps.Add("Elevation sheets (no boom assemblies in the room)");
                yield break;
            }

            yield return PdfBatchExporter.ExportPerAssemblyPdfs(
                ExportPaths.ElevationsDir, title, subtitle, suppressDialog: true,
                shouldCancel: () => _cancelled);
            if (_cancelled)
                yield break;
            if (PdfBatchExporter.LastPerAssemblyExportOk)
            {
                _completedSteps.Add("Elevation sheets (one PDF per boom)");
                RememberPdfOutput();
            }
            else
                _failedSteps.Add("Elevation sheets (no boom PDFs could be generated)");
        }
    }

    private void RememberPdfOutput()
    {
        if (!string.IsNullOrWhiteSpace(PdfBatchExporter.LastExportedPath))
            _outputPaths.Add(PdfBatchExporter.LastExportedPath);
    }

    private static int CountBoomAssemblies() => PdfBatchExporter.CollectBoomAssemblyRoots().Count;

    private IEnumerator ExportProposal()
    {
        // Elevations deactivate non-assembly selectables during capture. Restore before pricing scan.
        if (Selectable.ActiveSelectables != null)
        {
            foreach (var selectable in Selectable.ActiveSelectables)
            {
                if (selectable != null && selectable.gameObject != null && !selectable.gameObject.activeSelf)
                    selectable.gameObject.SetActive(true);
            }
        }

        // Safety net: recreate any SelectablePrice wiped by load races / domain reload.
        PricingManager.RebuildPricingFromTrackedObjects();
        yield return null;

        var generator = FindAnyObjectByType<ProposalPDFGenerator>(FindObjectsInactive.Include);
        if (generator == null)
        {
            _failedSteps.Add("Sales proposal (generator not found in scene)");
            yield break;
        }

        _activeProposal = generator;
        generator.ApplyExportDefaults();
        generator.SuppressCompletionDialog = true;

        bool finished = false;
        bool hadError = false;
        string error = null;
        string outputPath = null;

        generator.GeneratePDFWithCallback((success, path, errorMessage) =>
        {
            finished = true;
            hadError = !success;
            error = errorMessage;
            outputPath = path;
        });

        yield return new WaitUntil(() => finished || _cancelled);
        if (_cancelled && !finished)
        {
            generator.RequestCancel();
            yield return new WaitUntil(() => finished);
        }
        generator.SuppressCompletionDialog = false;
        _activeProposal = null;

        if (_cancelled)
            yield break;

        if (hadError)
            _failedSteps.Add($"Sales proposal ({error ?? "unknown error"})");
        else
        {
            _completedSteps.Add("Sales proposal (PDF)");
            if (!string.IsNullOrWhiteSpace(outputPath))
                _outputPaths.Add(outputPath);
        }
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
        if (_cancelled)
            yield break;

        if (capture.LastPresentationBatchOk)
        {
            _completedSteps.Add("Presentation snapshots");
            if (Directory.Exists(ExportPaths.SnapshotsDir))
                _outputPaths.Add(ExportPaths.SnapshotsDir);
        }
        else
            _failedSteps.Add("Snapshots (capture failed — check room walls/setup)");
    }

    private void ShowCompletionDialog()
    {
        string exportBase = ExportPaths.GetExportBasePath();
        string revealPath = ResolveRevealPath(exportBase);

        if (_failedSteps.Count > 0 && _completedSteps.Count == 0)
        {
            ExportFolderUtility.RevealInFileManager(revealPath);
            UI_DialogPrompt.Open(
                "Export failed:\n" + string.Join("\n", _failedSteps),
                new ButtonAction("OK"));
            return;
        }

        string summary = _failedSteps.Count > 0
            ? "Export finished with some issues.\n\nCompleted:\n"
              + string.Join("\n", _completedSteps)
              + "\n\nIssues:\n"
              + string.Join("\n", _failedSteps)
            : "Export finished.\n\n"
              + (_completedSteps.Count > 0
                  ? string.Join("\n", _completedSteps)
                  : "(No files were exported)");

        ExportFolderUtility.RevealInFileManager(revealPath);
        UI_DialogPrompt.Open(
            summary,
            new ButtonAction("Done"));
    }

    private string ResolveRevealPath(string exportBase)
    {
        // Prefer the most recently written concrete file.
        for (int i = _outputPaths.Count - 1; i >= 0; i--)
        {
            string p = _outputPaths[i];
            if (string.IsNullOrWhiteSpace(p))
                continue;
            if (File.Exists(p) || Directory.Exists(p))
                return p;
        }

        return exportBase;
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
