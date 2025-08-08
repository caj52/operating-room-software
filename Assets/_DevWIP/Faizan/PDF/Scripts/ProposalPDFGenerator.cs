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
        // Cancel any existing generation
        if (pdfGenerationCoroutine != null)
        {
            StopCoroutine(pdfGenerationCoroutine);
        }

        isCancelled = false;
        pdfGenerationCoroutine = StartCoroutine(GeneratePDFCoroutine());
    }

    private IEnumerator GeneratePDFCoroutine()
    {
        path = null;
        bool hasError = false;
        string errorMessage = "";
        string filePath = "";

        // Show loading screen with cancel option
        UI_GeneralLoadingScreen.instance.ShowLoadingScreen();
        UI_GeneralLoadingScreen.instance.OnCancel += HandleCancellation;
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
        string fileName = $"SalesProposal_{Guid.NewGuid()}.pdf";
        filePath = Path.Combine(Application.persistentDataPath, fileName);

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
        // Clean up
        UI_GeneralLoadingScreen.instance.OnCancel -= HandleCancellation;
        UI_GeneralLoadingScreen.instance.HideLoadingScreen();
        pdfGenerationCoroutine = null;

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
                $"PDF generated successfully!\n\nFile saved to:\n{filePath}",
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

    private void HandleCancellation()
    {
        isCancelled = true;
        Debug.Log("PDF generation cancelled by user");
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
        titleFont = new Font(baseFont, 16, Font.BOLD);
        headerFont = new Font(baseFont, 12, Font.BOLD);
        normalFont = new Font(baseFont, 10, Font.NORMAL);
        smallFont = new Font(baseFont, 8, Font.NORMAL);
        boldFont = new Font(baseFont, 10, Font.BOLD);
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
         //   string configTitle = $"Configuration {configCount++}: {first.objectPricingData.ObjectName}";


            var lights = group.Where(sp => !sp.isBoomObject).ToList();
            var booms = group.Where(sp => sp.isBoomObject).ToList();
            string configTitle;

            if (lights.Count > 0 && booms.Count > 0)
                configTitle = "\tConfiguration Name: Tandem Equipment Boom with Light";
            else if (lights.Count > 0)
                configTitle = "\tConfiguration Name: Tandem Equipment Light";
            else if (booms.Count > 0)
                configTitle = "\tConfiguration Name: Tandem Equipment Boom";
            else
                configTitle = $"\tConfiguration Name:  {first.objectPricingData.ObjectName} ";

         
            document.Add(Chunk.NEWLINE);
            AddTableTitle(document, configTitle);
            if (lights.Count > 0)
            {
                PdfPTable lightTable = CreateModelTable("MODEL DESCRIPTION", "QTY", "LIGHT", lights.Count.ToString());
                document.Add(lightTable);
                AddHorizontalLine(document);

                AddSectionHeader(document, "OPTION/ACCESSORY DESCRIPTION");

                // 1. Get all current dropdown states
                var allStates =DropdownPopulator.GetAllCurrentStates();

                // 2. Filter by lights
                var lightStates = allStates
                    .Select(state => new
                    {
                        Key = state.Item1.name, // Or use a custom identifier
                        Value = state.Item2.ObjectName // Use the actual property from PriceExcelData
                    })
                    .ToList();

                // 3. Format key-value pairs
                string lightOptionsText = string.Join(", ", lightStates.Select(kv => $"{kv.Key}: {kv.Value}"));

                // 4. Create PDF table
                PdfPTable lightOptions = CreateOptionTable(lightOptionsText);


                AddDropdownItems(lightOptions, false);
              //  document.Add(lightOptions);
                document.Add(Chunk.NEWLINE);
            }

            if (booms.Count > 0)
            {
                PdfPTable boomTable = CreateModelTable("MODEL DESCRIPTION", "QTY", "ARTICULATING BOOM", "1");
                document.Add(boomTable);
                AddHorizontalLine(document);

                AddSectionHeader(document, "OPTION/ACCESSORY DESCRIPTION");

                // Boom-related options only
                var btNamesGroupedBoom = GetBtNamesGroupedByType(booms);
                string boomOptionsText = string.Join(", ", btNamesGroupedBoom["Boom"].Distinct());
                PdfPTable boomOptions = CreateOptionTable(boomOptionsText);
                AddDropdownItems(boomOptions, true);
                document.Add(boomOptions);
                AddHorizontalLine(document);
            }

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


    private void GenerateSecondPage(Document document, PdfWriter pdfWriter)
    {
        AddCompanyHeader(document);

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

            AddImageToPDF(document, path, pdfWriter, imageHeight);
            if (imageData.Count > 0)
                AddImageToPDF(document, imageData[0].Path, pdfWriter, imageHeight);
        }
    }

    private void GeneratePricingPage(Document document, PdfWriter writer)
    {
        AddCompanyHeader(document);

        // 1. Get all root-level configurations
        var rootConfigs = selectablePrices
            .Where(sp => sp.objectPricingData != null)
            .GroupBy(sp => sp.transform.root) // Root of each configuration group
            .ToList();

        int configNumber = 1;

        foreach (var configGroup in rootConfigs)
        {
            var firstSp = configGroup.FirstOrDefault();
            string configTitle = $"Configuration {configNumber++}: {firstSp?.objectPricingData?.ObjectName ?? "Unnamed Configuration"}";
            AddTableTitle(document, configTitle);

            PdfPTable configTable = new PdfPTable(5);
            configTable.WidthPercentage = 100;
            configTable.SetWidths(new float[] { 2, 5, 1, 2, 2 });

            AddRowToTable(configTable, "MODEL DESCRIPTION", BaseColor.WHITE, BaseColor.BLACK, PdfPCell.NO_BORDER);

            double subtotal = 0;

            // Group light components by name/partNumber for aggregation
            var lightGroups = configGroup
                .Where(sp => sp.objectPricingData != null &&
                             sp.objectPricingData.ObjectName.ToLower().Contains("light"))
                .GroupBy(sp => new
                {
                    sp.objectPricingData.PartNumber,
                    Name = string.IsNullOrEmpty(sp.objectPricingData.ObjectSize)
                        ? sp.objectPricingData.ObjectName
                        : $"{sp.objectPricingData.ObjectSize} {sp.objectPricingData.ObjectName}",
                    UnitPrice = sp.objectPricingData.ListPrice +
                               (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0)
                });

            // Add lights as aggregated rows
            foreach (var lightGroup in lightGroups)
            {
                int quantity = lightGroup.Count();
                double unitPrice = lightGroup.Key.UnitPrice;
                double extPrice = unitPrice * quantity;

                // Try to get UI data from any item in the group that has it
                var itemWithUI = lightGroup.FirstOrDefault(sp => sp.UIRefPricingRowDataFill != null);

                if (itemWithUI?.UIRefPricingRowDataFill != null)
                {
                    // Use UI data if available
                    var pricingRowDataFill = itemWithUI.UIRefPricingRowDataFill;
                    configTable.AddCell(CreateLeftAlignedCell(pricingRowDataFill.partNo.text ?? "N/A", normalFont));
                    configTable.AddCell(CreateLeftAlignedCell(lightGroup.Key.Name, normalFont));
                    configTable.AddCell(CreateCenteredCell(quantity.ToString(), normalFont));
                    configTable.AddCell(CreateRightAlignedCell(unitPrice.ToString("C"), normalFont));
                    configTable.AddCell(CreateRightAlignedCell(pricingRowDataFill.Price.ToString("C"), normalFont));
                    subtotal += pricingRowDataFill.Price;
                }
                else
                {
                    // Fallback to pricing data if UI not available
                    configTable.AddCell(CreateLeftAlignedCell(lightGroup.Key.PartNumber ?? "N/A", normalFont));
                    configTable.AddCell(CreateLeftAlignedCell(lightGroup.Key.Name, normalFont));
                    configTable.AddCell(CreateCenteredCell(quantity.ToString(), normalFont));
                    configTable.AddCell(CreateRightAlignedCell(unitPrice.ToString("C"), normalFont));
                    configTable.AddCell(CreateRightAlignedCell(extPrice.ToString("C"), normalFont));
                    subtotal += extPrice;
                }
            }

            // Add all non-light components as individual rows
            var nonLightItems = configGroup
                .Where(sp => sp.objectPricingData != null &&
                             !sp.objectPricingData.ObjectName.ToLower().Contains("light"))
                .Reverse()
                .ToList();

            foreach (var sp in nonLightItems)
            {
                var data = sp.objectPricingData;
                string partNumber = string.IsNullOrEmpty(data.PartNumber) ? "N/A" : data.PartNumber;
                string objectName = string.IsNullOrEmpty(data.ObjectSize)
                    ? data.ObjectName
                    : $"{data.ObjectSize} {data.ObjectName}";
                double price = data.ListPrice + (data.isSimFlexArmAvailable ? data.SimFlexPrice : 0);

                if (sp.UIRefPricingRowDataFill != null)
                {
                    // Use UI data if available
                    var pricingRowDataFill = sp.UIRefPricingRowDataFill;
                    configTable.AddCell(CreateLeftAlignedCell(pricingRowDataFill.partNo.text, normalFont));
                    configTable.AddCell(CreateLeftAlignedCell(pricingRowDataFill.modelName.text, normalFont));
                    configTable.AddCell(CreateCenteredCell("1", normalFont));
                    configTable.AddCell(CreateRightAlignedCell(pricingRowDataFill.listPrice.text, normalFont));
                    configTable.AddCell(CreateRightAlignedCell(pricingRowDataFill.Price.ToString("C"), normalFont));
                    subtotal += pricingRowDataFill.Price;
                }
                else
                {
                    // Fallback to pricing data if UI not available
                    configTable.AddCell(CreateLeftAlignedCell(partNumber, normalFont));
                    configTable.AddCell(CreateLeftAlignedCell(objectName, normalFont));
                    configTable.AddCell(CreateCenteredCell("1", normalFont));
                    configTable.AddCell(CreateRightAlignedCell(price.ToString("C"), normalFont));
                    configTable.AddCell(CreateRightAlignedCell(price.ToString("C"), normalFont));
                    subtotal += price;
                }
            }

            // Add subtotal row
            PdfPCell subtotalLabel = new PdfPCell(new Phrase("Subtotal", boldFont))
            {
                Colspan = 4,
                Border = Rectangle.TOP_BORDER,
                HorizontalAlignment = Element.ALIGN_RIGHT,
                PaddingTop = 5
            };
            PdfPCell subtotalValue = CreateRightAlignedCell(subtotal.ToString("C"), boldFont);
            subtotalValue.Border = Rectangle.TOP_BORDER;

            configTable.AddCell(subtotalLabel);
            configTable.AddCell(subtotalValue);

            document.Add(configTable);
            document.Add(Chunk.NEWLINE);
        }

        // Charges
        PdfPTable misc = new PdfPTable(5);
        misc.WidthPercentage = 100;
        misc.SetWidths(new float[] { 2, 5, 1, 2, 2 });

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
        leftCell.AddElement(new Paragraph($"Sales Rep: {salesRepName}",titleFont));
        leftCell.AddElement(new Paragraph($"{salesRepEmail}",normalFont));
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
                logo.ScaleAbsolute(150, 75);
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
        document.Add(new Paragraph("\n",normalFont));
        document.Add(new Paragraph($"Project: {projectName}", new Font(titleFont.BaseFont, titleFont.Size, Font.UNDERLINE)));
    }

    private void AddTableTitle(Document doc, string text)
    {
        Paragraph tableTitle = new Paragraph("\t"+text, headerFont);
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
        Debug.LogError("++"+content);
        PdfPTable table = new PdfPTable(2);
        table.WidthPercentage = 100;
        table.SetWidths(new float[] { 8, 1 });

        table.AddCell(CreateLeftAlignedCell(content, normalFont));
        table.AddCell(CreateCenteredCell("", normalFont));

        return table;
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

            double price = data.ListPrice + (data.isSimFlexArmAvailable ? data.SimFlexPrice : 0);

            table.AddCell(CreateLeftAlignedCell(partNumber, normalFont));
            table.AddCell(CreateLeftAlignedCell(name, normalFont));
            table.AddCell(CreateCenteredCell("1", normalFont));
            table.AddCell(CreateRightAlignedCell(price.ToString("C"), normalFont));
            table.AddCell(CreateRightAlignedCell(price.ToString("C"), normalFont));
        }
    }

    private void AddDropdownItems(PdfPTable table, bool isBoom)
    {
        foreach (var state in DropdownPopulator.GetAllCurrentStates())
        {
            if (state.Item1.isBoomExcelFileDropDown != isBoom) continue;

            var data = state.Item2;
            if (data.ListPrice == 0) continue;

            table.AddCell(CreateLeftAlignedCell(data.PartNumber ?? "N/A", normalFont));
            table.AddCell(CreateLeftAlignedCell(data.ObjectName, normalFont));
            table.AddCell(CreateCenteredCell("1", normalFont));
            table.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont));
            table.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont));
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
        table.AddCell(CreateLeftAlignedCell(data.PartNumber ?? "N/A", normalFont));
        table.AddCell(CreateLeftAlignedCell(data.ObjectName, normalFont));
        table.AddCell(CreateCenteredCell("1", normalFont));
        table.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont));
        table.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont));
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

        // Safe access to UI_ClientMetaData
        string accountName = GetClientMetaDataSafely("AccountName");
        string addressLine1 = GetClientMetaDataSafely("AccountAddressLine1");
        string addressLine2 = GetClientMetaDataSafely("AccountAddressLine2");
        string projectName = GetClientMetaDataSafely("ProjectName");
        string projectNumber = GetClientMetaDataSafely("ProjectNumber");
        string orderRef = GetClientMetaDataSafely("OrderReferenceNumber");

        AddRowWithBottomBorder(inner, "Account Name: " + accountName, normalFont);
        AddRowWithBottomBorder(inner, "Account Address: " + addressLine1, normalFont);
        AddRowWithBottomBorder(inner, " " + addressLine2, normalFont);
        AddRowWithBottomBorder(inner, "Project Name: " + projectName, normalFont);
        AddRowWithBottomBorder(inner, "Project Number: " + projectNumber, normalFont);
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
        return new PdfPCell(new Phrase(text, font)) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_LEFT };
    }

    private PdfPCell CreateRightAlignedCell(string text, Font font)
    {
        return new PdfPCell(new Phrase(text, font)) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT };
    }

    private PdfPCell CreateCenteredCell(string text, Font font)
    {
        return new PdfPCell(new Phrase(text, font)) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_CENTER };
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

        double dropdownTotal = DropdownPopulator.GetAllCurrentStates().Sum(state => state.Item2.ListPrice);
        Debug.LogError("+++dropdownTotal" + dropdownTotal);
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

        //public override void OnStartPage(PdfWriter writer, Document document)
        //{
        //    // Track the total number of pages
        //    totalPageCount = writer.PageNumber;
        //}
        public override void OnStartPage(PdfWriter writer, Document document)
        {
            // Track the total number of pages
            totalPageCount = writer.PageNumber;
            // Only add header if it's not the very first page
            //if (document.PageSize.Height != 0)
            //{
            //    Debug.Log("Adding header to page: " + writer.PageNumber);
            //    FindAnyObjectByType<ProposalPDFGenerator>().AddCompanyHeader(document);
            //}


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