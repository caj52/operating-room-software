using UnityEngine;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Font = iTextSharp.text.Font;
using iTextSharp.text.pdf.draw;
using System.Diagnostics;
using Debug = UnityEngine.Debug;


/// <summary>
/// Monolithic refactor of ProposalPDFGenerator with modularized methods and accurate itemized pricing
/// Enhanced with better UI messages and error handling while maintaining original functionality
/// </summary>
public class ProposalPDFGenerator : MonoBehaviour
{
    #region Properties and Fields

    // Company Details
    private readonly string companyName = "Imagine Unlimited";
    private readonly string companyAddress = "9155 Sterling St Suite 120";
    private readonly string companyCity = "Irving, TX 75063";
    private readonly string companyPhone = "Tel: 214.987.0404";



    // Client Details (public for inspector input)
    public string clientName = "Client Name";
    public string projectName = "Project Name";
    public string configName = "Tandem Equipment Boom with Light";
    public string salesRepName = "Sales Rep Name";
    public string salesRepPhone = "";
    public string salesRepEmail = "SalesRepEmail@igoimagine.com";

    public string accountName = "";
    public string accountAddress = "";
    public string projectNumber = "Project Name";
    public string referenceNumber = "";

    // Footer Notes
    public string note1 = "35% deposit. Progress billing to completion. Sales tax to be added to invoices in applicable";
    public string note2 = "Quote is valid for 90 days from creation date.";
    public string note3 = "Customer Acceptance and Configuration Acknowledgement.";

    public float discountPercentage;

    // Internal Refs
    private ScreenshotCapture screenshot;
    private string path = "";

    private SelectablePrice[] selectablePrices;
    private Selectable[] selectables;

    // Totals and names
    private string concatedStrBoomObjectsName;
    private string concatedStrNonBoomObjectsName;
    private string concatedStrNonBoomObjectsPartName;
    private double boomTotalPrice;
    private double nonBoomTotalPrice;
    private double boomTotalPriceDd;
    private double nonBoomTotalPriceDd;
    private int boomObjectsCount;
    private int nonBoomObjectsCount;

    // Fonts
    private Font titleFont, headerFont, normalFont, smallFont, boldFont, notesFont, tableHeaderFont, tableTitleFont;

    // Progress tracking
    private float currentProgress = 0f;
    private bool isCancelled = false;
    private Coroutine pdfGenerationCoroutine = null;
    public bool SuppressCompletionDialog { get; set; }
    private Action<bool, string, string> _completionCallback;

    /// <summary>Quiet generation into a temp PDF for the workspace page viewer.</summary>
    bool _previewMode;
    /// <summary>Reuse last ceiling / elevation stills so text/pricing edits stay fast.</summary>
    bool _reuseCachedVisuals;
    string _previewCeilingCachePath;
    readonly List<string> _previewElevationCachePaths = new();
    /// <summary>
    /// When true, elevation capture for the proposal preview uses a single view + JPEG.
    /// Export path stays at full quality (front+back PNG).
    /// </summary>
    public static bool FastPreviewCapture { get; private set; }
    /// <summary>True after scene layout/selectables change — next open needs fresh stills.</summary>
    public static bool PreviewVisualsStale { get; set; } = true;
    /// <summary>When set, export writes to this path instead of the default proposals folder.</summary>
    string _exportPathOverride;

    // Image cleanup tracking
    private List<string> tempImagePaths = new List<string>();

    #endregion

    private void Start()
    {
        screenshot = FindObjectOfType<ScreenshotCapture>();
        Selectable.ActiveSelectablesInSceneChanged.AddListener(MarkPreviewVisualsStale);
    }

    private void OnDestroy()
    {
        Selectable.ActiveSelectablesInSceneChanged.RemoveListener(MarkPreviewVisualsStale);
        StopActiveGeneration();
        FastPreviewCapture = false;
    }

    static void MarkPreviewVisualsStale() => PreviewVisualsStale = true;

    public void GeneratePDF()
    {
        GeneratePDF(null);
    }

    /// <summary>Export the sales proposal PDF, optionally to an explicit file path from a save dialog.</summary>
    public void GeneratePDF(string outputPathOverride)
    {
        StopActiveGeneration(notifyCallback: true);
        ApplyExportDefaults();

        isCancelled = false;
        _previewMode = false;
        _reuseCachedVisuals = false;
        SuppressCompletionDialog = false;
        FastPreviewCapture = false;
        _completionCallback = null;
        _exportPathOverride = string.IsNullOrWhiteSpace(outputPathOverride) ? null : outputPathOverride.Trim();
        pdfGenerationCoroutine = StartCoroutine(GeneratePDFCoroutine());
    }

    /// <summary>
    /// Build the same proposal PDF used for export, into a temp file for on-screen preview.
    /// When <paramref name="reuseVisuals"/> is true, skips ceiling/elevation re-capture
    /// if preview caches exist.
    /// </summary>
    public void GeneratePreviewPdf(Action<bool, string, string> callback, bool reuseVisuals = true)
    {
        StopActiveGeneration(notifyCallback: true);

        isCancelled = false;
        _previewMode = true;
        _reuseCachedVisuals = reuseVisuals;
        SuppressCompletionDialog = true;
        FastPreviewCapture = false;
        _completionCallback = callback;

        ApplyExportDefaults();
        var workspace = UI_ProposalWorkspace.Instance;
        if (workspace != null && workspace.PreviewModel != null)
            workspace.PreviewModel.ApplyTo(this);

        pdfGenerationCoroutine = StartCoroutine(GeneratePDFCoroutine());
    }

    /// <summary>Stop an in-flight preview/export without marking visuals as fresh.</summary>
    public void CancelPreview() => StopActiveGeneration(notifyCallback: false);

    /// <summary>
    /// Stops the running bake. Does not set PreviewVisualsStale=false — only a finished
    /// preview bake may do that. Optionally notifies the preview callback as cancelled.
    /// </summary>
    void StopActiveGeneration(bool notifyCallback = false)
    {
        isCancelled = true;

        if (pdfGenerationCoroutine != null)
        {
            StopCoroutine(pdfGenerationCoroutine);
            pdfGenerationCoroutine = null;
        }

        FastPreviewCapture = false;
        _previewMode = false;
        _reuseCachedVisuals = false;
        SuppressCompletionDialog = false;

        var cb = _completionCallback;
        _completionCallback = null;
        if (notifyCallback && cb != null)
            cb.Invoke(false, null, "cancelled");
    }

    public void ApplyExportDefaults()
    {
        // Job / client fields already live in Client Metadata (saved with the room).
        // Room equipment, accessories, prices, and images are read live during PDF generation.
        // Sales rep is the only Carlyn-noted manual field — persisted on this machine.
        // Notes / discount may also come from the proposal workspace preview model.
        try
        {
            string account = UI_ClientMetaData.AccountName;
            string project = UI_ClientMetaData.ProjectName;
            string projectNo = UI_ClientMetaData.ProjectNumber;
            string addr1 = UI_ClientMetaData.AccountAddressLine1;
            string addr2 = UI_ClientMetaData.AccountAddressLine2;
            string orderRef = UI_ClientMetaData.OrderReferenceNumber;

            clientName = account;
            accountName = account;
            projectName = project;
            projectNumber = projectNo;
            referenceNumber = orderRef;

            var addressParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(addr1))
                addressParts.Add(addr1.Trim());
            if (!string.IsNullOrWhiteSpace(addr2))
                addressParts.Add(addr2.Trim());
            accountAddress = addressParts.Count > 0 ? string.Join(", ", addressParts) : "";
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not apply client metadata defaults: {e.Message}");
            if (IsPlaceholder(clientName))
                clientName = "";
            if (IsPlaceholder(projectName))
                projectName = "";
            if (IsPlaceholder(projectNumber))
                projectNumber = "";
        }

        salesRepName = UI_ClientMetaData.SalesRepName ?? "";
        salesRepPhone = UI_ClientMetaData.SalesRepPhone ?? "";
        salesRepEmail = UI_ClientMetaData.SalesRepEmail ?? "";
        if (IsPlaceholder(salesRepName))
            salesRepName = "";
        if (IsPlaceholder(salesRepPhone))
            salesRepPhone = "";
        if (IsPlaceholder(salesRepEmail))
            salesRepEmail = "";

        // Configuration title: workspace override → saved room name → fallback.
        if (!string.IsNullOrWhiteSpace(ProposalPreviewModel.ConfigNameOverride))
            configName = ProposalPreviewModel.ConfigNameOverride;
        else if (ExportPaths.HasSavedRoomName())
            configName = ExportPaths.GetRoomExportName();
        else if (string.IsNullOrWhiteSpace(configName)
                 || configName.Equals("Tandem Equipment Boom with Light", StringComparison.OrdinalIgnoreCase))
            configName = "Configuration";

        // Prefer live workspace discount, then pricing UI, then last saved value.
        if (UI_ProposalWorkspace.LiveDiscountPercentage.HasValue)
        {
            discountPercentage = Mathf.Max(0f, UI_ProposalWorkspace.LiveDiscountPercentage.Value);
            SaveDiscountPercentage(discountPercentage);
        }
        else if (!TryPullDiscountFromPricingUi() && discountPercentage <= 0f)
            discountPercentage = GetSavedDiscountPercentage();

        note1 = PlayerPrefs.GetString(ProposalPreviewModel.PrefsNote1, note1);
        note2 = PlayerPrefs.GetString(ProposalPreviewModel.PrefsNote2, note2);
        note3 = PlayerPrefs.GetString(ProposalPreviewModel.PrefsNote3, note3);

