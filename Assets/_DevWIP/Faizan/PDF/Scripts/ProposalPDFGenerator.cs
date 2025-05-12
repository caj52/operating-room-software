using UnityEngine;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Font = iTextSharp.text.Font;
using System.Linq;
using System;
using System.Collections.Generic;
using iTextSharp.text.pdf.draw;
using System.Collections;

public class ProposalPDFGenerator : MonoBehaviour
{
    #region Properties and Fields

    // Company details
    private readonly string companyName = "Imagine Unlimited";
    private readonly string companyAddress = "9155 Sterling St Suite 120";
    private readonly string companyCity = "Irving, TX 75063";
    private readonly string companyPhone = "1 877 789 8106";
    private readonly string companyFax = "1 408 754 2969";

    // Client details
    public string clientName = "Client Name";
    public string projectName = "Project Name";
    public string configName = "Tandem Equipment Boom with Light";
    public string salesRepName = "Sales Rep Name";
    public string salesRepEmail = "SalesRepEmail@igoimagine.com";

    public string accountName = "";
    public string accountAddress = "";
    public string projectNumber = "Project Name";
    public string referenceNumber = "";

    // Notes for last page
    public string note1 = "35% deposit. Progress billing to completion. Sales tax to be added to invoices in applicable";
    public string note2 = "Quote is valid for 90 days from creation date.";
    public string note3 = "Customer Acceptance and Configuration Acknowledgement.";

    public float discountPercentage;

    // Fonts
    private Font titleFont;
    private Font headerFont;
    private Font normalFont;
    private Font smallFont;
    private Font boldFont;
    private Font notesFont;
    private Font tableHeaderFont;
    private Font tableTitleFont;

    // Colors
    private readonly BaseColor headerColor = new BaseColor(220, 220, 220);
    private readonly BaseColor lightGrayColor = new BaseColor(240, 240, 240);
    private readonly BaseColor darkBlue = new BaseColor(0, 18, 54);

    // Data fields
    private ScreenshotCapture screenshot;
    private SelectablePrice[] selectablePrices;
    private Selectable[] selectables;
    private string path = "";

    // Cached data
    private string concatedStrBoomObjectsName;
    private double boomTotalPrice;
    private string concatedStrNonBoomObjectsName;
    private double nonBoomTotalPrice;
    private string contactedDdboomObjects;
    private double boomTotalPriceDd;
    private string contactedDdnonBoomObjects;
    private double nonBoomTotalPriceDd;
    private int nonBoomObjectsCount = 0;
    private int boomObjectsCount = 0;
    string concatedStrNonBoomObjectsPartName;

    #endregion

    private void Start()
    {
        screenshot = FindObjectOfType<ScreenshotCapture>();
    }

    public void GeneratePDF()
    {
        StartCoroutine(GeneratePDFCoroutine());
    }

    #region PDF Generation

