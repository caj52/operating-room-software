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
    private readonly string companyPhone = "Tel: 1 877 789 8106";



    // Client Details (public for inspector input)
    public string clientName = "Client Name";
    public string projectName = "Project Name";
    public string configName = "Tandem Equipment Boom with Light";
    public string salesRepName = "Sales Rep Name";
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
    
    // Image cleanup tracking
    private List<string> tempImagePaths = new List<string>();

    #endregion

    private void Start()
    {
        screenshot = FindObjectOfType<ScreenshotCapture>();
    }

    private void OnDestroy()
    {
        // Cancel any ongoing PDF generation
        if (pdfGenerationCoroutine != null)
        {
            StopCoroutine(pdfGenerationCoroutine);
            pdfGenerationCoroutine = null;
        }
    }

    public void GeneratePDF()
    {
        // Always pull Client Metadata + room + saved sales rep before writing.
        ApplyExportDefaults();

        // Cancel any existing generation
        if (pdfGenerationCoroutine != null)
        {
            StopCoroutine(pdfGenerationCoroutine);
        }

        isCancelled = false;
        pdfGenerationCoroutine = StartCoroutine(GeneratePDFCoroutine());
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
        salesRepEmail = UI_ClientMetaData.SalesRepEmail ?? "";
        if (IsPlaceholder(salesRepName))
            salesRepName = "";
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

        // If the document workspace is open, keep mock and PDF identical.
        var workspaceModel = UI_ProposalWorkspace.Instance != null
            ? UI_ProposalWorkspace.Instance.ActiveModel
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
        _completionCallback = callback;
        GeneratePDF();
    }

    private IEnumerator GeneratePDFCoroutine()
    {
        path = null;
        bool hasError = false;
        string errorMessage = "";
        string filePath = "";
        
        // Clear any previous temp image paths
        tempImagePaths.Clear();

        // Show loading screen with cancel option (skip when unified export already owns it)
        if (!ExportOrchestrator.SuppressIndividualDialogs)
        {
            UI_GeneralLoadingScreen.instance.ShowLoadingScreen();
            UI_GeneralLoadingScreen.instance.OnCancel += HandleCancellation;
        }
        UI_GeneralLoadingScreen.instance.SetStatus("Initializing PDF generation...");
        UI_GeneralLoadingScreen.instance.SetProgress(0f);

        // Step 1: Capture screenshot
        UI_GeneralLoadingScreen.instance.SetStatus("Capturing ceiling view for visualization...");
        UI_GeneralLoadingScreen.instance.SetProgress(0.1f);

        yield return StartCoroutine(
            screenshot.CaptureCeilingOnly(
                RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).transform.position + new Vector3(0, 10, 0),
                null,
                (resultPath) => path = resultPath
            )
        );

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
            UI_GeneralLoadingScreen.instance.SetStatus("Ceiling view captured successfully");
            UI_GeneralLoadingScreen.instance.SetProgress(0.2f);
        }
        else if (!isCancelled)
        {
            Debug.LogWarning("Screenshot capture failed or timed out, continuing without image");
            UI_GeneralLoadingScreen.instance.SetStatus("Continuing without ceiling view image");
        }

        if (isCancelled)
        {
            CleanupAndShowResult(isCancelled, hasError, errorMessage, filePath);
            yield break;
        }

        // Step 2: Prepare pricing data
        UI_GeneralLoadingScreen.instance.SetStatus("Collecting and organizing pricing data...");
        UI_GeneralLoadingScreen.instance.SetProgress(0.3f);
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

        // Check if we have any pricing data
        if (!hasError && (selectablePrices == null || selectablePrices.Length == 0))
        {
            hasError = true;
            errorMessage = "No pricing data found. Please ensure you have configured at least one item.";
        }

        if (isCancelled || hasError)
        {
            CleanupAndShowResult(isCancelled, hasError, errorMessage, filePath);
            yield break;
        }

        UI_GeneralLoadingScreen.instance.SetStatus($"Successfully collected {selectablePrices.Length} pricing items");
        UI_GeneralLoadingScreen.instance.SetProgress(0.4f);

        // Step 3: Generate PDF
        // Sanitize config name for safe filename
        string safeConfigName = string.IsNullOrWhiteSpace(configName)
            ? "Config"
            : string.Join("_", configName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        
        // Resolve output path (Documents/Operating Room Exports/{room}/proposals by default)
        string salesProposalsPath = ExportPaths.ProposalsDir;
        
        // Ensure target directory exists
        if (!Directory.Exists(salesProposalsPath))
        {
            Directory.CreateDirectory(salesProposalsPath);
            Debug.Log($"Created SalesProposals folder at: {salesProposalsPath}");
        }
        
        // Generate unique filename to prevent overwriting using existing method
        string baseFileName = $"SalesProposal_{safeConfigName}";
        string fileName = GenerateUniqueFileName(salesProposalsPath, baseFileName, ".pdf");
        
        filePath = Path.Combine(salesProposalsPath, fileName);


        UI_GeneralLoadingScreen.instance.SetStatus("Creating PDF document structure...");
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

        if (!hasError && pdfGenerated && !isCancelled)
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
            document = new Document(PageSize.A4, 18, 18, 18, 18);
            writer = PdfWriter.GetInstance(document, new FileStream(filePath, FileMode.Create));
            writer.PageEvent = new PageEventHelper { clientName = clientName, projectName = projectName, configName = configName };

            document.Open();
            SetupFonts();

            if (!isCancelled)
            {
                UI_GeneralLoadingScreen.instance.SetStatus("Generating configuration summary page...");
                UI_GeneralLoadingScreen.instance.SetProgress(0.5f);
                GenerateFirstPage(document);
            }

            if (!isCancelled)
            {
                document.NewPage();
                UI_GeneralLoadingScreen.instance.SetStatus("Adding visual representations...");
                UI_GeneralLoadingScreen.instance.SetProgress(0.6f);
                GenerateSecondPage(document, writer);
            }

            if (!isCancelled)
            {
                document.NewPage();
                UI_GeneralLoadingScreen.instance.SetStatus("Creating detailed pricing breakdown...");
                UI_GeneralLoadingScreen.instance.SetProgress(0.8f);
                GeneratePricingPage(document, writer);
            }

            document.Close();
            return !isCancelled;
        }
        catch (Exception e)
        {
            document?.Close();
            writer?.Close();
            throw;
        }
    }

    private void CleanupAndShowResult(bool cancelled, bool hasError, string errorMessage, string filePath)
    {
        // Clean up temporary images
        CleanupTemporaryImages();
        
        // Clean up UI references
        UI_GeneralLoadingScreen.instance.OnCancel -= HandleCancellation;
        if (!ExportOrchestrator.SuppressIndividualDialogs)
            UI_GeneralLoadingScreen.instance.HideLoadingScreen();
        pdfGenerationCoroutine = null;

        var callback = _completionCallback;
        _completionCallback = null;
        callback?.Invoke(!cancelled && !hasError, filePath, errorMessage);

        if (SuppressCompletionDialog || ExportOrchestrator.SuppressIndividualDialogs)
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
            UI_DialogPrompt.Open(
                $"Sales proposal saved to:\n{filePath}",
                new ButtonAction("Open PDF", () => OpenPDF(filePath)),
                new ButtonAction("Copy Path", () => {
                    GUIUtility.systemCopyBuffer = filePath;
                    UI_DialogPrompt.Open("Path copied to clipboard!", new ButtonAction("OK"));
                }),
                new ButtonAction("Done")
            );

            // Try to open the PDF automatically
            OpenPDF(filePath);
        }
    }
    
    /// <summary>
    /// Cleans up all temporary image files created during PDF generation
    /// </summary>
    private void CleanupTemporaryImages()
    {
        // Delete ceiling screenshot if it exists
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
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

        // Delete all elevation images
        foreach (string imagePath in tempImagePaths)
        {
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                try
                {
                    File.Delete(imagePath);
                    Debug.Log($"Deleted elevation image: {imagePath}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to delete elevation image {imagePath}: {e.Message}");
                }
            }
        }
        
        // Clear the tracking list
        tempImagePaths.Clear();
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
            UI_DialogPrompt.Open(
                "Unable to open PDF automatically. You can find it at:\n" + filePath,
                new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = filePath),
                new ButtonAction("OK")
            );
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
            selectablePrices = FindObjectsOfType<SelectablePrice>();
            selectables = new Selectable[selectablePrices.Length];

            for (int i = 0; i < selectablePrices.Length; i++)
            {
                selectables[i] = selectablePrices[i].transform.root.GetComponentInChildren<Selectable>();
            }

            // Group names by object size + name
            concatedStrBoomObjectsName = string.Join(", ", selectablePrices
                .Where(sp => sp.isBoomObject && sp.objectPricingData != null)
                .OrderBy(sp => GetHierarchyPath(sp.transform))
                .Select(sp => string.IsNullOrEmpty(sp.objectPricingData.ObjectSize)
                    ? sp.objectPricingData.ObjectName
                    : $"{sp.objectPricingData.ObjectSize} {sp.objectPricingData.ObjectName}".Trim()));

            concatedStrNonBoomObjectsName = string.Join(", ", selectablePrices
                .Where(sp => !sp.isBoomObject)
                .Select(sp => sp.objectPricingData.ObjectName));

            // Count
            boomObjectsCount = selectablePrices.Count(sp => sp.isBoomObject && UINameToExcelKey.IsBoomBaseModelFromExcel(sp.pricingObjectName));
            nonBoomObjectsCount = selectablePrices.Count(sp => !sp.isBoomObject);

            // Pricing
            boomTotalPrice = selectablePrices.Where(sp => sp.isBoomObject)
                .Sum(sp => sp.objectPricingData.ListPrice + (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0));

            nonBoomTotalPrice = selectablePrices.Where(sp => !sp.isBoomObject)
                .Sum(sp => sp.objectPricingData.ListPrice + (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0));

            (concatedStrBoomObjectsName, boomTotalPriceDd, concatedStrNonBoomObjectsName, nonBoomTotalPriceDd) = GetSelectedBoomAndNonBoomData();

            /*            concatedStrNonBoomObjectsPartName = string.Join(", ", selectablePrices
                            .Where(sp => !sp.isBoomObject)
                            .Select(sp => ConcatenateBtNamesFromSelectablePrice(sp))
                            .Where(bt => !string.IsNullOrEmpty(bt)));*/
        }
        catch (Exception e)
        {
            Debug.LogError($"Error preparing quote data: {e.Message}");
            throw;
        }
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

    private Dictionary<string, List<string>> GetBtNamesGroupedByType(List<SelectablePrice> selectables)
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
    private string GetHierarchyPath(Transform transform)
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

        var rootGroups = selectablePrices
            .Where(sp => sp.objectPricingData != null)
            .GroupBy(sp => sp.transform.root)
            .ToList();

        int configCount = 1;

        foreach (var group in rootGroups)
        {
            var first = group.Last();
            var lights = group.Where(sp => !sp.isBoomObject).ToList();
            var booms = group.Where(sp => sp.isBoomObject).ToList();


            document.Add(Chunk.NEWLINE);
            AddTableTitle(document, configName);

            // Handle lights section if lights exist
            if (lights.Count > 0)
            {
                PdfPTable lightTable = CreateModelTable("MODEL DESCRIPTION", "QTY", "LIGHT", lights.Count.ToString());
                document.Add(lightTable);
                AddHorizontalLine(document);

                AddSectionHeader(document, "OPTION/ACCESSORY DESCRIPTION");
                // Old-style text-only options for LIGHT
                string lightOptionsText = BuildOptionsDescriptionString(lights, false);
                PdfPTable lightOptions = CreateOptionTable(lightOptionsText);
                document.Add(lightOptions);
                document.Add(Chunk.NEWLINE);
            }

            // Handle booms section if booms exist
            if (booms.Count > 0)
            {
                // Special quantity logic for Boom - Tandem Ceiling Cover (same as pricing page)
                int boomQty = 1; // default quantity
                
                // Check if this is a Boom - Tandem Ceiling Cover configuration
                var firstSp = group.FirstOrDefault();
                if (firstSp != null)
                {
                    var rootSelectables = firstSp.transform.root.GetComponentsInChildren<Selectable>(true);
                    bool isTandemCeilingCover = rootSelectables.Any(s => 
                        !string.IsNullOrEmpty(s.UIButtonName) && 
                        s.UIButtonName.Contains("Boom - Tandem Ceiling Cover"));

                    if (isTandemCeilingCover)
                    {
                        // Count boom service heads attached to this configuration
                        int serviceHeadCount = 0;
                        foreach (var selectable in rootSelectables)
                        {
                            var scaleHandler = selectable.GetComponent<BoomHeadScaleHandler>();
                            if (scaleHandler != null)
                            {
                                serviceHeadCount++;
                            }
                        }
                        
                        // If 2 service heads are found, set quantity to 2
                        if (serviceHeadCount >= 2)
                        {
                            boomQty = 2;
                        }
                    }
                }

                PdfPTable boomTable = CreateModelTable("MODEL DESCRIPTION", "QTY", "ARTICULATING BOOM", boomQty.ToString());
                document.Add(boomTable);
                AddHorizontalLine(document);

                AddSectionHeader(document, "OPTION/ACCESSORY DESCRIPTION");
                // Old-style text-only options for BOOM
                string boomOptionsText = BuildOptionsDescriptionString(booms, true);
                PdfPTable boomOptions = CreateOptionTable(boomOptionsText);
                document.Add(boomOptions);
                AddHorizontalLine(document);
            }

            // Calculate and display total if we have any items (lights or booms)
            if (lights.Count > 0 || booms.Count > 0)
            {
                double configTotal = 0;

                // Calculate total from the group, handling both UI and non-UI cases
                foreach (var sp in group)
                {
                    if (sp.UIRefPricingRowDataFill != null)
                    {
                        configTotal += sp.UIRefPricingRowDataFill.Price;
                    }
                    else if (sp.objectPricingData != null)
                    {
                        configTotal += sp.objectPricingData.ListPrice +
                            (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0);
                    }
                }

                PdfPTable totalTable = CreateTotalTable("EQUIPMENT TOTAL LIST PRICE", configTotal.ToString("C"));
                document.Add(totalTable);
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

        for (int i = 0; i < selectables.Length; i++)
        {
            Selectable root = GetRootParent(selectables[i]);
            if (!processedRoots.Add(root)) continue;

            List<PdfExporter.PdfImageData> imageData = selectables[i].ExportElevationPdf();
            if (i > 0)
            {
                document.NewPage();
                AddCompanyHeader(document);
            }

            // Add ceiling image
            AddImageToPDF(document, path, pdfWriter, imageHeight);
            
            // Add elevation images and track paths for cleanup
            if (imageData.Count > 0)
            {
                AddImageToPDF(document, imageData[0].Path, pdfWriter, imageHeight);
                // Track image paths for cleanup
                foreach (var imgData in imageData)
                {
                    if (!string.IsNullOrEmpty(imgData.Path))
                    {
                        tempImagePaths.Add(imgData.Path);
                    }
                }
            }
        }
    }

    private void GeneratePricingPage(Document document, PdfWriter writer)
    {
        AddCompanyHeader(document);

        // 1) Group by root-level configuration
        var rootConfigs = selectablePrices
            .Where(sp => sp != null && sp.objectPricingData != null)
            .GroupBy(sp => sp.transform.root)
            .ToList();

        int configNumber = 1;

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

            // ---------- LIGHT MODELS ----------
            // Aggregate light rows by PartNumber+Name+UnitPrice (as before)
            var lightGroups = configGroup
                .Where(sp => sp.objectPricingData != null &&
                             sp.objectPricingData.ObjectName != null &&
                             sp.objectPricingData.ObjectName.ToLower().Contains("light"))
                .GroupBy(sp => new
                {
                    sp.objectPricingData.PartNumber,
                    Name = string.IsNullOrEmpty(sp.objectPricingData.ObjectSize)
                        ? sp.objectPricingData.ObjectName
                        : $"{sp.objectPricingData.ObjectSize} {sp.objectPricingData.ObjectName}",
                    UnitPrice = sp.objectPricingData.ListPrice +
                               (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0)
                })
                .ToList(); // Convert to list to avoid multiple enumeration

            // Only add light models header if there are actual light items
            if (lightGroups.Count > 0)
            {
                AddRowToTable(table, "MODEL DESCRIPTION", BaseColor.WHITE, BaseColor.BLACK, PdfPCell.NO_BORDER);

                foreach (var g in lightGroups)
                {
                    int qty = g.Count();
                    double unit = g.Key.UnitPrice;
                    double ext = unit * qty; // EXT LIST PRICE = LIST PRICE * QTY

                    // Prefer UI row if any item has it
                    var withUI = g.FirstOrDefault(x => x.UIRefPricingRowDataFill != null);
                    if (withUI?.UIRefPricingRowDataFill != null)
                    {
                        var ui = withUI.UIRefPricingRowDataFill;
                        table.AddCell(CreateLeftAlignedCell(ui.partNo.text ?? "N/A", normalFont));
                        table.AddCell(CreateLeftAlignedCell(g.Key.Name, normalFont));
                        table.AddCell(CreateCenteredCell(qty.ToString(), normalFont));
                        table.AddCell(CreateRightAlignedCell(unit.ToString("C"), normalFont));
                        table.AddCell(CreateRightAlignedCell((unit * qty).ToString("C"), normalFont)); // EXT = UNIT * QTY
                        subtotal += (unit * qty);
                    }
                    else
                    {
                        table.AddCell(CreateLeftAlignedCell(g.Key.PartNumber ?? "N/A", normalFont));
                        table.AddCell(CreateLeftAlignedCell(g.Key.Name, normalFont));
                        table.AddCell(CreateCenteredCell(qty.ToString(), normalFont));
                        table.AddCell(CreateRightAlignedCell(unit.ToString("C"), normalFont));
                        table.AddCell(CreateRightAlignedCell(ext.ToString("C"), normalFont)); // EXT = UNIT * QTY
                        subtotal += ext;
                    }
                }
                
                // Add a small spacer row after light models
                if (lightGroups.Count > 0)
                {
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
            }

            // ---------- LIGHT OPTIONS (priced line-items) ----------
            // Pull dropdown items that are NON-BOOM, skip "None"/zero
            var lightOptionData = DropdownPopulator.GetAllCurrentStates()
                .Where(s => s.Item1 != null && s.Item2 != null && !s.Item1.isBoomExcelFileDropDown)
                .Select(s => s.Item2)
                .Where(d => d != null &&
                            d.ListPrice > 0 &&
                            !string.Equals(d.ObjectName, "None", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (lightOptionData.Count > 0)
            {
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

            // ---------- BOOM MODELS (AGGREGATED) ----------
            var nonLightItems = configGroup
                .Where(sp => sp.objectPricingData != null &&
                             (sp.objectPricingData.ObjectName == null ||
                              !sp.objectPricingData.ObjectName.ToLower().Contains("light")))
                .ToList();

            // Only add boom models header if there are boom items
            if (nonLightItems.Count > 0)
            {
                AddRowToTable(table, "MODEL DESCRIPTION", BaseColor.WHITE, BaseColor.BLACK, PdfPCell.NO_BORDER);

                // Calculate total boom price first
                double totalBoomPrice = 0;
                string aggregatedPartNumber = "";
                string aggregatedDescription = "";

                // Calculate total price from all boom items
                foreach (var sp in nonLightItems)
                {
                    var data = sp.objectPricingData;
                    double itemPrice = data.ListPrice + (data.isSimFlexArmAvailable ? data.SimFlexPrice : 0);
                    
                    if (sp.UIRefPricingRowDataFill != null)
                    {
                        totalBoomPrice += sp.UIRefPricingRowDataFill.Price;
                    }
                    else
                    {
                        totalBoomPrice += itemPrice;
                    }
                }

                // Use the first item's part number and create dynamic description
                if (nonLightItems.Count > 0)
                {
                    var firstItem = nonLightItems.First();
                    aggregatedPartNumber = firstItem.objectPricingData.PartNumber ?? "N/A";
                    
                    // Build dynamic description based on boom configuration
                    aggregatedDescription = BuildBoomModelDescription(firstItem.transform.root.gameObject);
                }

                // Special quantity logic for Boom - Tandem Ceiling Cover
                int boomQty = 1; // default quantity
                
                // Check if this is a Boom - Tandem Ceiling Cover configuration
                var rootSelectables = firstSp.transform.root.GetComponentsInChildren<Selectable>(true);
                bool isTandemCeilingCover = rootSelectables.Any(s => 
                    !string.IsNullOrEmpty(s.UIButtonName) && 
                    s.UIButtonName.Contains("Boom - Tandem Ceiling Cover"));

                if (isTandemCeilingCover)
                {
                    // Count boom service heads attached to this configuration
                    int serviceHeadCount = 0;
                    foreach (var selectable in rootSelectables)
                    {
                        var scaleHandler = selectable.GetComponent<BoomHeadScaleHandler>();
                        if (scaleHandler != null)
                        {
                            serviceHeadCount++;
                        }
                    }
                    
                    // If 2 service heads are found, set quantity to 2
                    if (serviceHeadCount >= 2)
                    {
                        boomQty = 2;
                    }
                }

                // Pricing calculation based on quantity
                double listPrice, extendedPrice;
                
                if (boomQty == 2)
                {
                    // For tandem with 2 booms: LIST PRICE = cost of 1 boom, EXT LIST PRICE = LIST PRICE × 2
                    listPrice = totalBoomPrice/2;  // Cost of 1 boom
                    extendedPrice = listPrice*boomQty;  // Cost of 1 boom × 2 = total cost
                }
                else
                {
                    // For single boom: LIST PRICE = total boom cost, EXT LIST PRICE = LIST PRICE × 1
                    listPrice = totalBoomPrice;  // Total boom cost
                    extendedPrice = listPrice * boomQty;  // Same as list price since qty = 1
                }

                // Add single aggregated row for all boom items
                table.AddCell(CreateLeftAlignedCell(aggregatedPartNumber, normalFont));
                table.AddCell(CreateLeftAlignedCell(aggregatedDescription, normalFont));
                table.AddCell(CreateCenteredCell(boomQty.ToString(), normalFont));
                table.AddCell(CreateRightAlignedCell(listPrice.ToString("C"), normalFont)); // LIST PRICE (cost of 1 boom)
                table.AddCell(CreateRightAlignedCell(extendedPrice.ToString("C"), normalFont)); // EXT LIST PRICE = LIST PRICE × QTY

                subtotal += extendedPrice;
                
                // Add a small spacer row after boom models
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
            var boomOptionData = DropdownPopulator.GetAllCurrentStates()
                .Where(s => s.Item1 != null && s.Item2 != null && s.Item1.isBoomExcelFileDropDown)
                .Select(s => s.Item2)
                .Where(d => d != null &&
                            d.ListPrice > 0 &&
                            !string.Equals(d.ObjectName, "None", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (boomOptionData.Count > 0)
            {
                AddRowToTable(table, "OPTION/ACCESSORY DESCRIPTION", BaseColor.BLACK, new BaseColor(180,180,180), PdfPCell.NO_BORDER);

                foreach (var d in boomOptionData)
                {
                    AddRowToTable(table, d); // uses existing helper (qty=1, so EXT = LIST * 1 = LIST)
                    subtotal += d.ListPrice;
                }
            }

            // ---------- SUBTOTAL ----------
            // Only add subtotal if we have any content in the table
            if (lightGroups.Count > 0 || lightOptionData.Count > 0 || nonLightItems.Count > 0 || boomOptionData.Count > 0)
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
        leftCell.AddElement(new Paragraph($"{salesRepName}", titleFont));
        leftCell.AddElement(new Paragraph($"{salesRepEmail}", normalFont));
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
        Debug.LogError("++" + content);
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
            .Select(s => s.Item2.ObjectName);

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
            .Concat(ddValues);

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
        double selectablesPrice = 0;

        // Calculate price from selectables, handling both UI and non-UI cases
        foreach (var sp in selectablePrices)
        {
            if (sp == null) continue;

            if (sp.UIRefPricingRowDataFill != null)
            {
                // Use UI price if available
                selectablesPrice += sp.UIRefPricingRowDataFill.Price;
            }
            else if (sp.objectPricingData != null)
            {
                // Fallback to pricing data
                selectablesPrice += sp.objectPricingData.ListPrice +
                    (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0);
            }
        }

        double dropdownTotal = DropdownPopulator.GetAllCurrentStates()
            .Where(state => state.Item2 != null
                && !string.IsNullOrWhiteSpace(state.Item2.ObjectName)
                && !state.Item2.ObjectName.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
            .Sum(state => state.Item2.ListPrice);
        double install = FindObjectOfType<ExcelReader>()?.GetInstallationLightsCharges()?.ListPrice ?? 0;
        double ship = FindObjectOfType<ExcelReader>()?.GetShippingLightsCharges()?.ListPrice ?? 0;

        return selectablesPrice + dropdownTotal + install + ship;
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
    /// Builds a dynamic boom model description based on the actual boom configuration
    /// </summary>
    private string BuildBoomModelDescription(GameObject boomRoot)
    {
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
    private string DetermineBoomType(Selectable[] selectables)
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
    private string GetServiceHeadInfo(Selectable[] selectables)
    {
        foreach (var selectable in selectables)
        {
            // Look for service head components
            var scaleHandler = selectable.GetComponent<BoomHeadScaleHandler>();
            if (scaleHandler != null)
            {
                // Try to get size from current scale level
                var currentScale = selectable.CurrentPreviewScaleLevel;
                if (currentScale != null && currentScale.TryGetValue("size", out string sizeStr))
                {
                    return $"{sizeStr} Service Head";
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
    private string GetArticulatingInfo(Selectable[] selectables)
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
    private string GetActiveRowsInfo(Selectable[] selectables)
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
    private string GetAccessoriesInfo(Selectable[] selectables)
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
        // Document metadata
        public string clientName;
        public string projectName;
        public string configName;

        // Constants
        private const string EffectiveDateText = "Effective Date: 90 Days from Delivery";
        private const int FooterMarginBottom = 10;
        private const int ProjectInfoStartY = 25;
        private const int LineSpacing = 10;

        private int totalPageCount;

        public override void OnStartPage(PdfWriter writer, Document document)
        {
            // Track the total number of pages
            totalPageCount = writer.PageNumber;
        }

        public override void OnEndPage(PdfWriter writer, Document document)
        {
            PdfContentByte contentByte = writer.DirectContent;
            Font footerFont = CreateFooterFont();

            // Add page footer with page numbers
            AddPageFooter(contentByte, footerFont, document, writer);

            // Add project information footer (skip first page)
            if (writer.PageNumber > 1)
            {
                AddProjectInfoFooter(contentByte, footerFont, document);
            }
        }

        private Font CreateFooterFont()
        {
            return new Font(
                BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.EMBEDDED),
                8
            );
        }

        private void AddPageFooter(PdfContentByte contentByte, Font font, Document document, PdfWriter writer)
        {
            string footerText = $"{EffectiveDateText}          {clientName} Proposal | Page {writer.PageNumber} of {totalPageCount}";

            ColumnText.ShowTextAligned(
                contentByte,
                Element.ALIGN_CENTER,
                new Phrase(footerText, font),
                document.Right / 2 + document.LeftMargin,
                document.Bottom - FooterMarginBottom,
                0
            );
        }

        private void AddProjectInfoFooter(PdfContentByte contentByte, Font font, Document document)
        {
            // Project name
            AddFooterLine(
                contentByte,
                font,
                document,
                $"Project: {projectName}",
                ProjectInfoStartY
            );

            // Configuration name
            AddFooterLine(
                contentByte,
                font,
                document,
                $"Configuration 1: {configName}",
                ProjectInfoStartY + LineSpacing
            );

            // Client name
            AddFooterLine(
                contentByte,
                font,
                document,
                $"Submitted To: {clientName}",
                ProjectInfoStartY + (LineSpacing * 2)
            );
        }

        private void AddFooterLine(PdfContentByte contentByte, Font font, Document document, string text, int yOffset)
        {
            ColumnText.ShowTextAligned(
                contentByte,
                Element.ALIGN_LEFT,
                new Phrase(text, font),
                document.LeftMargin,
                document.Bottom - yOffset,
                0
            );
        }

        private string GetTotalPageCount(PdfWriter writer)
        {
            // This is a placeholder - in a real implementation you'd need to track
            // or calculate the total number of pages
            return "3"; // Currently hardcoded as in the original
        }
    }

}