        // Keep mock and PDF identical to the preview model (open workspace or warm cache).
        var workspaceModel = UI_ProposalWorkspace.Instance != null
            ? UI_ProposalWorkspace.Instance.PreviewModel
            : null;
        if (workspaceModel != null)
            workspaceModel.ApplyTo(this);
    }

    private bool TryPullDiscountFromPricingUi()
    {
        try
        {
            var ui = FindAnyObjectByType<PDFGeneratorUIHandler>();
            if (ui?.inputDiscountPercentage == null)
                return false;
            if (string.IsNullOrWhiteSpace(ui.inputDiscountPercentage.text))
                return false;
            if (!float.TryParse(ui.inputDiscountPercentage.text, out float discount) || discount < 0f)
                return false;

            discountPercentage = discount;
            SaveDiscountPercentage(discount);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private const string PrefsDiscount = "SalesProposal.DiscountPercentage";

    public static float GetSavedDiscountPercentage()
        => PlayerPrefs.GetFloat(PrefsDiscount, 0f);

    public static void SaveDiscountPercentage(float value)
    {
        PlayerPrefs.SetFloat(PrefsDiscount, Mathf.Max(0f, value));
        PlayerPrefs.Save();
    }

    private static bool IsPlaceholder(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        value = value.Trim();
        return value.Equals("Client Name", StringComparison.OrdinalIgnoreCase)
               || value.Equals("Project Name", StringComparison.OrdinalIgnoreCase)
               || value.Equals("Sales Rep Name", StringComparison.OrdinalIgnoreCase)
               || value.Equals("SalesRepEmail@igoimagine.com", StringComparison.OrdinalIgnoreCase)
               || value.Equals("N/A", StringComparison.OrdinalIgnoreCase);
    }

    public void GeneratePDFWithCallback(Action<bool, string, string> callback)
    {
        // Do not call GeneratePDF() — it clears _completionCallback. Keep the same abort + start
        // path, then attach the orchestrator/workspace completion handler.
        StopActiveGeneration(notifyCallback: true);
        ApplyExportDefaults();

        isCancelled = false;
        _previewMode = false;
        _reuseCachedVisuals = false;
        FastPreviewCapture = false;
        SuppressCompletionDialog = true;
        _completionCallback = callback;
        _exportPathOverride = null;
        pdfGenerationCoroutine = StartCoroutine(GeneratePDFCoroutine());
    }

    private IEnumerator GeneratePDFCoroutine()
    {
        if (!_previewMode)
            path = null;
        bool hasError = false;
        string errorMessage = "";
        string filePath = "";
        
        // Clear any previous temp image paths
        tempImagePaths.Clear();

        bool quietUi = _previewMode || ExportOrchestrator.SuppressIndividualDialogs;

        // Show loading screen with cancel option (skip when unified export / preview owns UX)
        if (!quietUi)
        {
            UI_GeneralLoadingScreen.instance.ShowLoadingScreen();
            UI_GeneralLoadingScreen.instance.OnCancel += HandleCancellation;
        }

        if (!quietUi)
        {
            UI_GeneralLoadingScreen.instance.SetStatus("Initializing PDF generation...");
            UI_GeneralLoadingScreen.instance.SetProgress(0f);
        }

        // Step 1: Capture screenshot (skip when preview can reuse the last ceiling still)
        bool canReuseCeiling = _previewMode && _reuseCachedVisuals
            && !string.IsNullOrEmpty(_previewCeilingCachePath)
            && File.Exists(_previewCeilingCachePath);

        if (canReuseCeiling)
        {
            path = _previewCeilingCachePath;
        }
        else
        {
            if (!quietUi)
            {
                UI_GeneralLoadingScreen.instance.SetStatus("Capturing ceiling view for visualization...");
                UI_GeneralLoadingScreen.instance.SetProgress(0.1f);
            }

            if (screenshot == null)
                screenshot = FindObjectOfType<ScreenshotCapture>();

            if (screenshot != null)
            {
                yield return StartCoroutine(
                    screenshot.CaptureCeilingOnly(
                        RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).transform.position + new Vector3(0, 10, 0),
                        null,
                        (resultPath) => path = resultPath
                    )
                );
            }

            if (isCancelled)
            {
                CleanupAndShowResult(isCancelled, hasError, errorMessage, filePath);
                yield break;
            }

            // Wait for screenshot with timeout
            float timeout = 5f;
            float elapsed = 0f;
            while (string.IsNullOrEmpty(path) && elapsed < timeout && !isCancelled)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                if (_previewMode)
                {
                    try
                    {
                        _previewCeilingCachePath = Path.Combine(Application.temporaryCachePath, "proposal_preview_ceiling.png");
                        File.Copy(path, _previewCeilingCachePath, overwrite: true);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Could not cache ceiling preview image: {e.Message}");
                        _previewCeilingCachePath = path;
                    }
                }

                if (!quietUi)
                {
                    UI_GeneralLoadingScreen.instance.SetStatus("Ceiling view captured successfully");
                    UI_GeneralLoadingScreen.instance.SetProgress(0.2f);
                }
            }
            else if (!isCancelled && !quietUi)
            {
                Debug.LogWarning("Screenshot capture failed or timed out, continuing without image");
                UI_GeneralLoadingScreen.instance.SetStatus("Continuing without ceiling view image");
            }
        }

        if (isCancelled)
        {
            CleanupAndShowResult(isCancelled, hasError, errorMessage, filePath);
            yield break;
        }

        // Step 2: Prepare pricing data
        if (!quietUi)
        {
            UI_GeneralLoadingScreen.instance.SetStatus("Collecting and organizing pricing data...");
            UI_GeneralLoadingScreen.instance.SetProgress(0.3f);
        }
        yield return null; // Allow UI to update

        // Prepare data with error handling
        try
        {
            PrepareQuoteData();
        }
        catch (Exception e)
        {
            hasError = true;
            errorMessage = $"Failed to prepare pricing data: {e.Message}";
            Debug.LogError($"PDF Generation Error: {errorMessage}\n{e.StackTrace}");
        }

        // Check if we have any exportable pricing (equipment, options, or install/ship charges).
        // Install/shipping alone is valid — the document workspace shows those without SelectablePrice.
        if (!hasError && !HasExportablePricing())
        {
            hasError = true;
            errorMessage = "No pricing data found. Please ensure you have configured at least one item.";
        }

        if (isCancelled || hasError)
        {
            CleanupAndShowResult(isCancelled, hasError, errorMessage, filePath);
            yield break;
        }

        int itemCount = selectablePrices?.Length ?? 0;
        if (!quietUi)
        {
            UI_GeneralLoadingScreen.instance.SetStatus(
                itemCount > 0
                    ? $"Successfully collected {itemCount} pricing items"
                    : "Building proposal from install/shipping and option charges…");
            UI_GeneralLoadingScreen.instance.SetProgress(0.4f);
        }

        // Step 3: Generate PDF
        if (_previewMode)
        {
            filePath = Path.Combine(Application.temporaryCachePath, "SalesProposal_WorkspacePreview.pdf");
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch { /* overwrite below */ }
        }
        else if (!string.IsNullOrWhiteSpace(_exportPathOverride))
        {
            filePath = _exportPathOverride;
            _exportPathOverride = null;
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        else
        {
            // Sanitize config name for safe filename
            string safeConfigName = string.IsNullOrWhiteSpace(configName)
                ? "Config"
                : string.Join("_", configName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            string salesProposalsPath = ExportPaths.ProposalsDir;

            if (!Directory.Exists(salesProposalsPath))
            {
                Directory.CreateDirectory(salesProposalsPath);
                Debug.Log($"Created SalesProposals folder at: {salesProposalsPath}");
            }

            string baseFileName = $"SalesProposal_{safeConfigName}";
            string fileName = GenerateUniqueFileName(salesProposalsPath, baseFileName, ".pdf");
            filePath = Path.Combine(salesProposalsPath, fileName);
        }

        if (!quietUi)
        {
            UI_GeneralLoadingScreen.instance.SetStatus("Creating PDF document structure...");
        }
        yield return null;

        if (isCancelled)
        {
            CleanupAndShowResult(isCancelled, hasError, errorMessage, filePath);
            yield break;
        }

        // Generate PDF document
        bool pdfGenerated = false;
        try
        {
            pdfGenerated = GeneratePDFDocument(filePath);
        }
        catch (Exception e)
        {
            hasError = true;
            errorMessage = $"Failed to generate PDF: {e.Message}";
            Debug.LogError($"PDF Generation Error: {errorMessage}\n{e.StackTrace}");
        }

        if (!hasError && pdfGenerated && !isCancelled && !quietUi)
        {
            UI_GeneralLoadingScreen.instance.SetStatus("PDF generation completed successfully!");
            UI_GeneralLoadingScreen.instance.SetProgress(1f);
            yield return new WaitForSeconds(0.5f); // Brief pause to show completion
        }

        // Clean up and show results
        CleanupAndShowResult(isCancelled, hasError, errorMessage, filePath);
    }

    private bool GeneratePDFDocument(string filePath)
    {
        Document document = null;
        PdfWriter writer = null;

        try
        {
            FastPreviewCapture = _previewMode;
            if (_previewMode && !_reuseCachedVisuals)
                ClearPreviewElevationCache();

            document = new Document(PageSize.A4, 18, 18, 18, 18);
            writer = PdfWriter.GetInstance(document, new FileStream(filePath, FileMode.Create));
            writer.PageEvent = new PageEventHelper
            {
                clientName = clientName,
                projectName = projectName,
                configName = configName
            };

            document.Open();
            SetupFonts();

            if (!isCancelled)
            {
                if (!_previewMode && UI_GeneralLoadingScreen.instance != null)
                {
                    UI_GeneralLoadingScreen.instance.SetStatus("Generating configuration summary page...");
                    UI_GeneralLoadingScreen.instance.SetProgress(0.5f);
                }
                GenerateFirstPage(document);
            }

            if (!isCancelled)
            {
                document.NewPage();
                if (!_previewMode && UI_GeneralLoadingScreen.instance != null)
                {
                    UI_GeneralLoadingScreen.instance.SetStatus("Adding visual representations...");
                    UI_GeneralLoadingScreen.instance.SetProgress(0.6f);
                }
                GenerateSecondPage(document, writer);
            }

            if (!isCancelled)
            {
                document.NewPage();
                if (!_previewMode && UI_GeneralLoadingScreen.instance != null)
                {
                    UI_GeneralLoadingScreen.instance.SetStatus("Creating detailed pricing breakdown...");
                    UI_GeneralLoadingScreen.instance.SetProgress(0.8f);
                }
                GeneratePricingPage(document, writer);
            }

            document.Close();
            // Finished preview bake → stills are current. Cancelled / stopped bakes never reach here.
            if (_previewMode && !isCancelled)
                PreviewVisualsStale = false;
            return !isCancelled;
        }
        catch (Exception e)
        {
            document?.Close();
            writer?.Close();
            throw;
        }
        finally
        {
            FastPreviewCapture = false;
        }
    }

    private void CleanupAndShowResult(bool cancelled, bool hasError, string errorMessage, string filePath)
    {
        CleanupTemporaryImages();

        if (UI_GeneralLoadingScreen.instance != null)
        {
            UI_GeneralLoadingScreen.instance.OnCancel -= HandleCancellation;
            if (!ExportOrchestrator.SuppressIndividualDialogs && !_previewMode)
                UI_GeneralLoadingScreen.instance.HideLoadingScreen();
        }
        pdfGenerationCoroutine = null;

        bool suppressDialog = SuppressCompletionDialog
                              || _previewMode
                              || ExportOrchestrator.SuppressIndividualDialogs;

        var callback = _completionCallback;
        _completionCallback = null;

        // Reset preview flags before the callback so the next bake starts clean.
        _previewMode = false;
        _reuseCachedVisuals = false;
        SuppressCompletionDialog = false;

        callback?.Invoke(!cancelled && !hasError, filePath, errorMessage);

        if (suppressDialog)
            return;

        // Show result dialog
        if (cancelled)
        {
            UI_DialogPrompt.Open(
                "PDF generation was cancelled.",
                new ButtonAction("OK")
            );
        }
        else if (hasError)
        {
            UI_DialogPrompt.Open(
                $"Unable to generate PDF:\n\n{errorMessage}\n\nPlease check your configuration and try again.",
                new ButtonAction("OK")
            );
        }
        else
        {
            // Open the containing folder automatically — no Copy Path needed.
            ExportFolderUtility.RevealInFileManager(filePath);
            UI_DialogPrompt.Open(
                "Sales proposal exported.",
                new ButtonAction("Done"));
        }
    }
    
    /// <summary>
    /// Cleans up all temporary image files created during PDF generation
    /// </summary>
    private void CleanupTemporaryImages()
    {
        // Delete ceiling screenshot if it exists — keep the stable preview cache file.
        if (!string.IsNullOrEmpty(path) && File.Exists(path)
            && !string.Equals(path, _previewCeilingCachePath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(path);
                Debug.Log($"Deleted ceiling screenshot: {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to delete ceiling screenshot {path}: {e.Message}");
            }
        }

        // Delete all elevation images — keep stable preview-cache copies.
        foreach (string imagePath in tempImagePaths)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                continue;
            if (_previewElevationCachePaths.Contains(imagePath))
                continue;
            try
            {
                File.Delete(imagePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to delete elevation image {imagePath}: {e.Message}");
            }
        }
        
        // Clear the tracking list
        tempImagePaths.Clear();
        if (!_previewMode)
            path = null;
        else if (!string.IsNullOrEmpty(_previewCeilingCachePath) && File.Exists(_previewCeilingCachePath))
            path = _previewCeilingCachePath;
    }


    private void HandleCancellation()
    {
        isCancelled = true;
        Debug.Log("PDF generation cancelled by user");
    }

    /// <summary>Used by ExportOrchestrator when Escape is pressed during a unified export.</summary>
    public void RequestCancel()
    {
        isCancelled = true;
    }

    private void OpenPDF(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                UI_DialogPrompt.Open("PDF file not found at the specified location.", new ButtonAction("OK"));
                return;
            }

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            // Hack fix for macOS not liking Application.OpenURL
            string location = filePath;
            ProcessStartInfo startInfo = new ProcessStartInfo("/System/Library/CoreServices/Finder.app")
            {
                WindowStyle = ProcessWindowStyle.Normal,
                FileName = location.Trim()
            };
            Process.Start(startInfo);
#else
            Application.OpenURL("file:///" + filePath);
#endif
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to open PDF: {e.Message}");
            ExportFolderUtility.RevealInFileManager(filePath);
            UI_DialogPrompt.Open(
                "Could not open the PDF. The export folder was opened instead.",
                new ButtonAction("OK"));
        }
    }

    private void SetupFonts()
    {
        BaseFont baseFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.EMBEDDED);
        titleFont = new Font(baseFont, 12, Font.BOLD);
        headerFont = new Font(baseFont, 10, Font.BOLD);
        normalFont = new Font(baseFont, 6, Font.NORMAL);
        smallFont = new Font(baseFont, 8, Font.NORMAL);
        boldFont = new Font(baseFont, 8, Font.BOLD);
        notesFont = new Font(baseFont, 8, Font.ITALIC);
        tableHeaderFont = new Font(baseFont, normalFont.Size, Font.NORMAL, BaseColor.WHITE);
        tableTitleFont = new Font(baseFont, normalFont.Size, Font.BOLD, new BaseColor(168, 168, 168));
    }

    private void PrepareQuoteData()
    {
        try
        {
            selectablePrices = FindObjectsOfType<SelectablePrice>() ?? Array.Empty<SelectablePrice>();
            selectables = new Selectable[selectablePrices.Length];

            for (int i = 0; i < selectablePrices.Length; i++)
            {
                selectables[i] = selectablePrices[i] != null
                    ? selectablePrices[i].transform.root.GetComponentInChildren<Selectable>()
                    : null;
            }

            // Group names by object size + name
            concatedStrBoomObjectsName = string.Join(", ", selectablePrices
                .Where(sp => sp != null && sp.isBoomObject && sp.objectPricingData != null)
                .OrderBy(sp => GetHierarchyPath(sp.transform))
                .Select(sp => string.IsNullOrEmpty(sp.objectPricingData.ObjectSize)
                    ? sp.objectPricingData.ObjectName
                    : $"{sp.objectPricingData.ObjectSize} {sp.objectPricingData.ObjectName}".Trim()));

            concatedStrNonBoomObjectsName = string.Join(", ", selectablePrices
                .Where(sp => sp != null && !sp.isBoomObject && sp.objectPricingData != null)
                .Select(sp => sp.objectPricingData.ObjectName));

            // Count
            boomObjectsCount = selectablePrices.Count(sp =>
                sp != null && sp.isBoomObject && UINameToExcelKey.IsBoomBaseModelFromExcel(sp.pricingObjectName));
            nonBoomObjectsCount = selectablePrices.Count(sp => sp != null && !sp.isBoomObject);

            // Pricing — same resolver as the proposal PDF (combo lights + bundled boom).
            var equipmentLines = ProposalPricingResolver.ResolveEquipmentLines(selectablePrices);
            boomTotalPrice = equipmentLines.Where(l => l.IsBoom).Sum(l => l.ExtPrice);
            nonBoomTotalPrice = equipmentLines.Where(l => l.IsLight).Sum(l => l.ExtPrice);

            (concatedStrBoomObjectsName, boomTotalPriceDd, concatedStrNonBoomObjectsName, nonBoomTotalPriceDd) = GetSelectedBoomAndNonBoomData();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error preparing quote data: {e.Message}");
            throw;
        }
    }

    /// <summary>
    /// True when export can produce a meaningful pricing page — equipment rows,
    /// priced option dropdowns, and/or install/shipping charges.
    /// </summary>
    private bool HasExportablePricing()
    {
        if (selectablePrices != null && selectablePrices.Any(sp => sp != null))
            return true;

        if (CalculateTotalPrice() > 0)
            return true;

        // Zero-dollar but still configured options (e.g. "None" filtered) — allow if Excel charges exist.
        var reader = FindObjectOfType<ExcelReader>();
        if (reader == null)
            return false;

        var install = reader.GetInstallationLightsCharges();
        if (install != null && install.ListPrice >= 0 && !string.IsNullOrWhiteSpace(install.ObjectName))
            return true;

        var ship = reader.GetShippingLightsCharges();
        if (ship != null && ship.ListPrice >= 0 && !string.IsNullOrWhiteSpace(ship.ObjectName))
            return true;

        return false;
    }


    public static (string boomObjects, double boomTotalPrice, string nonBoomObjects, double nonBoomTotalPrice)
        GetSelectedBoomAndNonBoomData()
    {
        // Get all selected dropdown states
        var selectedStates = DropdownPopulator.GetAllCurrentStates();

        // Filter and process boom objects
        var boomData = selectedStates
            .Where(state => state.Item1.isBoomExcelFileDropDown)
            .Select(state => state.Item2)
            .ToList();

        string boomObjects = string.Join(", ", boomData
            .Select(data => $"{data.ObjectName} {data.ObjectSize}".Trim()));

        double boomTotalPrice = boomData.Sum(data => data.ListPrice);

        // Filter and process non-boom objects
        var nonBoomData = selectedStates
            .Where(state => !state.Item1.isBoomExcelFileDropDown).Where(state => state.Item2.ObjectName != "None")
            .Select(state => state.Item2)
            .ToList();

        string nonBoomObjects = string.Join(", ", nonBoomData
            .Select(data => data.ObjectName));

        double nonBoomTotalPrice = nonBoomData.Sum(data => data.ListPrice);

        return (boomObjects, boomTotalPrice, nonBoomObjects, nonBoomTotalPrice);
    }

    /// <summary>Shared with proposal workspace preview (page-1 boom option text).</summary>
    public static Dictionary<string, List<string>> GetBtNamesGroupedByType(List<SelectablePrice> selectables)
    {
        var result = new Dictionary<string, List<string>>
        {
            { "Light", new List<string>() },
            { "Boom", new List<string>() }
        };

        // Sort selectables by hierarchy order first
        var sortedSelectables = selectables
            .Where(s => s != null && !s.Equals(null) && s.gameObject != null && !s.gameObject.Equals(null))
            .OrderBy(s => GetHierarchyPath(s.transform))
            .ToList();

        foreach (var a in sortedSelectables)
        {
            GameObject go = a.gameObject;
            string btName = a.pricingObjectName;
            var selectable = go.GetComponent<Selectable>();
            string sizeStr = "";

            if (selectable != null && !selectable.Equals(null))
            {
                float size = selectable.CurrentPreviewScaleLevel?.Size ?? 0f;
                if (size > 0)
                {
                    sizeStr = $" ({size * 1000}mm)";
                }
                else
                {
                    foreach (var related in selectable.RelatedSelectables)
                    {
                        var selectedScale = related.ScaleLevels?.FirstOrDefault(s => s.Selected);
                        if (selectedScale != null)
                        {
                            sizeStr = $" ({selectedScale.Size * 1000}mm)";
                            break;
                        }
                    }
                }
            }

            string fullName = btName + sizeStr;

            // Classify by keyword
            if (btName.ToLower().Contains("light") || btName.ToLower().Contains("spring arm"))
                result["Light"].Add(fullName);
            else
                result["Boom"].Add(fullName);
        }

        return result;
    }

    public static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
    #region PDF Page Generation

    private void GenerateFirstPage(Document document)
    {
        AddCompanyHeader(document);

        var rootGroups = (selectablePrices ?? Array.Empty<SelectablePrice>())
            .Where(sp => sp != null && sp.objectPricingData != null)
            .GroupBy(sp => sp.transform.root)
            .ToList();

        if (rootGroups.Count == 0)
        {
            // Still emit a summary page when only install/shipping/options are priced.
            document.Add(Chunk.NEWLINE);
            AddTableTitle(document, string.IsNullOrWhiteSpace(configName) ? "Configuration" : configName);
            AddSectionHeader(document, "OPTION/ACCESSORY DESCRIPTION");
            string lightOpts = BuildOptionsDescriptionString(new List<SelectablePrice>(), false);
            string boomOpts = BuildOptionsDescriptionString(new List<SelectablePrice>(), true);
            string combined = string.Join(", ", new[] { lightOpts, boomOpts }.Where(s => !string.IsNullOrWhiteSpace(s)));
            document.Add(CreateOptionTable(string.IsNullOrWhiteSpace(combined) ? "No options selected" : combined));
            document.Add(Chunk.NEWLINE);
            // Page 1 equipment total is selectable equipment only (options live as text; $ on page 3).
            document.Add(CreateTotalTable("EQUIPMENT TOTAL LIST PRICE", "$0.00"));
            return;
        }

        int configCount = 1;

        foreach (var group in rootGroups)
        {
            var first = group.Last();
            var lights = group.Where(sp => !sp.isBoomObject).ToList();
            var booms = group.Where(sp => sp.isBoomObject).ToList();


            document.Add(Chunk.NEWLINE);
            AddTableTitle(document, configName);

            // Page 1 model rows must match page 3 / ProposalPricingResolver (not raw light counts).
            var lightLines = ProposalPricingResolver.ResolveLightLines(lights);
            if (lightLines.Count > 0)
            {
                foreach (var line in lightLines)
                {
                    string desc = string.IsNullOrWhiteSpace(line.Description) ? "LIGHT" : line.Description;
                    document.Add(CreateModelTable(
                        "MODEL DESCRIPTION", "QTY", desc, Math.Max(1, line.Qty).ToString()));
                    AddHorizontalLine(document);
                }

                AddSectionHeader(document, "OPTION/ACCESSORY DESCRIPTION");
                string lightOptionsText = BuildOptionsDescriptionString(lights, false);
                document.Add(CreateOptionTable(lightOptionsText));
                document.Add(Chunk.NEWLINE);
            }

            var boomLine = ProposalPricingResolver.ResolveBoomLine(
                group.Key != null ? group.Key.gameObject : null, booms);
            if (boomLine != null)
            {
                string boomDesc = string.IsNullOrWhiteSpace(boomLine.Description)
                    ? "ARTICULATING BOOM"
                    : boomLine.Description;
                document.Add(CreateModelTable(
                    "MODEL DESCRIPTION", "QTY", boomDesc, Math.Max(1, boomLine.Qty).ToString()));
                AddHorizontalLine(document);

                AddSectionHeader(document, "OPTION/ACCESSORY DESCRIPTION");
                string boomOptionsText = BuildOptionsDescriptionString(booms, true);
                document.Add(CreateOptionTable(boomOptionsText));
                AddHorizontalLine(document);
            }

            // Equipment-only total (options + install/ship appear with $ on page 3).
            if (lightLines.Count > 0 || boomLine != null)
            {
                double configTotal = ProposalPricingResolver.SumEquipment(group);
                document.Add(CreateTotalTable("EQUIPMENT TOTAL LIST PRICE", configTotal.ToString("C")));
                document.Add(Chunk.NEWLINE);
            }
        }
    }


    private void GenerateSecondPage(Document document, PdfWriter pdfWriter)
    {
        AddCompanyHeader(document);

        AddTableTitle(document, configName);
        HashSet<Selectable> processedRoots = new HashSet<Selectable>();
        float availableHeight = document.PageSize.Height - document.TopMargin - document.BottomMargin - 125f;
        float imageHeight = availableHeight / 3f;

        var list = selectables ?? Array.Empty<Selectable>();
        if (list.Length == 0)
        {
            // No assemblies — still include the ceiling capture when available.
            AddImageToPDF(document, path, pdfWriter, imageHeight);
            return;
        }

        bool reuseElevations = _previewMode && _reuseCachedVisuals
                               && _previewElevationCachePaths.Count > 0
                               && _previewElevationCachePaths.TrueForAll(p => !string.IsNullOrEmpty(p) && File.Exists(p));
        int elevationCacheIndex = 0;
        var freshElevationCache = new List<string>();

        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == null) continue;

            Selectable root = GetRootParent(list[i]);
            if (root == null || !processedRoots.Add(root)) continue;

            string elevationPath = null;
            if (reuseElevations && elevationCacheIndex < _previewElevationCachePaths.Count)
            {
                elevationPath = _previewElevationCachePaths[elevationCacheIndex++];
            }
            else
            {
                List<PdfExporter.PdfImageData> imageData = list[i].ExportElevationPdf();
                if (imageData != null && imageData.Count > 0 && !string.IsNullOrEmpty(imageData[0].Path))
                {
                    elevationPath = StabilizePreviewElevation(imageData[0].Path, freshElevationCache.Count);
                    foreach (var imgData in imageData)
                    {
                        if (!string.IsNullOrEmpty(imgData.Path))
                            tempImagePaths.Add(imgData.Path);
                    }
                }
            }

            if (processedRoots.Count > 1)
            {
                document.NewPage();
                AddCompanyHeader(document);
            }

            // Add ceiling image
            AddImageToPDF(document, path, pdfWriter, imageHeight);

            if (!string.IsNullOrEmpty(elevationPath))
            {
                AddImageToPDF(document, elevationPath, pdfWriter, imageHeight);
                if (_previewMode)
                    freshElevationCache.Add(elevationPath);
            }
        }

        if (_previewMode && !reuseElevations && freshElevationCache.Count > 0)
        {
            // Cache was cleared at bake start when visuals were forced; just remember paths.
            _previewElevationCachePaths.Clear();
            _previewElevationCachePaths.AddRange(freshElevationCache);
        }
    }

    string StabilizePreviewElevation(string sourcePath, int index)
    {
        if (!_previewMode || string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            return sourcePath;

        try
        {
            string ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrEmpty(ext))
                ext = ".jpg";
            string dest = Path.Combine(
                Application.temporaryCachePath,
                $"proposal_preview_elev_{index}{ext}");
            File.Copy(sourcePath, dest, overwrite: true);
            return dest;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not cache preview elevation: {e.Message}");
            return sourcePath;
        }
    }

    void ClearPreviewElevationCache()
    {
        for (int i = 0; i < _previewElevationCachePaths.Count; i++)
        {
            string p = _previewElevationCachePaths[i];
            if (string.IsNullOrEmpty(p) || !File.Exists(p))
                continue;
            try { File.Delete(p); }
            catch { /* ignore */ }
        }
        _previewElevationCachePaths.Clear();
    }

    private void GeneratePricingPage(Document document, PdfWriter writer)
    {
        AddCompanyHeader(document);

        // 1) Group by root-level configuration
        var rootConfigs = (selectablePrices ?? Array.Empty<SelectablePrice>())
            .Where(sp => sp != null && sp.objectPricingData != null)
            .GroupBy(sp => sp.transform.root)
            .ToList();

        int configNumber = 1;

        // Options are room-global — emit once on the first config table only.
        // (Previously they were repeated for every root group.)
        bool optionsEmitted = false;

        foreach (var configGroup in rootConfigs)
        {
            var firstSp = configGroup.FirstOrDefault();  
            AddTableTitle(document, configName);

            PdfPTable table = new PdfPTable(5);
            table.WidthPercentage = 100;
            table.SetWidths(new float[] { 2, 5, 1, 2, 2 });
            table.SpacingBefore = 5f; // Add space before table
            table.SpacingAfter = 5f;  // Add space after table

            double subtotal = 0;
            bool emitOptions = !optionsEmitted;

            var lights = configGroup.Where(sp => !sp.isBoomObject).ToList();
            var booms = configGroup.Where(sp => sp.isBoomObject).ToList();
            var lightLines = ProposalPricingResolver.ResolveLightLines(lights);
            var boomLine = ProposalPricingResolver.ResolveBoomLine(
                firstSp != null ? firstSp.transform.root.gameObject : null, booms);

            // ---------- LIGHT MODELS (combo sheet, same as pricing UI) ----------
            if (lightLines.Count > 0)
            {
                AddRowToTable(table, "MODEL DESCRIPTION", BaseColor.WHITE, BaseColor.BLACK, PdfPCell.NO_BORDER);

                foreach (var line in lightLines)
                {
                    table.AddCell(CreateLeftAlignedCell(line.PartNumber ?? "N/A", normalFont));
                    table.AddCell(CreateLeftAlignedCell(line.Description ?? "", normalFont));
                    table.AddCell(CreateCenteredCell(line.Qty.ToString(), normalFont));
                    table.AddCell(CreateRightAlignedCell(line.UnitPrice.ToString("C"), normalFont));
                    table.AddCell(CreateRightAlignedCell(line.ExtPrice.ToString("C"), normalFont));
                    subtotal += line.ExtPrice;
                }

                for (int i = 0; i < 5; i++)
                {
                    PdfPCell spacerCell = new PdfPCell(new Phrase(" ", normalFont))
                    {
                        Border = Rectangle.NO_BORDER,
                        FixedHeight = 3f
                    };
                    table.AddCell(spacerCell);
                }
            }

            // ---------- LIGHT OPTIONS (priced line-items) ----------
            // Pull dropdown items that are NON-BOOM, skip "None"/zero — once per proposal.
            var lightOptionData = emitOptions
                ? DropdownPopulator.GetAllCurrentStates()
                    .Where(s => s.Item1 != null && s.Item2 != null && !s.Item1.isBoomExcelFileDropDown)
                    .Select(s => s.Item2)
                    .Where(d => d != null &&
                                d.ListPrice > 0 &&
                                !string.Equals(d.ObjectName, "None", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : new List<PriceExcelData>();

            if (lightOptionData.Count > 0)
            {
                optionsEmitted = true;
                AddRowToTable(table, "OPTION/ACCESSORY DESCRIPTION", BaseColor.BLACK,new BaseColor(180,180,180), PdfPCell.NO_BORDER);

                foreach (var d in lightOptionData)
                {
                    AddRowToTable(table, d); // uses existing helper (qty=1, so EXT = LIST * 1 = LIST)
                    subtotal += d.ListPrice;
                }
                
                // Add a small spacer row after light options
                for (int i = 0; i < 5; i++)
                {
                    PdfPCell spacerCell = new PdfPCell(new Phrase(" ", normalFont))
                    {
                        Border = Rectangle.NO_BORDER,
                        FixedHeight = 3f
                    };
                    table.AddCell(spacerCell);
                }
            }

            // ---------- BOOM MODELS (bundled 3D config sheet) ----------
            if (boomLine != null)
            {
                AddRowToTable(table, "MODEL DESCRIPTION", BaseColor.WHITE, BaseColor.BLACK, PdfPCell.NO_BORDER);

                table.AddCell(CreateLeftAlignedCell(boomLine.PartNumber ?? "N/A", normalFont));
                table.AddCell(CreateLeftAlignedCell(boomLine.Description ?? "", normalFont));
                table.AddCell(CreateCenteredCell(boomLine.Qty.ToString(), normalFont));
                table.AddCell(CreateRightAlignedCell(boomLine.UnitPrice.ToString("C"), normalFont));
                table.AddCell(CreateRightAlignedCell(boomLine.ExtPrice.ToString("C"), normalFont));
                subtotal += boomLine.ExtPrice;

                for (int i = 0; i < 5; i++)
                {
                    PdfPCell spacerCell = new PdfPCell(new Phrase(" ", normalFont))
                    {
                        Border = Rectangle.NO_BORDER,
                        FixedHeight = 3f
                    };
                    table.AddCell(spacerCell);
                }
            }

            // ---------- BOOM OPTIONS (priced line-items) ----------
            var boomOptionData = emitOptions
                ? DropdownPopulator.GetAllCurrentStates()
                    .Where(s => s.Item1 != null && s.Item2 != null && s.Item1.isBoomExcelFileDropDown)
                    .Select(s => s.Item2)
                    .Where(d => d != null &&
                                d.ListPrice > 0 &&
                                !string.Equals(d.ObjectName, "None", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : new List<PriceExcelData>();

            if (boomOptionData.Count > 0)
            {
                optionsEmitted = true;
                AddRowToTable(table, "OPTION/ACCESSORY DESCRIPTION", BaseColor.BLACK, new BaseColor(180,180,180), PdfPCell.NO_BORDER);

                foreach (var d in boomOptionData)
                {
                    AddRowToTable(table, d); // uses existing helper (qty=1, so EXT = LIST * 1 = LIST)
                    subtotal += d.ListPrice;
                }
            }

            // ---------- SUBTOTAL ----------
            // Only add subtotal if we have any content in the table
            if (lightLines.Count > 0 || lightOptionData.Count > 0 || boomLine != null || boomOptionData.Count > 0)
            {
                // Add a spacer row before subtotal
                for (int i = 0; i < 5; i++)
                {
                    PdfPCell spacerCell = new PdfPCell(new Phrase(" ", normalFont))
                    {
                        Border = Rectangle.NO_BORDER,
                        FixedHeight = 5f
                    };
                    table.AddCell(spacerCell);
                }
                
                PdfPCell subtotalLabel = new PdfPCell(new Phrase("Subtotal", boldFont))
                {
                    Colspan = 4,
                    Border = Rectangle.TOP_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT,
                    PaddingTop = 5
                };
                PdfPCell subtotalValue = CreateRightAlignedCell(subtotal.ToString("C"), boldFont);
                subtotalValue.Border = Rectangle.TOP_BORDER;

                table.AddCell(subtotalLabel);
                table.AddCell(subtotalValue);

                document.Add(table);
                document.Add(Chunk.NEWLINE);
            }
        }

        // No boom/light selectables — still print priced options once (then install/ship).
        if (!optionsEmitted)
        {
            PdfPTable optTable = new PdfPTable(5);
            optTable.WidthPercentage = 100;
            optTable.SetWidths(new float[] { 2, 5, 1, 2, 2 });
            double optSub = 0;

            var lightOptionData = DropdownPopulator.GetAllCurrentStates()
                .Where(s => s.Item1 != null && s.Item2 != null && !s.Item1.isBoomExcelFileDropDown)
                .Select(s => s.Item2)
                .Where(d => d != null && d.ListPrice > 0 &&
                            !string.Equals(d.ObjectName, "None", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (lightOptionData.Count > 0)
            {
                AddRowToTable(optTable, "OPTION/ACCESSORY DESCRIPTION", BaseColor.BLACK, new BaseColor(180, 180, 180), PdfPCell.NO_BORDER);
                foreach (var d in lightOptionData)
                {
                    AddRowToTable(optTable, d);
                    optSub += d.ListPrice;
                }
            }

            var boomOptionData = DropdownPopulator.GetAllCurrentStates()
                .Where(s => s.Item1 != null && s.Item2 != null && s.Item1.isBoomExcelFileDropDown)
                .Select(s => s.Item2)
                .Where(d => d != null && d.ListPrice > 0 &&
                            !string.Equals(d.ObjectName, "None", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (boomOptionData.Count > 0)
            {
                AddRowToTable(optTable, "OPTION/ACCESSORY DESCRIPTION", BaseColor.BLACK, new BaseColor(180, 180, 180), PdfPCell.NO_BORDER);
                foreach (var d in boomOptionData)
                {
                    AddRowToTable(optTable, d);
                    optSub += d.ListPrice;
                }
            }

            if (optSub > 0)
            {
                PdfPCell subtotalLabel = new PdfPCell(new Phrase("Subtotal", boldFont))
                {
                    Colspan = 4,
                    Border = Rectangle.TOP_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT,
                    PaddingTop = 5
                };
                PdfPCell subtotalValue = CreateRightAlignedCell(optSub.ToString("C"), boldFont);
                subtotalValue.Border = Rectangle.TOP_BORDER;
                optTable.AddCell(subtotalLabel);
                optTable.AddCell(subtotalValue);
                document.Add(optTable);
                document.Add(Chunk.NEWLINE);
            }
        }

        // Add common charges and grand totals once at the end
        PdfPTable misc = new PdfPTable(5);
        misc.WidthPercentage = 100;
        misc.SetWidths(new float[] { 2, 5, 1, 2, 2 });
        misc.SpacingBefore = 5f;
        misc.SpacingAfter = 5f;

        var reader = FindObjectOfType<ExcelReader>();
        if (reader != null)
        {
            var install = reader.GetInstallationLightsCharges();
            if (install != null) AddRowToTable(misc, install);

            var ship = reader.GetShippingLightsCharges();
            if (ship != null) AddRowToTable(misc, ship);
        }

        if (misc.Rows.Count > 0)
            document.Add(misc);

        AddTotalsTable(document);
        document.Add(Chunk.NEWLINE);
        EnsureNotesAndAcceptanceFit(document, writer);
    }

    #endregion
    #region Table & Footer Helpers


    private void EnsureNotesAndAcceptanceFit(Document document, PdfWriter writer)
    {
        // Estimate height in points (rough manual estimate)
        float estimatedNotesHeight = 40f;
        float estimatedAcceptanceHeight = 120f;
        float totalEstimatedHeight = estimatedNotesHeight + estimatedAcceptanceHeight;

        // Current vertical position
        float spaceLeft = writer.GetVerticalPosition(true) - document.BottomMargin;

        if (spaceLeft < totalEstimatedHeight)
        {
            document.NewPage();
        }

        // Then add content
        AddNotes(document);
        document.Add(Chunk.NEWLINE);
        AddAcceptanceTable(document);
    }

    private void AddCompanyHeader(Document document)
    {
        PdfPTable headerTable = new PdfPTable(2);
        headerTable.WidthPercentage = 100;
        headerTable.SetWidths(new float[] { 70, 30 });

        // Left
        PdfPCell leftCell = new PdfPCell { Border = Rectangle.NO_BORDER };
        leftCell.AddElement(new Paragraph("Proposal", titleFont));
        leftCell.AddElement(new Paragraph("\n", normalFont));
        if (!string.IsNullOrWhiteSpace(salesRepName))
            leftCell.AddElement(new Paragraph(salesRepName, titleFont));
        if (!string.IsNullOrWhiteSpace(salesRepPhone))
            leftCell.AddElement(new Paragraph(salesRepPhone, normalFont));
        if (!string.IsNullOrWhiteSpace(salesRepEmail))
            leftCell.AddElement(new Paragraph(salesRepEmail, normalFont));
        leftCell.AddElement(new Paragraph("\n", normalFont));
        leftCell.AddElement(new Paragraph(companyName, normalFont));
        leftCell.AddElement(new Paragraph(companyAddress, normalFont));
        leftCell.AddElement(new Paragraph(companyCity, normalFont));
        leftCell.AddElement(new Paragraph(companyPhone, normalFont));
        leftCell.AddElement(new Paragraph("\n", normalFont));

        headerTable.AddCell(leftCell);

        // Right
        PdfPCell rightCell = new PdfPCell { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT };
        try
        {
            string logoPath = Path.Combine(Application.streamingAssetsPath, "Data/quotes/IMAGINE-UNLIMITED_FullLogo_orange.png");
            if (File.Exists(logoPath))
            {
                Image logo = Image.GetInstance(logoPath);
                logo.ScaleAbsolute(150f, 75f);
                rightCell.AddElement(logo);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to load logo: {e.Message}");
        }
        headerTable.AddCell(rightCell);

        document.Add(headerTable);
        document.Add(Chunk.NEWLINE);

        PdfPTable clientTable = new PdfPTable(1);
        clientTable.WidthPercentage = 100;
        clientTable.AddCell(new PdfPCell(new Phrase($"Submitted To: {clientName}", boldFont)) { BackgroundColor = BaseColor.LIGHT_GRAY, Border = Rectangle.NO_BORDER, Padding = 5 });
        document.Add(clientTable);
        document.Add(new Paragraph("\n", normalFont));
        document.Add(new Paragraph($"Project: {projectName}", new Font(titleFont.BaseFont, titleFont.Size, Font.UNDERLINE)));
        document.Add(new Paragraph("\n", normalFont));
    }

    private void AddTableTitle(Document doc, string text)
    {
        Paragraph tableTitle = new Paragraph("\t" + text, headerFont);
        tableTitle.Alignment = Element.ALIGN_LEFT;
        tableTitle.SpacingAfter = 10;
        doc.Add(tableTitle);
    }

    private PdfPTable CreateModelTable(string col1, string col2, string rowLabel, string qty)
    {
        PdfPTable table = new PdfPTable(2);
        table.WidthPercentage = 100;
        table.SetWidths(new float[] { 8, 1 });

        AddHeaderRow(table, col1, col2);
        table.AddCell(CreateLeftAlignedCell(rowLabel, normalFont));
        table.AddCell(CreateCenteredCell(qty, normalFont));

        return table;
    }


    private PdfPTable CreateOptionTable(string content)
    {
        PdfPTable table = new PdfPTable(2);
        table.WidthPercentage = 100;
        table.SetWidths(new float[] { 8, 1 });

        table.AddCell(CreateLeftAlignedCell(content, normalFont));
        table.AddCell(CreateCenteredCell("", normalFont));

        return table;
    }

    // ADDED: Build OPTION/ACCESSORY text like the old script
    private string BuildOptionsDescriptionString(IEnumerable<SelectablePrice> group, bool isBoom)
    {
        // 1) Filter & order by hierarchy so output follows scene order
        var filtered = group
            .Where(sp => sp != null && sp.objectPricingData != null && sp.isBoomObject == isBoom)
            .OrderBy(sp => GetHierarchyPath(sp.transform))  // maintain hierarchy
            .ToList();

        // 2) Base model names (already in hierarchical order)
        IEnumerable<string> baseNamesSeq = filtered.Select(sp =>
            string.IsNullOrEmpty(sp.objectPricingData.ObjectSize)
                ? sp.objectPricingData.ObjectName
                : $"{sp.objectPricingData.ObjectSize} {sp.objectPricingData.ObjectName}".Trim());

        // 3) Dropdown selections (order as provided by UI list)
        var selectedStates = DropdownPopulator.GetAllCurrentStates();
        IEnumerable<string> ddValues = selectedStates
            .Where(s => s.Item1 != null && s.Item2 != null && s.Item1.isBoomExcelFileDropDown == isBoom)
            .Select(s => s.Item2.ObjectName)
            .Where(name => !string.IsNullOrWhiteSpace(name)
                           && !name.Trim().Equals("None", StringComparison.OrdinalIgnoreCase));

        // 4) btName parts – for BOOM only, and already hierarchy-sorted by helper
        IEnumerable<string> btParts = Enumerable.Empty<string>();
        if (isBoom)
        {
            var btGrouped = GetBtNamesGroupedByType(filtered); // this sorts by hierarchy internally
            if (btGrouped.TryGetValue("Boom", out var boomList) && boomList != null)
                btParts = boomList;
        }
        // (Lights stay as before: no btParts appended)

        // 5) Combine in this order: Base Models -> Dropdown Picks -> (Boom) btParts
        var combined = baseNamesSeq
            .Concat(ddValues)
            .Concat(btParts);

        // 6) Deduplicate while preserving the first occurrence order
        var final = string.Join(", ",
            combined
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct());

        return final;
    }

    private PdfPTable CreateTotalTable(string label, string value)
    {
        PdfPTable table = new PdfPTable(2);
        table.WidthPercentage = 100;
        table.SetWidths(new float[] { 8, 2 });

        table.AddCell(CreateLeftAlignedCell(label, boldFont));
        table.AddCell(CreateRightAlignedCell(value, boldFont));

        return table;
    }

    private void AddSectionHeader(Document doc, string title)
    {
        PdfPTable table = new PdfPTable(1);
        table.WidthPercentage = 100;
        PdfPCell cell = new PdfPCell(new Phrase(title, boldFont)) { BackgroundColor = BaseColor.LIGHT_GRAY, Border = Rectangle.NO_BORDER, Padding = 5 };
        table.AddCell(cell);
        doc.Add(table);
    }

    private void AddHorizontalLine(Document doc)
    {
        LineSeparator line = new LineSeparator(1f, 100f, BaseColor.BLACK, Element.ALIGN_CENTER, 10);
        doc.Add(new Chunk(line));
    }

    private void AddHeaderRow(PdfPTable table, string left, string right)
    {
        table.AddCell(new PdfPCell(new Phrase(left, tableHeaderFont)) { BackgroundColor = BaseColor.BLACK, Border = Rectangle.NO_BORDER, Padding = 5 });
        table.AddCell(new PdfPCell(new Phrase(right, tableHeaderFont)) { BackgroundColor = BaseColor.BLACK, Border = Rectangle.NO_BORDER, Padding = 5, HorizontalAlignment = Element.ALIGN_CENTER });
    }


    private void AddSelectableItems(PdfPTable table, bool isBoom)
    {
        foreach (var sp in selectablePrices)
        {
            if (sp == null || sp.objectPricingData == null || sp.isBoomObject != isBoom)
                continue;

            var data = sp.objectPricingData;

            string partNumber = string.IsNullOrEmpty(data.PartNumber) ? "N/A" : data.PartNumber;
            string name = string.IsNullOrEmpty(data.ObjectSize)
                ? data.ObjectName
                : $"{data.ObjectSize} {data.ObjectName}";

            int qty = 1; // Default quantity
            double unitPrice = data.ListPrice + (data.isSimFlexArmAvailable ? data.SimFlexPrice : 0);
            double extPrice = unitPrice * qty; // EXT LIST PRICE = LIST PRICE * QTY

            table.AddCell(CreateLeftAlignedCell(partNumber, normalFont));
            table.AddCell(CreateLeftAlignedCell(name, normalFont));
            table.AddCell(CreateCenteredCell(qty.ToString(), normalFont));
            table.AddCell(CreateRightAlignedCell(unitPrice.ToString("C"), normalFont));
            table.AddCell(CreateRightAlignedCell(extPrice.ToString("C"), normalFont)); // EXT = UNIT * QTY
        }
    }

    private void AddDropdownItems(PdfPTable table, bool isBoom)
    {
        foreach (var state in DropdownPopulator.GetAllCurrentStates())
        {
            if (state.Item1.isBoomExcelFileDropDown != isBoom) continue;

            var data = state.Item2;
            if (data == null || data.ListPrice == 0) continue;
            if (string.IsNullOrWhiteSpace(data.ObjectName)
                || data.ObjectName.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
                continue;

            int qty = 1; // Default quantity for dropdown items
            double unitPrice = data.ListPrice;
            double extPrice = unitPrice * qty; // EXT LIST PRICE = LIST PRICE * QTY

            table.AddCell(CreateLeftAlignedCell(data.PartNumber ?? "N/A", normalFont));
            table.AddCell(CreateLeftAlignedCell(data.ObjectName, normalFont));
            table.AddCell(CreateCenteredCell(qty.ToString(), normalFont));
            table.AddCell(CreateRightAlignedCell(unitPrice.ToString("C"), normalFont));
            table.AddCell(CreateRightAlignedCell(extPrice.ToString("C"), normalFont)); // EXT = UNIT * QTY
        }
    }

    private void AddRowToTable(PdfPTable table, string title, BaseColor textColor, BaseColor bg, int border)
    {
        string[] headers = { "PART #", title, "QTY", "LIST PRICE", "EXT LIST PRICE" };
        foreach (string h in headers)
        {
            PdfPCell cell = new PdfPCell(new Phrase(h, new Font(normalFont.BaseFont, normalFont.Size, normalFont.Style, textColor)))
            {
                BackgroundColor = bg,
                Border = border,
                PaddingTop = 4f,
                PaddingBottom = 4f,
                HorizontalAlignment = h switch
                {
                    "QTY" => Element.ALIGN_CENTER,
                    "LIST PRICE" or "EXT LIST PRICE" => Element.ALIGN_RIGHT,
                    _ => Element.ALIGN_LEFT
                }
            };
            table.AddCell(cell);
        }
    }

    private void AddRowToTable(PdfPTable table, PriceExcelData data)
    {
        int qty = 1; // Default quantity for dropdown items
        double unitPrice = data.ListPrice;
        double extPrice = unitPrice * qty; // EXT LIST PRICE = LIST PRICE * QTY
        
        table.AddCell(CreateLeftAlignedCell(data.PartNumber ?? "N/A", normalFont));
        table.AddCell(CreateLeftAlignedCell(data.ObjectName, normalFont));
        table.AddCell(CreateCenteredCell(qty.ToString(), normalFont));
        table.AddCell(CreateRightAlignedCell(unitPrice.ToString("C"), normalFont));
        table.AddCell(CreateRightAlignedCell(extPrice.ToString("C"), normalFont)); // EXT = UNIT * QTY
    }

    private void AddTotalsTable(Document doc)
    {
        PdfPTable table = new PdfPTable(2);
        table.WidthPercentage = 100;
        table.SetWidths(new float[] { 8, 2 });

        double total = CalculateTotalPrice();
        double discounted = total - (total * discountPercentage / 100);

        AddTotalLine("TOTAL EQUIPMENT LIST PRICE", total.ToString("C"), table);
        AddTotalLine("DISCOUNT %", discountPercentage.ToString("F1") + "%", table);
        AddTotalLine("GRAND TOTAL PRICE", discounted.ToString("C"), table);

        doc.Add(table);
    }

    private void AddTotalLine(string label, string value, PdfPTable table)
    {
        PdfPCell labelCell = new PdfPCell(new Phrase(label, boldFont)) { Border = PdfPCell.BOTTOM_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT, Padding = 5 };
        PdfPCell valueCell = new PdfPCell(new Phrase(value, boldFont)) { Border = PdfPCell.BOTTOM_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT, Padding = 5 };
        table.AddCell(labelCell);
        table.AddCell(valueCell);
    }

    private void AddNotes(Document doc)
    {
        doc.Add(new Paragraph("Notes:", boldFont));
        doc.Add(new Paragraph(note1, boldFont));
        doc.Add(new Paragraph(note2, notesFont));
    }

    private void AddAcceptanceTable(Document doc)
    {
        PdfPTable table = new PdfPTable(2);
        table.WidthPercentage = 100;
        table.SetWidths(new float[] { 60, 40 });

        PdfPCell right = new PdfPCell();
        right.Border = Rectangle.BOX;
        right.AddElement(new Paragraph(note3, notesFont));
        right.AddElement(new Paragraph("Signature                                      Date", normalFont));

        PdfPCell left = new PdfPCell();
        left.Border = Rectangle.BOX;
        PdfPTable inner = new PdfPTable(1);
        inner.WidthPercentage = 100;

        // Prefer values already applied for this export; fall back to live Client Metadata.
        string acct = !string.IsNullOrWhiteSpace(accountName)
            ? accountName
            : GetClientMetaDataSafely("AccountName");
        string addressLine1 = GetClientMetaDataSafely("AccountAddressLine1");
        string addressLine2 = GetClientMetaDataSafely("AccountAddressLine2");
        if (string.IsNullOrWhiteSpace(addressLine1) && !string.IsNullOrWhiteSpace(accountAddress))
        {
            addressLine1 = accountAddress;
            addressLine2 = "";
        }

        string proj = !string.IsNullOrWhiteSpace(projectName)
            ? projectName
            : GetClientMetaDataSafely("ProjectName");
        string projNo = !string.IsNullOrWhiteSpace(projectNumber)
            ? projectNumber
            : GetClientMetaDataSafely("ProjectNumber");
        string orderRef = !string.IsNullOrWhiteSpace(referenceNumber)
            ? referenceNumber
            : GetClientMetaDataSafely("OrderReferenceNumber");

        AddRowWithBottomBorder(inner, "Account Name: " + acct, normalFont);
        AddRowWithBottomBorder(inner, "Account Address: " + addressLine1, normalFont);
        AddRowWithBottomBorder(inner, " " + addressLine2, normalFont);
        AddRowWithBottomBorder(inner, "Project Name: " + proj, normalFont);
        AddRowWithBottomBorder(inner, "Project Number: " + projNo, normalFont);
        AddRowWithBottomBorder(inner, "Order Reference #: " + orderRef, normalFont, false);

        left.AddElement(inner);

        table.AddCell(right);
        table.AddCell(left);
        doc.Add(table);
    }

    private string GetClientMetaDataSafely(string propertyName)
    {
        try
        {
            var type = typeof(UI_ClientMetaData);
            var property = type.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (property != null)
            {
                return property.GetValue(null)?.ToString() ?? "";
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Error accessing UI_ClientMetaData.{propertyName}: {e.Message}");
        }
        return "";
    }

    private void AddRowWithBottomBorder(PdfPTable table, string text, Font font, bool border = true)
    {
        PdfPCell cell = new PdfPCell(new Phrase(text, font));
        cell.Border = border ? PdfPCell.BOTTOM_BORDER : Rectangle.NO_BORDER;
        cell.PaddingBottom = 5;
        table.AddCell(cell);
    }

    private PdfPCell CreateLeftAlignedCell(string text, Font font)
    {
        return new PdfPCell(new Phrase(text, font)) 
        { 
            Border = Rectangle.NO_BORDER, 
            HorizontalAlignment = Element.ALIGN_LEFT,
            PaddingTop = 4f,
            PaddingBottom = 4f
        };
    }

    private PdfPCell CreateRightAlignedCell(string text, Font font)
    {
        return new PdfPCell(new Phrase(text, font)) 
        { 
            Border = Rectangle.NO_BORDER, 
            HorizontalAlignment = Element.ALIGN_RIGHT,
            PaddingTop = 4f,
            PaddingBottom = 4f
        };
    }

    private PdfPCell CreateCenteredCell(string text, Font font)
    {
        return new PdfPCell(new Phrase(text, font)) 
        { 
            Border = Rectangle.NO_BORDER, 
            HorizontalAlignment = Element.ALIGN_CENTER,
            PaddingTop = 4f,
            PaddingBottom = 4f
        };
    }

    private double CalculateTotalPrice()
    {
        return ProposalPricingResolver.CalculateGrandEquipmentTotal(
            selectablePrices ?? Array.Empty<SelectablePrice>());
    }

    private Selectable GetRootParent(Selectable selectable)
    {
        while (selectable.ParentSelectable != null)
            selectable = selectable.ParentSelectable;
        return selectable;
    }

    private void AddImageToPDF(Document doc, string imgPath, PdfWriter writer, float maxHeight)
    {
        try
        {
            if (!string.IsNullOrEmpty(imgPath) && File.Exists(imgPath))
            {
                Image img = Image.GetInstance(imgPath);
                img.ScaleToFit(doc.PageSize.Width - doc.LeftMargin - doc.RightMargin, maxHeight);
                img.Alignment = Image.ALIGN_CENTER;
                doc.Add(img);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to add image to PDF: {e.Message}");
        }
    }

    /// <summary>
    /// Generate a unique file name by appending a counter suffix if the file already exists
    /// </summary>
    private string GenerateUniqueFileName(string directory, string baseFileName, string extension)
    {
        // Sanitize file name
        string safeBaseFileName = string.Join("_", baseFileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        
        string fileName = $"{safeBaseFileName}{extension}";
        string filePath = Path.Combine(directory, fileName);

        // File exists, append counter suffix
        if (File.Exists(filePath))
        {
            int counter = 1;
            while (true)
            {
                string numberedFileName = $"{safeBaseFileName}_{counter}{extension}";
                filePath = Path.Combine(directory, numberedFileName);

                // If this file name doesn't exist, use it
                if (!File.Exists(filePath))
                    break;

                counter++;
            }
        }

        return Path.GetFileName(filePath);
    }

    #endregion

    #region Dynamic Boom Description Helpers
    
    /// <summary>
    /// Builds a dynamic boom model description based on the actual boom configuration.
    /// Shared with the proposal workspace preview so mock and PDF stay aligned.
    /// </summary>
    public static string BuildBoomModelDescription(GameObject boomRoot)
    {
        if (boomRoot == null)
            return "ARTICULATING BOOM";

        var description = new System.Text.StringBuilder();
        
        // Get all selectables in the boom hierarchy
        var allSelectables = boomRoot.GetComponentsInChildren<Selectable>(true);
        
        // Determine boom type from UIButtonName (exclude S-Series)
        string boomType = DetermineBoomType(allSelectables);
        if (!string.IsNullOrEmpty(boomType))
        {
            description.Append(boomType);
        }
        
        // Get service head configuration if present
        var serviceHeadInfo = GetServiceHeadInfo(allSelectables);
        if (!string.IsNullOrEmpty(serviceHeadInfo))
        {
            if (description.Length > 0) description.Append(", ");
            description.Append(serviceHeadInfo);
        }
        
        // Get articulating configuration (exclude top arm info)
        var articulatingInfo = GetArticulatingInfo(allSelectables);
        if (!string.IsNullOrEmpty(articulatingInfo))
        {
            if (description.Length > 0) description.Append(", ");
            description.Append(articulatingInfo);
        }
        
        // Get active rows count
        var rowsInfo = GetActiveRowsInfo(allSelectables);
        if (!string.IsNullOrEmpty(rowsInfo))
        {
            if (description.Length > 0) description.Append(", ");
            description.Append(rowsInfo);
        }
        
        // Get accessories count
        var accessoriesInfo = GetAccessoriesInfo(allSelectables);
        if (!string.IsNullOrEmpty(accessoriesInfo))
        {
            if (description.Length > 0) description.Append(", ");
            description.Append(accessoriesInfo);
        }
        
        return description.ToString();
    }
    
    /// <summary>
    /// Determines the boom type from UIButtonName, excluding S-Series and top arm info
    /// </summary>
    private static string DetermineBoomType(Selectable[] selectables)
    {
        foreach (var selectable in selectables)
        {
            if (!string.IsNullOrEmpty(selectable.UIButtonName))
            {
                string uiName = selectable.UIButtonName.ToLower();
                
                // Skip top arm selectables
                if (uiName.Contains("top arm") || uiName.Contains("toparm"))
                    continue;
                
                return selectable.UIButtonName;
                
            }
        }
        
        // Fallback to generic
        return "STANDARD POWERED";
    }
    
    /// <summary>
    /// Gets service head configuration info including size
    /// </summary>
    private static string GetServiceHeadInfo(Selectable[] selectables)
    {
        foreach (var selectable in selectables)
        {
            // Look for service head components
            var scaleHandler = selectable.GetComponent<BoomHeadScaleHandler>();
            if (scaleHandler != null)
            {
                // Try to get size from current scale level
                var currentScale = selectable.CurrentPreviewScaleLevel;
                if (currentScale != null && currentScale.Size > 0)
                {
                    int mm = currentScale.Size >= 10f
                        ? Mathf.RoundToInt(currentScale.Size)
                        : Mathf.RoundToInt(currentScale.Size * 1000f);
                    return $"{mm}mm Service Head";
                }
                
                // Fallback: try to determine from UIButtonName
                if (!string.IsNullOrEmpty(selectable.UIButtonName))
                {
                    if (selectable.UIButtonName.Contains("600"))
                        return "600mm Service Head";
                    else if (selectable.UIButtonName.Contains("200"))
                        return "200mm Service Head";
                    else if (selectable.UIButtonName.Contains("400"))
                        return "400mm Service Head";
                }
            }
        }
        
        return "";
    }
    
    /// <summary>
    /// Gets articulating arm configuration, excluding top arm info
    /// </summary>
    private static string GetArticulatingInfo(Selectable[] selectables)
    {
        foreach (var selectable in selectables)
        {
            if (!string.IsNullOrEmpty(selectable.UIButtonName))
            {
                string uiName = selectable.UIButtonName.ToLower();
                
                // Skip top arm selectables
                if (uiName.Contains("top arm") || uiName.Contains("toparm"))
                    continue;
                
                if (uiName.Contains("xl") && uiName.Contains("powered"))
                    return "XL Articulating";
                else if (uiName.Contains("articulating"))
                    return "Articulating";
                else if (uiName.Contains("spring") && uiName.Contains("arm"))
                    return "Spring Articulating";
            }
        }
        
        return "Articulating";
    }
    
    /// <summary>
    /// Gets the number of active rows from service head configuration
    /// </summary>
    private static string GetActiveRowsInfo(Selectable[] selectables)
    {
        foreach (var selectable in selectables)
        {
            var scaleHandler = selectable.GetComponent<BoomHeadScaleHandler>();
            if (scaleHandler != null)
            {
                var currentScale = selectable.CurrentPreviewScaleLevel;
                if (currentScale != null && currentScale.TryGetValue("rows", out string rowsStr))
                {
                    if (int.TryParse(rowsStr, out int rowCount))
                    {
                        return $"{rowCount} ROW";
                    }
                }
                
                // Fallback: count active rows from scale handler
                if (scaleHandler.attachRow != null)
                {
                    int activeRows = 0;
                    foreach (var row in scaleHandler.attachRow)
                    {
                        if (row.entries != null && row.entries.Any(entry => entry != null && entry.activeInHierarchy))
                        {
                            activeRows++;
                        }
                    }
                    if (activeRows > 0)
                    {
                        return $"{activeRows} ROW";
                    }
                }
            }
        }
        
        return "";
    }
    
    /// <summary>
    /// Gets the accessories count from attached components
    /// </summary>
    private static string GetAccessoriesInfo(Selectable[] selectables)
    {
        int accessoryCount = 0;
        
        foreach (var selectable in selectables)
        {
            // Count outlets and other accessories
            if (!string.IsNullOrEmpty(selectable.UIButtonName))
            {
                string uiName = selectable.UIButtonName.ToLower();
                
                if (uiName.Contains("outlet") || 
                    uiName.Contains("accessory") || 
                    uiName.Contains("ethernet") ||
                    uiName.Contains("gas") ||
                    uiName.Contains("power"))
                {
                    accessoryCount++;
                }
            }
            
 
        }
        
        if (accessoryCount > 0)
        {
            return $"{accessoryCount}A";
        }
        
        return "";
    }
    
    #endregion

    public class PageEventHelper : PdfPageEventHelper
    {
        public string clientName;
        public string projectName;
        public string configName;

        const string EffectiveDateText = "Effective Date: 90 Days from Delivery";
        const int FooterMarginBottom = 10;
        const int ProjectInfoStartY = 25;
        const int LineSpacing = 10;
        const float FooterFontSize = 8f;

        PdfTemplate _totalPageTemplate;
        BaseFont _footerBaseFont;

        public override void OnOpenDocument(PdfWriter writer, Document document)
        {
            _footerBaseFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.EMBEDDED);
            _totalPageTemplate = writer.DirectContent.CreateTemplate(28f, FooterFontSize + 2f);
        }

        public override void OnEndPage(PdfWriter writer, Document document)
        {
            PdfContentByte cb = writer.DirectContent;
            var font = new Font(_footerBaseFont, FooterFontSize);

            // "Page X of " now; total is filled into the template on close.
            string prefix =
                $"{EffectiveDateText}          {clientName} Proposal | Page {writer.PageNumber} of ";
            float y = document.Bottom - FooterMarginBottom;
            float x = document.LeftMargin;
            float prefixWidth = _footerBaseFont.GetWidthPoint(prefix, FooterFontSize);

            ColumnText.ShowTextAligned(cb, Element.ALIGN_LEFT, new Phrase(prefix, font), x, y, 0);
            // 6-float form avoids System.Drawing.Matrix overloads that Unity can't resolve.
            if (_totalPageTemplate != null)
                cb.AddTemplate(_totalPageTemplate, 1f, 0f, 0f, 1f, x + prefixWidth, y);

            if (writer.PageNumber > 1)
                AddProjectInfoFooter(cb, font, document);
        }

        public override void OnCloseDocument(PdfWriter writer, Document document)
        {
            if (_totalPageTemplate == null || _footerBaseFont == null)
                return;

            // iTextSharp reports PageNumber+1 in OnCloseDocument; subtract one for the real total.
            int total = Mathf.Max(1, writer.PageNumber - 1);
            ColumnText.ShowTextAligned(
                _totalPageTemplate,
                Element.ALIGN_LEFT,
                new Phrase(total.ToString(), new Font(_footerBaseFont, FooterFontSize)),
                0f,
                1f,
                0f);
        }

        void AddProjectInfoFooter(PdfContentByte contentByte, Font font, Document document)
        {
            AddFooterLine(contentByte, font, document, $"Project: {projectName}", ProjectInfoStartY);
            AddFooterLine(contentByte, font, document, $"Configuration 1: {configName}", ProjectInfoStartY + LineSpacing);
            AddFooterLine(contentByte, font, document, $"Submitted To: {clientName}", ProjectInfoStartY + (LineSpacing * 2));
        }

        static void AddFooterLine(PdfContentByte contentByte, Font font, Document document, string text, int yOffset)
        {
            ColumnText.ShowTextAligned(
                contentByte,
                Element.ALIGN_LEFT,
                new Phrase(text, font),
                document.LeftMargin,
                document.Bottom - yOffset,
                0);
        }
    }

}