    private IEnumerator GeneratePDFCoroutine()
    {
       // try
        {
            path = null; // Reset path

            // Capture ceiling screenshot
            yield return StartCoroutine(
                screenshot.CaptureCeilingOnly(
                    RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).transform.position + new Vector3(0, 10, 0),
                    null,
                    (resultPath) => path = resultPath
                )
            );

            yield return new WaitUntil(() => !string.IsNullOrEmpty(path));
            yield return new WaitUntil(() => File.Exists(path));

            // Create PDF file
            string fileName = string.Format(@"SalesProposal_{0}.pdf", Guid.NewGuid());
            string filePath = Path.Combine(Application.persistentDataPath, fileName);

            using (Document document = new Document(PageSize.A4, 18, 18, 18, 18))
            using (PdfWriter writer = PdfWriter.GetInstance(document, new FileStream(filePath, FileMode.Create)))
            {
                // Setup page event for headers/footers
                PageEventHelper pageEvent = new PageEventHelper
                {
                    clientName = clientName,
                    projectName = projectName,
                    configName = configName
                };
                writer.PageEvent = pageEvent;

                document.Open();

                // Initialize fonts
                SetupFonts();

                // Prepare quote data
                PrepareQuoteData();

                // Generate all pages
                GenerateFirstPage(document);
                document.NewPage();
                GenerateSecondPage(document, writer);
                document.NewPage();
                GeneratePricingPage(document, writer);
                document.Close();
            }

            Application.OpenURL(filePath);
            string message = "PDF created at: " + filePath;
            Debug.Log(message);
            UI_DialogPrompt.Open(message);
        }
      /*  catch (Exception e)
        {
            string message = "Error creating PDF: " + e.Message + "\n" + e.StackTrace;
            Debug.LogError(message);
            UI_DialogPrompt.Open(message);
        }*/
    }

    private void SetupFonts()
    {
        // Base font for embedded usage
        BaseFont baseFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.EMBEDDED);

        titleFont = new Font(baseFont, 16, Font.BOLD);
        headerFont = new Font(baseFont, 12, Font.BOLD);
        normalFont = new Font(baseFont, 10, Font.NORMAL);
        smallFont = new Font(baseFont, 8, Font.NORMAL);
        boldFont = new Font(baseFont, 10, Font.BOLD);
        notesFont = new Font(baseFont, 8, Font.ITALIC);
        tableHeaderFont = new Font(baseFont, normalFont.Size, Font.NORMAL, new BaseColor(255, 255, 255));
        tableTitleFont = new Font(baseFont, normalFont.Size, Font.BOLD, new BaseColor(168, 168, 168));
    }

    private void PrepareQuoteData()
    {
        // Get all selectable prices and their associated selectables
        selectablePrices = FindObjectsOfType<SelectablePrice>();
        selectables = new Selectable[selectablePrices.Length];

        for (int i = 0; i < selectablePrices.Length; i++)
        {
            selectables[i] = selectablePrices[i].transform.root.GetComponentInChildren<Selectable>();
        }

        //// Generate concatenated names for boom objects
        //concatedStrBoomObjectsName = string.Join(", ", selectablePrices
        //    .Where(sp => sp.isBoomObject && sp.objectPricingData != null)
        //    .Select(sp => string.IsNullOrEmpty(sp.objectPricingData.ObjectSize)
        //        ? sp.objectPricingData.ObjectName
        //        : $"{sp.objectPricingData.ObjectName} {sp.objectPricingData.ObjectSize}".Trim()));

        // Generate concatenated names for boom objects, ordered by hierarchy
        /* concatedStrBoomObjectsName = string.Join(", ", selectablePrices
             .Where(sp => sp.isBoomObject && sp.objectPricingData != null)
             .OrderBy(sp => sp.transform.GetSiblingIndex()) // Order by hierarchy
             .Select(sp => string.IsNullOrEmpty(sp.objectPricingData.ObjectSize)
                 ? sp.objectPricingData.ObjectName
                 : $"{sp.objectPricingData.ObjectName} {sp.objectPricingData.ObjectSize}".Trim()));*/

        concatedStrBoomObjectsName = string.Join(", ", selectablePrices
    .Where(sp => sp.isBoomObject && sp.objectPricingData != null)
    .OrderBy(sp => GetHierarchyPath(sp.transform)) // Order by full hierarchy path
    .Select(sp => string.IsNullOrEmpty(sp.objectPricingData.ObjectSize)
        ? sp.objectPricingData.ObjectName
        : $"{sp.objectPricingData.ObjectSize} {sp.objectPricingData.ObjectName}".Trim()));

        // Calculate boom objects total price
        boomTotalPrice = selectablePrices
            .Where(sp => sp.isBoomObject)
            .Sum(sp => sp.objectPricingData.ListPrice +
                  (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0));

        // Generate concatenated names for non-boom objects
        concatedStrNonBoomObjectsName = string.Join(", ", selectablePrices
            .Where(sp => !sp.isBoomObject)
            .Select(sp => sp.objectPricingData.ObjectName));

        // Calculate non-boom objects total price
        nonBoomTotalPrice = selectablePrices
            .Where(sp => !sp.isBoomObject)
            .Sum(sp => sp.objectPricingData.ListPrice +
                  (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0));


        // Concatenate btName values for all non-boom SelectablePrice instances
        concatedStrNonBoomObjectsPartName = string.Join(", ", selectablePrices
            .Where(sp => !sp.isBoomObject) // Filter only non-boom objects
            .Select(sp => ConcatenateBtNamesFromSelectablePrice(sp))
            .Where(btName => !string.IsNullOrEmpty(btName))); // Filter out empty results

        Debug.Log($"Concatenated btNames: {concatedStrNonBoomObjectsPartName}");

        // Get dropdown data
        (contactedDdboomObjects, boomTotalPriceDd, contactedDdnonBoomObjects, nonBoomTotalPriceDd) = GetSelectedBoomAndNonBoomData();

        // Count objects
        nonBoomObjectsCount = selectablePrices.Count(sp => !sp.isBoomObject);

        //Count all Boom Objects
        //boomObjectsCount = selectablePrices.Count(sp => sp.isBoomObject);

        // Count boom objects that are base models
        boomObjectsCount = selectablePrices.Count(sp => sp.isBoomObject && UINameToExcelKey.IsBoomBaseModelFromExcel(sp.pricingObjectName));
    }
    /// <summary>
    /// Get a string representation of the hierarchy path for sorting.
    /// </summary>
    /// <param name="transform">The transform to calculate the path for.</param>
    /// <returns>A string representing the hierarchy path.</returns>
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
    private string ConcatenateBtNamesFromSelectablePrice(SelectablePrice selectablePrice)
    {
        // Get the root parent of the SelectablePrice
        GameObject rootObject = selectablePrice.transform.root.gameObject;

        // Check if the root has the RecordHirarcheySelectables component
        RecordHirarcheySelectables hierarchyScript = rootObject.GetComponent<RecordHirarcheySelectables>();
        if (hierarchyScript == null)
        {
            Debug.LogError("RecordHirarcheySelectables script not found on root object.");
            return string.Empty;
        }

        // Determine which list the SelectablePrice belongs to
        var attachedList = hierarchyScript.attachedSelectables
            .FirstOrDefault(a => a.gameObject == selectablePrice.gameObject) != null
            ? hierarchyScript.attachedSelectables
            : hierarchyScript.attachedSelectables2;

        // Concatenate the btName values from the selected list
        string concatenatedNames = string.Join(", ", attachedList.Select(a =>
        {
            string btName = a.btName;

            // Get the Selectable component
            Selectable selectable = a.gameObject.GetComponent<Selectable>();
            if (selectable == null)
            {
                return btName; // If no Selectable component, return only btName
            }

            float size = selectable.CurrentPreviewScaleLevel?.Size ?? 0f;
            if (size > 0)
            {
                return $"{btName} ({size * 1000}mm)"; // Assuming Size is the property for scale
            }
            // Check the relatedSelectable list for scale levels
            string scaleLevel = string.Empty;
            foreach (var related in selectable.RelatedSelectables)
            {
                if (related.ScaleLevels != null && related.ScaleLevels.Count > 0)
                {
                    // Find the selected scale level (assuming a property like IsSelected exists)
                    var selectedScale = related.ScaleLevels.FirstOrDefault(scale => scale.Selected);
                    if (selectedScale != null)
                    {
                        scaleLevel = (selectedScale.Size * 1000) +"mm"; // Assuming Size is the property for scale
                        break;
                    }
                }
            }

            // Concatenate btName with scale level if found
            return string.IsNullOrEmpty(scaleLevel) ? btName : $"{btName} {scaleLevel}";
        }));

        return concatenatedNames;
    }


    #endregion

    #region Page Generation

    private void TableTitle(Document document)
    {
        Paragraph tableTitle = new Paragraph(" Configuration 1: Tandem Equipment Boom with Light ", tableTitleFont);
        tableTitle.Alignment = Element.ALIGN_LEFT;
        tableTitle.SpacingAfter = 10;
        document.Add(tableTitle);
    }

    private void GenerateFirstPage(Document document)
    {
        AddCompanyHeader(document);
        document.Add(Chunk.NEWLINE);

        TableTitle(document);

        // Equipment details table
        PdfPTable modelTable = new PdfPTable(2);
        modelTable.WidthPercentage = 100;
        modelTable.SetWidths(new float[] { 8, 1 });

        // MODEL DESCRIPTION row
        AddHeaderRow(modelTable, "MODEL DESCRIPTION", "QTY");

        // LIGHT row
        PdfPCell cell = new PdfPCell(new Phrase("LIGHT", normalFont));
        cell.Border = Rectangle.NO_BORDER;
        modelTable.AddCell(cell);

        cell = new PdfPCell(new Phrase(nonBoomObjectsCount.ToString(), normalFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        modelTable.AddCell(cell);

        document.Add(modelTable);

        // Add a horizontal line after the "LIGHT" row
        AddHorizontalLine(document);

        // OPTION/ACCESSORY DESCRIPTION with gray background
        AddSectionHeader(document, "OPTION/ACCESSORY DESCRIPTION");

        // Options table
        PdfPTable optionTable = new PdfPTable(2);
        optionTable.WidthPercentage = 100;
        optionTable.SetWidths(new float[] { 8, 1 });

        // Light objects
        string selectableNames = string.IsNullOrEmpty(concatedStrNonBoomObjectsName) ? contactedDdnonBoomObjects :
            $"{concatedStrNonBoomObjectsName}, {contactedDdnonBoomObjects}";
        Debug.Log($" selectableNames {selectableNames}");
        selectableNames = string.IsNullOrEmpty(concatedStrNonBoomObjectsPartName) ? selectableNames :
            $"{selectableNames}, {concatedStrNonBoomObjectsPartName}";
        Debug.Log($" after selectableNames {selectableNames}");
        Debug.Log($" after concatedStrNonBoomObjectsPartName {concatedStrNonBoomObjectsPartName}");

        cell = new PdfPCell(new Phrase(selectableNames, normalFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.Padding = 5;
        optionTable.AddCell(cell);

        //cell = new PdfPCell(new Phrase((6 + nonBoomObjectsCount).ToString(), normalFont));
        cell = new PdfPCell(new Phrase("", normalFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        optionTable.AddCell(cell);

        document.Add(optionTable);
        document.Add(Chunk.NEWLINE);

        // Second MODEL DESCRIPTION row
        PdfPTable model2Table = new PdfPTable(2);
        model2Table.WidthPercentage = 100;
        model2Table.SetWidths(new float[] { 8, 1 });

        AddHeaderRow(model2Table, "MODEL DESCRIPTION", "QTY");

        // ARTICULATING BOOM row
        string articulatingBoomText = "";
        if (boomObjectsCount > 0)
        {
            articulatingBoomText = "ARTICULATING BOOM ";

        }
     

        cell = new PdfPCell(new Phrase(articulatingBoomText, normalFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.Padding = 5;
        model2Table.AddCell(cell);

        cell = new PdfPCell(new Phrase((boomObjectsCount).ToString(), normalFont));//Total boom counter show
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        model2Table.AddCell(cell);

        document.Add(model2Table);

        // Add a horizontal line
        AddHorizontalLine(document);

        // Second OPTION/ACCESSORY DESCRIPTION with gray background
        AddSectionHeader(document, "OPTION/ACCESSORY DESCRIPTION");

        //string boomObjectsText = string.IsNullOrEmpty(concatedStrBoomObjectsName) ?
        // contactedDdboomObjects : $"{concatedStrBoomObjectsName}, {contactedDdboomObjects}";
        // Second MODEL DESCRIPTION row

        string boomObjectsText = string.IsNullOrEmpty(concatedStrBoomObjectsName) ?
         contactedDdboomObjects : $"{concatedStrBoomObjectsName}, {contactedDdboomObjects}";

        PdfPTable model3Table = new PdfPTable(2);
        model3Table.WidthPercentage = 100;
        model3Table.SetWidths(new float[] { 8, 1 });
        cell = new PdfPCell(new Phrase(boomObjectsText, normalFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.Padding = 5;
        model3Table.AddCell(cell);

        cell = new PdfPCell(new Phrase("", normalFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        model3Table.AddCell(cell);

        document.Add(model3Table);

        // Add another horizontal line
        AddHorizontalLine(document);

        // Total price
        PdfPTable totalTable = new PdfPTable(2);
        totalTable.WidthPercentage = 100;
        totalTable.SetWidths(new float[] { 8, 2 });

        cell = new PdfPCell(new Phrase("EQUIPMENT TOTAL LIST PRICE", boldFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.Padding = 5;
        totalTable.AddCell(cell);

        double totalCost = boomTotalPrice + nonBoomTotalPrice + boomTotalPriceDd + nonBoomTotalPriceDd;

        cell = new PdfPCell(new Phrase("$" + totalCost, boldFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_RIGHT;
        cell.Padding = 5;
        totalTable.AddCell(cell);

        document.Add(totalTable);
    }

    private void AddSectionHeader(Document document, string title)
    {
        PdfPTable headerTable = new PdfPTable(1);
        headerTable.WidthPercentage = 100;
        PdfPCell headerCell = new PdfPCell(new Phrase(title, boldFont));
        headerCell.BackgroundColor = lightGrayColor;
        headerCell.Border = Rectangle.NO_BORDER;
        headerCell.Padding = 5;
        headerTable.AddCell(headerCell);
        document.Add(headerTable);
    }

    private void AddHeaderRow(PdfPTable table, string leftHeader, string rightHeader)
    {
        PdfPCell cell1 = new PdfPCell(new Phrase(leftHeader, tableHeaderFont));
        cell1.BackgroundColor = BaseColor.BLACK;// darkBlue;
        cell1.Border = Rectangle.NO_BORDER;
        cell1.Padding = 5;
        table.AddCell(cell1);

        PdfPCell cell2 = new PdfPCell(new Phrase(rightHeader, tableHeaderFont));
        cell2.BackgroundColor = BaseColor.BLACK;// darkBlue;
        cell2.Border = Rectangle.NO_BORDER;
        cell2.HorizontalAlignment = Element.ALIGN_CENTER;
        cell2.Padding = 5;
        table.AddCell(cell2);
    }

    private void AddHorizontalLine(Document document)
    {
        LineSeparator line = new LineSeparator(1f, 100f, BaseColor.BLACK, Element.ALIGN_CENTER, 10);
        document.Add(new Chunk(line));
    }

    private void GenerateSecondPage(Document document, PdfWriter pdfWriter)
    {
        AddCompanyHeader(document);

        HashSet<Selectable> processedRoots = new HashSet<Selectable>();
        float headerHeight = 125f; // Estimated height in points
        float availableHeight = document.PageSize.Height - document.TopMargin - document.BottomMargin - headerHeight;
        // Divide the available height between the two images
        float imageHeight = (availableHeight) / 2.5f;

        for (int i = 0; i < selectables.Length; i++)
        {
            // Get the root parent of the current selectable
            Selectable rootParent = GetRootParent(selectables[i]);

            // Skip if this root parent has already been processed
            if (processedRoots.Contains(rootParent))
            {
                continue;
            }

            // Mark this root parent as processed
            processedRoots.Add(rootParent);

            // Add images to the second page
            List<PdfExporter.PdfImageData> imageDatas = selectables[i].ExportElevationPdf();
            if (i > 0)
            {
                document.NewPage();
                AddCompanyHeader(document);
            }
            AddImageToPDF(document, path, pdfWriter, imageHeight);
            AddImageToPDF(document, imageDatas[0].Path, pdfWriter, imageHeight);
        }
    }

    private void AddImageToPDF(Document document, string imagePath, PdfWriter writer, float availableHeight)
    {
        // Load the image
        Image image = Image.GetInstance(imagePath);

        // Calculate the available width and height for the image
        float availableWidth = document.PageSize.Width - document.LeftMargin - document.RightMargin;

        // Scale the image proportionally to fit within the available space
        image.ScaleToFit(availableWidth, availableHeight);

        // Center the image horizontally
        image.Alignment = Image.ALIGN_CENTER;

        // Add the image to the document
        document.Add(image);
    }

    private void AddImageToPDF(Document document, string imagePath, PdfWriter writer)
    {
        // Load image
        Image image = Image.GetInstance(imagePath);

        // Scale image
        image.ScaleToFit(document.PageSize.Width/2, document.PageSize.Height / 2);

        // Set alignment
        image.Alignment = Image.ALIGN_LEFT;

        // Add image to document
        document.Add(image);
    }

    private Selectable GetRootParent(Selectable selectable)
    {
        // Traverse up the hierarchy to find the root parent
        while (selectable.ParentSelectable != null)
        {
            selectable = selectable.ParentSelectable;
        }
        return selectable;
    }

    private void GeneratePricingPage(Document document, PdfWriter writer)
    {
        int currentPage = writer.PageNumber;
        // Add company header
        AddCompanyHeader(document);

        TableTitle(document);

        // Detailed pricing table with part numbers
        PdfPTable detailTable = new PdfPTable(5);
        detailTable.WidthPercentage = 100;
        detailTable.SetWidths(new float[] { 2, 5, 1, 2, 2 });

        // Add table headers
        //AddPricingTableHeaders(detailTable);

        // Add pricing data
        AddTableItems(detailTable, "LIGHTS", true, document);

        AddTableItems(detailTable, "ARTICULATING BOOM", false,document);

        // Add rows for installation and shipping charges
        ExcelReader excelReader = FindObjectOfType<ExcelReader>();
        if (excelReader != null)
        {
            // Installation Lights Charges
            var installationCharges = excelReader.GetInstallationLightsCharges();
            if (installationCharges != null)
            {
                AddRowToTable(detailTable, installationCharges);
            }

            // Shipping Lights Charges
            var shippingCharges = excelReader.GetShippingLightsCharges();
            if (shippingCharges != null)
            {
                AddRowToTable(detailTable, shippingCharges);
            }
        }

        

        document.Add(detailTable);
        // Add the table to the document and handle page overflow
        //AddTableWithPageHandling(document, detailTable);

        int totalLength = (boomObjectsCount * 10)+ (nonBoomObjectsCount * 10);



        // Add totals table
        AddTotalsTable(document);
        document.Add(Chunk.NEWLINE);

        // Add notes
        AddNotes(document);
        document.Add(Chunk.NEWLINE);

        // Add acceptance table
        AddAcceptanceTable(document);
        document.Add(Chunk.NEWLINE);
    }

    private void AddTableWithPageHandling(Document document, PdfPTable table)
    {
        // Check if the table will fit on the current page
        if (!document.IsOpen())
            return;

        // Simulate adding the table to check if it fits
        PdfPTable tempTable = new PdfPTable(table);
        tempTable.SplitLate = false; // Allow splitting rows across pages
        tempTable.SplitRows = true;

        // Add the table to the document
        document.Add(tempTable);

        // If the table overflows, add a new page and header
        if (!tempTable.TotalHeight.Equals(0) && tempTable.TotalHeight > document.PageSize.Height - document.TopMargin - document.BottomMargin)
        {
            document.NewPage();
            AddCompanyHeader(document);
            document.Add(table);
        }
    }


    private void AddPricingTableHeaders(PdfPTable table)
    {
        string[] headers = { "PART #", "MODEL DESCRIPTION", "QTY", "LIST PRICE", "EXT LIST PRICE" };

        foreach (string header in headers)
        {
            PdfPCell headerCell = new PdfPCell(new Phrase(header, boldFont));
            headerCell.BackgroundColor = headerColor;
            headerCell.HorizontalAlignment = Element.ALIGN_CENTER;
            headerCell.VerticalAlignment = Element.ALIGN_MIDDLE;
            headerCell.Padding = 5;
            table.AddCell(headerCell);
        }
    }

    private void AddTotalsTable(Document document)
    {
        PdfPTable totalsTable = new PdfPTable(2);
        totalsTable.WidthPercentage = 100;
        totalsTable.SetWidths(new float[] { 8, 2 });

        

        // Calculate total price
        double totalPrice = CalculateTotalPrice();

        AddCellWithBottomBorder("TOTAL EQUIPMENT LIST PRICE", totalsTable);
        AddCellWithBottomBorder(totalPrice.ToString("C"), totalsTable);

        AddCellWithBottomBorder("DISCOUNT %", totalsTable);
        AddCellWithBottomBorder(discountPercentage.ToString(), totalsTable);

        AddCellWithBottomBorder("GRAND TOTAL PRICE", totalsTable);
            var discountedTotal = totalPrice - ((totalPrice * discountPercentage)/100);//discout calculation
        AddCellWithBottomBorder(discountedTotal.ToString("C"), totalsTable);


        document.Add(totalsTable);
    }

    void AddCellWithBottomBorder(string cellValue, PdfPTable totalsTable)
    {
        PdfPCell cell = new PdfPCell(new Phrase(cellValue, boldFont));
        cell.Border = PdfPCell.BOTTOM_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_RIGHT;
        cell.Padding = 5;
        totalsTable.AddCell(cell);
    }

    private double CalculateTotalPrice()
    {
        // Calculate total price from selectables
        double selectablesPrice = selectablePrices.Sum(sp =>
            sp.objectPricingData.ListPrice +
            (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0));

        // Calculate total price from dropdowns
        double dropdownTotalPrice = 0;
        var dropdownStates = DropdownPopulator.GetAllCurrentStates();

        foreach (var state in dropdownStates)
        {
            var data = state.Item2;
            if (data.ListPrice == 0) continue; // Skip items with no price
            dropdownTotalPrice += data.ListPrice;
        }

        // Add installation and shipping charges
        ExcelReader excelReader = FindObjectOfType<ExcelReader>();
        double installationCharges = excelReader?.GetInstallationLightsCharges()?.ListPrice ?? 0;
        double shippingCharges = excelReader?.GetShippingLightsCharges()?.ListPrice ?? 0;

        return selectablesPrice + dropdownTotalPrice + installationCharges + shippingCharges; 
    }

    private void AddNotes(Document document)
    {
        Paragraph notesTitle = new Paragraph("Notes:", boldFont);
        document.Add(notesTitle);
        document.Add(Chunk.NEWLINE);

        Paragraph notes1 = new Paragraph(note1, boldFont);
        document.Add(notes1);
        document.Add(Chunk.NEWLINE);

        Paragraph notes2 = new Paragraph(note2, notesFont);
        document.Add(notes2);
        document.Add(Chunk.NEWLINE);
    }

    private void AddRowWithBottomBorder(PdfPTable table, string text, Font font, int borderStyle)
    {
        PdfPCell cell = new PdfPCell(new Phrase(text, font));
        cell.Border = borderStyle; // Only bottom border
        cell.PaddingBottom = 5; // Add some padding for spacing
        //cell.BorderWidthBottom = 1f; // Thickness of the bottom border
        cell.HorizontalAlignment = Element.ALIGN_LEFT; // Align text to the left
        table.AddCell(cell);
    }

    private void AddAcceptanceTable(Document document)
    {
        PdfPTable acceptanceTable = new PdfPTable(2);
        acceptanceTable.WidthPercentage = 100;
        acceptanceTable.DefaultCell.Border = Rectangle.BOX;
        acceptanceTable.SetWidths(new float[] { 60, 40 });

        // Create a line separator
        var line = new LineSeparator(1f, 100, BaseColor.BLACK, Element.ALIGN_CENTER, -2);
        
        // Right column
        PdfPCell rightCell = new PdfPCell();
        rightCell.Border = Rectangle.BOX;
        rightCell.AddElement(new Paragraph(note3, notesFont));
        rightCell.AddElement(Chunk.NEWLINE);
        rightCell.AddElement(Chunk.NEWLINE);
        rightCell.AddElement(Chunk.NEWLINE);
        rightCell.AddElement(Chunk.NEWLINE);
        rightCell.AddElement(new Paragraph("Signature                                      Date", normalFont));

        // Left column
        PdfPCell leftCell = new PdfPCell();
        leftCell.Border = Rectangle.BOX;
     
        PdfPTable leftInnerTable = new PdfPTable(1);
        leftInnerTable.WidthPercentage = 100;

        // Add rows to the inner table with bottom borders
        AddRowWithBottomBorder(leftInnerTable, "Account Name: "         + UI_ClientMetaData.AccountName, normalFont, PdfPCell.BOTTOM_BORDER);
        AddRowWithBottomBorder(leftInnerTable, "Account Address: "      + UI_ClientMetaData.AccountAddressLine1, normalFont, PdfPCell.BOTTOM_BORDER);
        AddRowWithBottomBorder(leftInnerTable, " "                      + UI_ClientMetaData.AccountAddressLine2, normalFont, PdfPCell.BOTTOM_BORDER);
        AddRowWithBottomBorder(leftInnerTable, "Project Name: "         + UI_ClientMetaData.ProjectName, normalFont, PdfPCell.BOTTOM_BORDER);
        AddRowWithBottomBorder(leftInnerTable, "Project Number: "       + UI_ClientMetaData.ProjectNumber, normalFont, PdfPCell.BOTTOM_BORDER);
        AddRowWithBottomBorder(leftInnerTable, "Order Reference #: "    + UI_ClientMetaData.OrderReferenceNumber, normalFont, PdfPCell.NO_BORDER);

        // Add the inner table to the left cell
        leftCell.AddElement(leftInnerTable);

        acceptanceTable.AddCell(rightCell);
        acceptanceTable.AddCell(leftCell);

        document.Add(acceptanceTable);
    }

    private void AddTableItems(PdfPTable detailTable, string heading, bool isBoomObject, Document document)
    {
        AddRowToTable(detailTable, "MODEL DESCRIPTION", BaseColor.WHITE, BaseColor.BLACK, PdfPCell.NO_BORDER);

        // Add section heading
        //PdfPCell headingCell = new PdfPCell(new Phrase(heading, boldFont));
        //headingCell.Colspan = 5;
        //headingCell.BackgroundColor = headerColor;
        //headingCell.HorizontalAlignment = Element.ALIGN_CENTER;
        //headingCell.Padding = 5;
        //detailTable.AddCell(headingCell);

        // Add selectable items
        AddSelectableItems(detailTable, isBoomObject);

        AddRowToTable(detailTable, "OPTION / ACCESSORY DESCRIPTION", BaseColor.BLACK, BaseColor.GRAY, PdfPCell.NO_BORDER);
        // Add dropdown items
        AddDropdownItems(detailTable, isBoomObject);
    }

    private void AddRowToTable(PdfPTable table, PriceExcelData data)
    {
        table.AddCell(CreateLeftAlignedCell(data.PartNumber, normalFont));
        table.AddCell(CreateLeftAlignedCell(data.ObjectName, normalFont));
        table.AddCell(CreateCenteredCell("1", normalFont)); // Quantity always 1
        table.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont));
        table.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont));
    }

    private void AddRowToTable(PdfPTable table, string title, BaseColor fontColor, BaseColor backgroundColor, int borderStyle)
    {
        // Define the column values
        string[] columnValues = { "PART #", title, "QTY", "LIST PRICE", "EXT LIST PRICE" };

        // Loop through the column values and add cells
        foreach (string value in columnValues)
        {
            PdfPCell cell = new PdfPCell(new Phrase(value, new Font(normalFont.BaseFont, normalFont.Size, normalFont.Style, fontColor)))
            {
                BackgroundColor = backgroundColor,
                Border = borderStyle, // Apply the custom border style
                HorizontalAlignment = value == "QTY" ? Element.ALIGN_CENTER : Element.ALIGN_LEFT, // Center align "QTY", left align others
                //Padding = 5
            };

            // Right-align "LIST PRICE" and "EXT LIST PRICE"
            if (value == "LIST PRICE" || value == "EXT LIST PRICE")
            {
                cell.HorizontalAlignment = Element.ALIGN_RIGHT;
            }
            Debug.Log($"Adding cell with value: {value}");
            table.AddCell(cell);
        }
    }

    private void AddRowToTable(PdfPTable table, string title)
    {
        table.AddCell(new PdfPCell(new Phrase("PART #", normalFont)));
        table.AddCell(new PdfPCell(new Phrase(title, normalFont)));
        table.AddCell(CreateCenteredCell("QTY", normalFont)); // Quantity always 1
        table.AddCell(CreateRightAlignedCell("LIST PRICE", normalFont));
        table.AddCell(CreateRightAlignedCell("EXT LIST PRICE", normalFont));
    }

    //private void AddSelectableItems(PdfPTable detailTable, bool isBoomObject)
    //{
    //    foreach (var selectablePrice in selectablePrices)
    //    {
    //        if (selectablePrice.isBoomObject == isBoomObject) { continue; }

    //        var data = selectablePrice.objectPricingData;
    //        if (data == null)
    //        {
    //            Debug.LogError("No data found for SelectablePrice.", selectablePrice.gameObject);
    //            continue;
    //        }

    //        string partNumber = string.IsNullOrEmpty(data.PartNumber) ? "N/A" : data.PartNumber;

    //        // Add item data to the table
    //        //detailTable.AddCell(new PdfPCell(new Phrase(partNumber, normalFont)));
    //        detailTable.AddCell(CreateLeftAlignedCell(partNumber, normalFont));
    //        string objectName = string.IsNullOrEmpty(data.ObjectSize) ?
    //            data.ObjectName : $"{data.ObjectName} {data.ObjectSize}";
            
    //        detailTable.AddCell(CreateLeftAlignedCell(objectName, normalFont));
    //        detailTable.AddCell(CreateCenteredCell("1", normalFont)); // Quantity always 1

    //        double itemPrice = data.ListPrice + (data.isSimFlexArmAvailable ? data.SimFlexPrice : 0);
    //        detailTable.AddCell(CreateRightAlignedCell(itemPrice.ToString("C"), normalFont));
    //        detailTable.AddCell(CreateRightAlignedCell(itemPrice.ToString("C"), normalFont));
    //        Debug.Log($"Adding item to table: {partNumber}, {objectName}, {itemPrice.ToString("C")}");
    //    }
    //}

    private void AddSelectableItems(PdfPTable detailTable, bool isBoomObject)
    {
        // Group items by name, price, and type
        var groupedItems = selectablePrices
            .Where(sp => sp.isBoomObject == isBoomObject) // Filter by type
            .GroupBy(sp => new
            {
                Name = string.IsNullOrEmpty(sp.objectPricingData.ObjectSize)
                    ? sp.objectPricingData.ObjectName
                    : $"{sp.objectPricingData.ObjectName} {sp.objectPricingData.ObjectSize}",
                Price = sp.objectPricingData.ListPrice +
                        (sp.objectPricingData.isSimFlexArmAvailable ? sp.objectPricingData.SimFlexPrice : 0)
            })
            .Select(group => new
            {
                Name = group.Key.Name,
                Price = group.Key.Price,
                Quantity = group.Count() // Count the number of items in the group
            });

        // Add grouped items to the table
        foreach (var item in groupedItems)
        {
            detailTable.AddCell(CreateLeftAlignedCell("N/A", normalFont)); // Part number (if not available, use "N/A")
            detailTable.AddCell(CreateLeftAlignedCell(item.Name, normalFont)); // Object name
            detailTable.AddCell(CreateCenteredCell(item.Quantity.ToString(), normalFont)); // Quantity
            detailTable.AddCell(CreateRightAlignedCell(item.Price.ToString("C"), normalFont)); // List price
            detailTable.AddCell(CreateRightAlignedCell((item.Price * item.Quantity).ToString("C"), normalFont)); // Extended price
        }
    }


    private void AddDropdownItems(PdfPTable detailTable, bool isBoomObject)
    {
        var dropdownStates = DropdownPopulator.GetAllCurrentStates();

        foreach (var state in dropdownStates)
        {
            if (state.Item1.isBoomExcelFileDropDown == isBoomObject)
                continue;

            var data = state.Item2;
            if (data.ListPrice == 0) continue; // Skip items with no price

            // Add dropdown item data to the table
            PdfPCell cellPartNumber = new PdfPCell(new Phrase(data.PartNumber, normalFont));
            cellPartNumber.Border = Rectangle.NO_BORDER;
            detailTable.AddCell(cellPartNumber);

            PdfPCell cellObjectName = new PdfPCell(new Phrase(data.ObjectName, normalFont));
            cellObjectName.Border = Rectangle.NO_BORDER;
            detailTable.AddCell(cellObjectName);

            detailTable.AddCell(CreateCenteredCell("1", normalFont)); // Quantity always 1
            detailTable.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont));
            detailTable.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont));
            Debug.Log($"Adding dropdown item to table: {data.PartNumber}, {data.ObjectName}, {data.ListPrice.ToString("C")}");
        }
    }

    private void AddCompanyHeader(Document document)
    {
        PdfPTable headerTable = new PdfPTable(2);
        headerTable.WidthPercentage = 100;
        headerTable.DefaultCell.Border = Rectangle.NO_BORDER;
        headerTable.SetWidths(new float[] { 70, 30 });

        // Left cell: Company information
        PdfPCell leftCell = new PdfPCell();
        leftCell.Border = Rectangle.NO_BORDER;

        // Proposal paragraph
        Paragraph proposalParagraph = new Paragraph("Proposal", titleFont);
        proposalParagraph.Alignment = Element.ALIGN_LEFT;
        proposalParagraph.SpacingAfter = 10;
        leftCell.AddElement(proposalParagraph);

        // Sales Rep paragraph
        Paragraph salesRepParagraph = new Paragraph($"Sales Rep: {salesRepName}\n{salesRepEmail}", normalFont);
        salesRepParagraph.Alignment = Element.ALIGN_LEFT;
        salesRepParagraph.SpacingAfter = 10;
        leftCell.AddElement(salesRepParagraph);

        // Company header information
        leftCell.AddElement(new Paragraph(companyName, headerFont));
        leftCell.AddElement(new Paragraph(companyAddress, normalFont));
        leftCell.AddElement(new Paragraph(companyCity, normalFont));
        leftCell.AddElement(new Paragraph($"Tel: {companyPhone} Fax: {companyFax}", normalFont));

        headerTable.AddCell(leftCell);

        // Right cell: Logo
        PdfPCell rightCell = new PdfPCell();
        rightCell.Border = Rectangle.NO_BORDER;
        rightCell.HorizontalAlignment = Element.ALIGN_RIGHT;

        Image logo = Image.GetInstance(Application.streamingAssetsPath + "/Data/quotes/UImagineUnlimited-logo.png");
        logo.ScaleAbsolute(150, 75);
        rightCell.AddElement(logo);

        headerTable.AddCell(rightCell);

        document.Add(headerTable);
        document.Add(Chunk.NEWLINE);

        // "Submitted To" paragraph
        PdfPCell submittedToCell = new PdfPCell(new Phrase($"Submitted To: {clientName}", boldFont));
        submittedToCell.BackgroundColor = lightGrayColor;
        submittedToCell.Border = Rectangle.NO_BORDER;
        submittedToCell.Padding = 5;

        PdfPTable submittedToTable = new PdfPTable(1);
        submittedToTable.WidthPercentage = 100;
        submittedToTable.AddCell(submittedToCell);
        document.Add(submittedToTable);

        // "Project" paragraph with underline
        Font underlineFont = new Font(normalFont);
        underlineFont.SetStyle(Font.UNDERLINE);
        Paragraph projectTo = new Paragraph($"Project: {projectName}", underlineFont);
        projectTo.Alignment = Element.ALIGN_LEFT;
        projectTo.SpacingAfter = 10;
        document.Add(projectTo);
    }

    #endregion

    #region Helper Methods

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

    private PdfPCell CreateCenteredCell(string text, Font font)
    {
        PdfPCell cell = new PdfPCell(new Phrase(text, font));
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        return cell;
    }

    private PdfPCell CreateRightAlignedCell(string text, Font font)
    {
        PdfPCell cell = new PdfPCell(new Phrase(text, font));
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_RIGHT;
        return cell;
    }

    private PdfPCell CreateLeftAlignedCell(string text, Font font)
    {
        PdfPCell cell = new PdfPCell(new Phrase(text, font));
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_LEFT;
        return cell;
    }

    #endregion

    #region Page Event Helper Class

    // Page event helper for headers and footers
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

    #endregion
}