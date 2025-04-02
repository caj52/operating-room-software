using UnityEngine;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Font = iTextSharp.text.Font;
using System.Linq;
using System;
using System.Collections.Generic;
using iTextSharp.text.pdf.draw;
using Unity.Mathematics;
using UnityEngine.Rendering.Universal;

public class ProposalPDFGenerator : MonoBehaviour
{
    // Company details
    private string companyName = "Imagine Unlimited";
    private string companyAddress = "9155 Sterling St Suite 120";
    private string companyCity = "Irving, TX 75063";
    private string companyPhone = "1 877 789 8106";
    private string companyFax = "1 408 754 2969";

    // Client details
    public string clientName = "Client Name";
    public string projectName = "Project Name";
    public string configName = "Tandem Equipment Boom with Light";
    public string salesRepName = "Sales Rep Name";
    public string salesRepEmail = "SalesRepEmail@igoimagine.com";

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
    private BaseColor headerColor = new BaseColor(220, 220, 220);
    private BaseColor lightGrayColor = new BaseColor(240, 240, 240);
    private BaseColor darkBlue = new  BaseColor(0, 18, 54);

    private void Start()
    {
       // GeneratePDF();
    }

    //Find how many light objects are in the scene
    SelectablePrice[] selectablePrices;
    Selectable[] selectables;
    string lighCount;

    void PrepareQuoteData()
    {
        selectablePrices = FindObjectsOfType<SelectablePrice>();
        lighCount = selectablePrices.Length.ToString();
       selectables = new Selectable[selectablePrices.Length];
        for (int i = 0; i < selectablePrices.Length; i++)
        {
            //Get Root Object Selectable Class
            selectables[i] = selectablePrices[i].transform.root.GetComponentInChildren<Selectable>();
        }
    }

    public void GeneratePDF()
    {
        try
        {
            var myUniqueFileName = string.Format(@"SalesProposal_{0}.pdf", Guid.NewGuid());
            string filePath = Path.Combine(Application.persistentDataPath, myUniqueFileName);
             
            Document document = new Document(PageSize.A4, 18, 18, 18, 18); // margins

            PdfWriter writer = PdfWriter.GetInstance(document, new FileStream(filePath, FileMode.Create));

            // Setup page event for headers/footers
            PageEventHelper pageEvent = new PageEventHelper();
            pageEvent.clientName = clientName;
            pageEvent.projectName = projectName;
            pageEvent.configName = configName;
            writer.PageEvent = pageEvent;

            document.Open();

            
            // Initialize fonts
            SetupFonts();

            //pick all the data from the scene which is required to create quote
            PrepareQuoteData();

            // Generate all pages
            GenerateFirstPage(document);
            document.NewPage();
            GenerateSecondPage(document, writer);
            //document.NewPage();
            //GenerateThirdPage(document);
            document.NewPage();
            GeneratePricingPage(document); // New page

            document.Close();
            Debug.Log("PDF created at: " + filePath);
            Application.OpenURL(filePath);

            string message = "PDF created at: " + filePath;
            Debug.Log(message);
            UI_DialogPrompt.Open(message);
        }
        catch (System.Exception e)
        {
            string message = "Error creating PDF: " + e.Message + "\n" + e.StackTrace;
            Debug.LogError(message);
            UI_DialogPrompt.Open(message);
        }
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
        tableHeaderFont = new Font(normalFont.BaseFont, normalFont.Size, Font.NORMAL, new BaseColor(255, 255, 255));

        tableTitleFont = new Font(normalFont.BaseFont, normalFont.Size, Font.BOLD, new BaseColor(168, 168, 168));
    }


    void TableTitle(Document document)
    {
        Paragraph tableTitle = new Paragraph(" Configuration 1: Tandem Equipment Boom with Light ", tableTitleFont);
        tableTitle.Alignment = Element.ALIGN_LEFT;
        tableTitle.SpacingAfter = 10;
        document.Add(tableTitle);
    }

    private void GenerateFirstPage(Document document)
    {
        AddCompanyHeader2(document);
        document.Add(Chunk.NEWLINE);

        TableTitle(document);
       
        // Equipment details table
        PdfPTable modelTable = new PdfPTable(2);
        modelTable.WidthPercentage = 100;
        modelTable.SetWidths(new float[] { 8, 1 });

        // First "MODEL DESCRIPTION" row
        PdfPCell cell1 = new PdfPCell(new Phrase("MODEL DESCRIPTION", tableHeaderFont));
        cell1.BackgroundColor = darkBlue;
        cell1.Border = Rectangle.NO_BORDER;
        cell1.Padding = 5;
        modelTable.AddCell(cell1);

        PdfPCell cell2 = new PdfPCell(new Phrase("QTY", tableHeaderFont));
        cell2.BackgroundColor = darkBlue;
        cell2.Border = Rectangle.NO_BORDER;
        cell2.HorizontalAlignment = Element.ALIGN_CENTER;
        cell2.Padding = 5;
        modelTable.AddCell(cell2);

        // "LIGHT" row
        PdfPCell cell = new PdfPCell(new Phrase("LIGHT", normalFont));
        cell.Border = Rectangle.NO_BORDER;
        //cell.Padding = 5;
        modelTable.AddCell(cell);

        cell = new PdfPCell(new Phrase(lighCount, normalFont));//getting light counts total
        //cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        //cell.Padding = 5;
        modelTable.AddCell(cell);

        document.Add(modelTable);

        // Add a horizontal line after the "LIGHT" row
        LineSeparator line = new LineSeparator(1f, 100f, BaseColor.BLACK, Element.ALIGN_CENTER, 10);
        document.Add(new Chunk(line));

        // "OPTION/ACCESSORY DESCRIPTION" with gray background
        PdfPTable optionDescriptionTable = new PdfPTable(1);
        optionDescriptionTable.WidthPercentage = 100;
        PdfPCell optionDescriptionCell = new PdfPCell(new Phrase("OPTION/ACCESSORY DESCRIPTION", boldFont));
        optionDescriptionCell.BackgroundColor = lightGrayColor;
        optionDescriptionCell.Border = Rectangle.NO_BORDER;
        //optionDescriptionCell.Padding = 5;
        optionDescriptionTable.AddCell(optionDescriptionCell);
        document.Add(optionDescriptionTable);

        PdfPTable optionTable = new PdfPTable(2);
        optionTable.WidthPercentage = 100;
        optionTable.SetWidths(new float[] { 8, 1 });


        string selectableNames = "What to add here?? ";//GetFormattedAssemblySelectables(selectables);// GetAllSelectableNames(selectables);

        cell = new PdfPCell(new Phrase(selectableNames, normalFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.Padding = 5;
        optionTable.AddCell(cell);

        cell = new PdfPCell(new Phrase(selectables.Length.ToString() , normalFont));
        //cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        optionTable.AddCell(cell);

        document.Add(optionTable);
        document.Add(Chunk.NEWLINE);

        // Second "MODEL DESCRIPTION" row
        PdfPTable model2Table = new PdfPTable(2);
        model2Table.WidthPercentage = 100;
        model2Table.SetWidths(new float[] { 8, 1 });

        cell1 = new PdfPCell(new Phrase("MODEL DESCRIPTION", tableHeaderFont));
        cell1.BackgroundColor = darkBlue;
        cell1.Border = Rectangle.NO_BORDER;
        cell1.Padding = 5;
        model2Table.AddCell(cell1);

        cell2 = new PdfPCell(new Phrase("QTY", tableHeaderFont));
        cell2.BackgroundColor = darkBlue;
        cell2.Border = Rectangle.NO_BORDER;
        cell2.HorizontalAlignment = Element.ALIGN_CENTER;
        cell2.Padding = 5;
        model2Table.AddCell(cell2);

        double dropdownItemPrice = 0;
        // Get data from DropdownPopulator and add rows to the table
        var dropdownStates = DropdownPopulator.GetAllCurrentStates();
        var dropdownText = "";
        
        foreach (var state in dropdownStates)
        {
            var data = state.Item2;
            if (data.ListPrice == 0) continue; // Skip items with no price

            dropdownItemPrice += data.ListPrice;

            if(data.isSimFlexArmAvailable)
            {
                dropdownItemPrice += data.SimFlexPrice;
            }
        }

        dropdownText = string.Join(", ", dropdownStates
        .Where(state => state.Item2.ListPrice > 0)
        .Select(state => state.Item2.ObjectName));


        cell = new PdfPCell(new Phrase(dropdownText, normalFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.Padding = 5;
        model2Table.AddCell(cell);

        cell = new PdfPCell(new Phrase(dropdownStates.Count.ToString(), normalFont));
        //cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        model2Table.AddCell(cell);

        document.Add(model2Table);

        // Add a horizontal line after the "ARTICULATING BOOM" row
        document.Add(new Chunk(line));

        // Second "OPTION/ACCESSORY DESCRIPTION" with gray background
        optionDescriptionTable = new PdfPTable(1);
        optionDescriptionTable.WidthPercentage = 100;
        optionDescriptionCell = new PdfPCell(new Phrase("OPTION/ACCESSORY DESCRIPTION", boldFont));
        optionDescriptionCell.BackgroundColor = lightGrayColor;
        optionDescriptionCell.Border = Rectangle.NO_BORDER;
        optionDescriptionCell.Padding = 5;
        optionDescriptionTable.AddCell(optionDescriptionCell);
        document.Add(optionDescriptionTable);

        // Add a horizontal line after the "(5) Gasses" row
        document.Add(new Chunk(line));

        // Total price
        PdfPTable totalTable = new PdfPTable(2);
        totalTable.WidthPercentage = 100;
        totalTable.SetWidths(new float[] { 8, 2 });

        cell = new PdfPCell(new Phrase("EQUIPMENT TOTAL LIST PRICE", boldFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.Padding = 5;
        totalTable.AddCell(cell);

        cell = new PdfPCell(new Phrase("$" + dropdownItemPrice , boldFont));
        cell.Border = Rectangle.NO_BORDER;
        cell.HorizontalAlignment = Element.ALIGN_RIGHT;
        cell.Padding = 5;
        totalTable.AddCell(cell);

        document.Add(totalTable);
    }

    private string GetFormattedAssemblySelectables(Selectable[] selectables)
    {
        List<string> names = new List<string>();

        foreach (var selectable in selectables)
        {
            if (selectable == null || selectable._assemblySelectables == null || selectable._assemblySelectables.Count <= 1)
            {
                continue;
            }

            for (int i = 1; i < selectable._assemblySelectables.Count; i++)
            {
                string name = selectable._assemblySelectables[i].name;
                name = name.Replace("(clone)", "").Replace("(selectable)", "").Trim();
                names.Add(name);
            }
        }

        if (names.Count == 0)
        {
            return string.Empty;
        }

        if (names.Count == 1)
        {
            return names[0];
        }

        string formattedNames = string.Join(", ", names.Take(names.Count - 1)) + " and " + names.Last();
        return formattedNames;
    }

    private string GetAllSelectableNames(Selectable[] selectables)
    {
        List<string> names = new List<string>();

        foreach (var selectable in selectables)
        {
            GetChildSelectableNames(selectable, names);
        }

        return string.Join(", ", names);
    }

    private void GetChildSelectableNames(Selectable selectable, List<string> names)
    {
        if (!string.IsNullOrEmpty(selectable.MetaData.Name))
        {
            names.Add(selectable.MetaData.Name);
        }

        foreach (var child in selectable.GetComponentsInChildren<Selectable>())
        {
            if (child != selectable) // Avoid adding the parent itself
            {
                GetChildSelectableNames(child, names);
            }
        }
    }

    private void GenerateFirstPage_backup(Document document)
    {
        AddCompanyHeader2(document);

        // Equipment details table
        PdfPTable modelTable = new PdfPTable(2);
        modelTable.WidthPercentage = 100;
        modelTable.SetWidths(new float[] { 8, 1 });

        // First "MODEL DESCRIPTION" row
        PdfPCell cell1 = new PdfPCell(new Phrase("MODEL DESCRIPTION", new Font(normalFont.BaseFont, normalFont.Size, Font.BOLD, new BaseColor(255,255,255))));
        cell1.BackgroundColor = darkBlue;
        cell1.Border = Rectangle.BOX;
        cell1.Padding = 5;
        modelTable.AddCell(cell1);

        PdfPCell cell2 = new PdfPCell(new Phrase("QTY", new Font(normalFont.BaseFont, normalFont.Size, Font.BOLD, new BaseColor(255, 255, 255))));
        cell2.BackgroundColor = darkBlue;
        cell2.Border = Rectangle.BOX;
        cell2.HorizontalAlignment = Element.ALIGN_CENTER;
        cell2.Padding = 5;
        modelTable.AddCell(cell2);

        // "LIGHT" row
        PdfPCell cell = new PdfPCell(new Phrase("LIGHT", normalFont));
       // cell.BackgroundColor = lightGrayColor;
        //cell.Border = Rectangle.BOX;
        cell.Padding = 5;
        modelTable.AddCell(cell);

        SelectablePrice[] selectablePrice = FindObjectsOfType<SelectablePrice>();
        string lighCount = selectablePrice.Length.ToString();
        cell = new PdfPCell(new Phrase(lighCount, normalFont));
        cell.BackgroundColor = lightGrayColor;
        //cell.Border = Rectangle.BOX;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        modelTable.AddCell(cell);

        // Add vertical line after "LIGHT" row
        PdfPCell verticalLineCell = new PdfPCell(new Phrase(""));
        verticalLineCell.Border = Rectangle.NO_BORDER;
        verticalLineCell.FixedHeight = 10;
        verticalLineCell.Colspan = 2;
        modelTable.AddCell(verticalLineCell);

        document.Add(modelTable);
        //document.Add(Chunk.NEWLINE);

        // "OPTION/ACCESSORY DESCRIPTION" with gray background
        PdfPTable optionDescriptionTable = new PdfPTable(1);
        optionDescriptionTable.WidthPercentage = 100;
        PdfPCell optionDescriptionCell = new PdfPCell(new Phrase("OPTION/ACCESSORY DESCRIPTION", boldFont));
        optionDescriptionCell.BackgroundColor = lightGrayColor;
        optionDescriptionCell.Border = Rectangle.NO_BORDER;
        optionDescriptionCell.Padding = 5;
        optionDescriptionTable.AddCell(optionDescriptionCell);
        document.Add(optionDescriptionTable);

        PdfPTable optionTable = new PdfPTable(2);
        optionTable.WidthPercentage = 100;
        optionTable.SetWidths(new float[] { 8, 1 });

        cell = new PdfPCell(new Phrase("DISPOSABLE HANDLE ADAPTER, LOW CEILING LIGHTHEADS, AND WALL CONTROL", normalFont));
        //cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.Padding = 5;
        optionTable.AddCell(cell);

        cell = new PdfPCell(new Phrase("3", normalFont));
        cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        optionTable.AddCell(cell);

        document.Add(optionTable);
        //document.Add(Chunk.NEWLINE);

        // Second "MODEL DESCRIPTION" row
        PdfPTable model2Table = new PdfPTable(2);
        model2Table.WidthPercentage = 100;
        model2Table.SetWidths(new float[] { 8, 1 });

        cell1 = new PdfPCell(new Phrase("MODEL DESCRIPTION", new Font(normalFont.BaseFont, normalFont.Size, Font.BOLD, new BaseColor(255, 255, 255))));
        cell1.BackgroundColor = darkBlue;
        cell1.Border = Rectangle.BOX;
        cell1.Padding = 5;
        model2Table.AddCell(cell1);

        cell2 = new PdfPCell(new Phrase("QTY", new Font(normalFont.BaseFont, normalFont.Size, Font.BOLD, new BaseColor(255, 255, 255))));
        cell2.BackgroundColor = darkBlue;
        cell2.Border = Rectangle.BOX;
        cell2.HorizontalAlignment = Element.ALIGN_CENTER;
        cell2.Padding = 5;
        model2Table.AddCell(cell2);

        cell = new PdfPCell(new Phrase("ARTICULATING BOOM: 1200MM TOP HORIZONTAL ARM and 1000MM ARTICULATING ARM", normalFont));
        //cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.Padding = 5;
        model2Table.AddCell(cell);

        cell = new PdfPCell(new Phrase("3", normalFont));
        cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        model2Table.AddCell(cell);

        // Add vertical line after the second "MODEL DESCRIPTION" row
        verticalLineCell = new PdfPCell(new Phrase(""));
        verticalLineCell.Border = Rectangle.NO_BORDER;
        verticalLineCell.FixedHeight = 10;
        verticalLineCell.Colspan = 2;
        model2Table.AddCell(verticalLineCell);

        document.Add(model2Table);
        //document.Add(Chunk.NEWLINE);

        // Second "OPTION/ACCESSORY DESCRIPTION" with gray background
        optionDescriptionTable = new PdfPTable(1);
        optionDescriptionTable.WidthPercentage = 100;
        optionDescriptionCell = new PdfPCell(new Phrase("OPTION/ACCESSORY DESCRIPTION", boldFont));
        optionDescriptionCell.BackgroundColor = lightGrayColor;
        optionDescriptionCell.Border = Rectangle.NO_BORDER;
        optionDescriptionCell.Padding = 5;
        optionDescriptionTable.AddCell(optionDescriptionCell);
        document.Add(optionDescriptionTable);

        PdfPTable option2Table = new PdfPTable(2);
        option2Table.WidthPercentage = 100;
        option2Table.SetWidths(new float[] { 8, 1 });

        cell = new PdfPCell(new Phrase("(5) Gasses, Nitrogen Control Panel, (4) 500mm shelves, Front and back handles, LED Control Display and accessories.", normalFont));
        //cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.Padding = 5;
        option2Table.AddCell(cell);

        cell = new PdfPCell(new Phrase("3", normalFont));
        cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        option2Table.AddCell(cell);

        document.Add(option2Table);
        //document.Add(Chunk.NEWLINE);

        // Total price
        PdfPTable totalTable = new PdfPTable(2);
        totalTable.WidthPercentage = 100;
        totalTable.SetWidths(new float[] { 8, 2 });

        cell = new PdfPCell(new Phrase("EQUIPMENT TOTAL LIST PRICE", boldFont));
        cell.Border = Rectangle.BOX;
        cell.Padding = 5;
        totalTable.AddCell(cell);

        cell = new PdfPCell(new Phrase("$143,334.18", boldFont));
        cell.Border = Rectangle.BOX;
        cell.HorizontalAlignment = Element.ALIGN_RIGHT;
        cell.Padding = 5;
        totalTable.AddCell(cell);

        document.Add(totalTable);
    }



    private void GenerateFirstPageBackup(Document document)
    {
        AddCompanyHeader2(document);

        // Equipment details table
        //document.Add(new Paragraph("MODEL DESCRIPTION", boldFont));

        PdfPTable modelTable = new PdfPTable(2);
        modelTable.WidthPercentage = 100;
        modelTable.SetWidths(new float[] { 8, 1 });


        PdfPCell cell1 = new PdfPCell(new Phrase("MODEL DESCRIPTION", normalFont));
        cell1.BackgroundColor = darkBlue;
        cell1.Border = Rectangle.BOX;
        cell1.Padding = 5;
        modelTable.AddCell(cell1);


        PdfPCell cell2 = new PdfPCell(new Phrase("QTY", normalFont));
        cell2.BackgroundColor = darkBlue;
        cell2.Border = Rectangle.BOX;
        cell2.HorizontalAlignment = Element.ALIGN_CENTER;
        cell2.Padding = 5;
        modelTable.AddCell(cell2);




        PdfPCell cell = new PdfPCell(new Phrase("LIGHT", normalFont));
        cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.Padding = 5;
        modelTable.AddCell(cell);

        SelectablePrice[] selectablePrice = FindObjectsOfType<SelectablePrice>();
        string lighCount = selectablePrice.Length.ToString();
        cell = new PdfPCell(new Phrase(lighCount, normalFont));
        cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        modelTable.AddCell(cell);

        document.Add(modelTable);
        document.Add(Chunk.NEWLINE);

        // Option/Accessory details
        document.Add(new Paragraph("OPTION/ACCESSORY DESCRIPTION", boldFont));

        PdfPTable optionTable = new PdfPTable(2);
        optionTable.WidthPercentage = 100;
        optionTable.SetWidths(new float[] { 8, 1 });

        cell = new PdfPCell(new Phrase("DISPOSABLE HANDLE ADAPTER, LOW CEILING LIGHTHEADS, AND WALL CONTROL", normalFont));
        cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.Padding = 5;
        optionTable.AddCell(cell);

        cell = new PdfPCell(new Phrase("3", normalFont));
        cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        optionTable.AddCell(cell);

        document.Add(optionTable);
        document.Add(Chunk.NEWLINE);

        // Second MODEL DESCRIPTION
        document.Add(new Paragraph("MODEL DESCRIPTION", boldFont));

        PdfPTable model2Table = new PdfPTable(2);
        model2Table.WidthPercentage = 100;
        model2Table.SetWidths(new float[] { 8, 1 });

        cell = new PdfPCell(new Phrase("ARTICULATING BOOM: 1200MM TOP HORIZONTAL ARM and 1000MM ARTICULATING ARM", normalFont));
        cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.Padding = 5;
        model2Table.AddCell(cell);

        cell = new PdfPCell(new Phrase("3", normalFont));
        cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        model2Table.AddCell(cell);

        document.Add(model2Table);
        document.Add(Chunk.NEWLINE);

        // Second OPTION/ACCESSORY DESCRIPTION
        document.Add(new Paragraph("OPTION/ACCESSORY DESCRIPTION", boldFont));

        PdfPTable option2Table = new PdfPTable(2);
        option2Table.WidthPercentage = 100;
        option2Table.SetWidths(new float[] { 8, 1 });

        cell = new PdfPCell(new Phrase("(5) Gasses, Nitrogen Control Panel, (4) 500mm shelves, Front and back handles, LED Control Display and acessories.", normalFont));
        cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.Padding = 5;
        option2Table.AddCell(cell);

        cell = new PdfPCell(new Phrase("3", normalFont));
        cell.BackgroundColor = lightGrayColor;
        cell.Border = Rectangle.BOX;
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        cell.Padding = 5;
        option2Table.AddCell(cell);

        document.Add(option2Table);
        document.Add(Chunk.NEWLINE);

        // Total price
        PdfPTable totalTable = new PdfPTable(2);
        totalTable.WidthPercentage = 100;
        totalTable.SetWidths(new float[] { 8, 2 });

        cell = new PdfPCell(new Phrase("EQUIPMENT TOTAL LIST PRICE", boldFont));
        cell.Border = Rectangle.BOX;
        cell.Padding = 5;
        totalTable.AddCell(cell);

        cell = new PdfPCell(new Phrase("$143,334.18", boldFont));
        cell.Border = Rectangle.BOX;
        cell.HorizontalAlignment = Element.ALIGN_RIGHT;
        cell.Padding = 5;
        totalTable.AddCell(cell);

        document.Add(totalTable);

        // Add notes section
        //Paragraph notesPara = new Paragraph("Notes:", notesFont);
        //notesPara.SpacingBefore = 20;
        //document.Add(notesPara);

        //// Add notes about highlighted cells
        //document.Add(new Paragraph("Highlighted cells can be pulled from cells already in the tool, apart from the sales rep name", notesFont));
        //document.Add(new Paragraph("* This description can be pulled from the list of items added to the boom", notesFont));
        //document.Add(new Paragraph("** This description will be pulled from the drop-down menu presented when adding a light", notesFont));
        //document.Add(new Paragraph("*** This description can be pulled from the list of items added to the boom", notesFont));
    }

    private void GenerateSecondPage(Document document, PdfWriter pdfWriter)
    {
        AddCompanyHeader2(document);

        int imageCount = 0;
        for (int i = 0; i < selectables.Length; i++)
        {
            // Add images to the second page
            List<PdfExporter.PdfImageData> imageDatas = selectables[i].ExportElevationPdf();
            for (int j = 0; j < imageDatas.Count; j++)
            {
                //	The imageCount variable is used to keep track of the number of images added
                //	If imageCount is even and not zero, a new page is created, and the header is added again.
                if (imageCount % 2 == 0 && imageCount != 0)
                {
                    document.NewPage();
                    AddCompanyHeader2(document);
                }

                //AddImageToPDF(document, imageDatas[j].Path);
                AddImageToPDF(document, imageDatas[j].Path, pdfWriter);
                imageCount++;
            }
        }
    }

    private void AddImageToPDF(Document document, string imagePath)
    {
        iTextSharp.text.Image image = iTextSharp.text.Image.GetInstance(imagePath);
        image.ScaleToFit(document.PageSize.Width / 4 - 20, document.PageSize.Height / 4 - 20);
        image.Alignment = iTextSharp.text.Image.ALIGN_CENTER;
        document.Add(image);

        // Get the PdfContentByte object from the writer
        PdfWriter writer = PdfWriter.GetInstance(document, new FileStream("temp.pdf", FileMode.Create));
        PdfContentByte cb = writer.DirectContent;

        // Get the position and size of the image
        float x = image.AbsoluteX;
        float y = image.AbsoluteY;
        float width = image.ScaledWidth;
        float height = image.ScaledHeight;

        // Draw a vertical line at the bottom of the image
        cb.MoveTo(x + width / 2, y);
        cb.LineTo(x + width / 2, y - 50); // Adjust the length of the line as needed
        cb.Stroke();

    }

    private void AddImageToPDF(Document document, string imagePath, PdfWriter writer)
    {
        // Load image
        Image image = Image.GetInstance(imagePath);
        // Scale image
        image.ScaleToFit(document.PageSize.Width / 4 - 20, document.PageSize.Height / 4 - 20);
        // Set alignment
        image.Alignment = Image.ALIGN_CENTER;
        // Get the Y position of the image
        float imageBottomY = document.Top - image.ScaledHeight - 10; // Adjust as needed

        // Add image to document
        document.Add(image);

        // Draw black line below the image
        //PdfContentByte canvas = writer.DirectContent;
        //canvas.SetLineWidth(2f); // Line thickness
        //canvas.SetRGBColorStroke(0, 0, 0); // Black color
        //canvas.MoveTo(document.LeftMargin, imageBottomY); // Start point (x, y)
        //canvas.LineTo(document.PageSize.Width - document.RightMargin, imageBottomY); // End point (x, y)
        //canvas.Stroke();
    }

    private void GenerateThirdPage(Document document)
    {
        // Add company header
        AddCompanyHeader2(document);

        // Add "Proposal" title
        //Paragraph proposalTitle = new Paragraph("Proposal", titleFont);
        //proposalTitle.Alignment = Element.ALIGN_CENTER;
        //proposalTitle.SpacingAfter = 20;
        //document.Add(proposalTitle);

        //// Add sales rep info
        //document.Add(new Paragraph("Sales Rep Name", boldFont));
        //document.Add(new Paragraph(salesRepEmail, normalFont));
        //document.Add(Chunk.NEWLINE);

        // Detailed pricing table with part numbers
        PdfPTable detailTable = new PdfPTable(5);
        detailTable.WidthPercentage = 100;
        detailTable.SetWidths(new float[] { 2, 5, 1, 2, 2 });

        // Table headers
        PdfPCell headerCell = new PdfPCell(new Phrase("PART #", boldFont));
        headerCell.BackgroundColor = headerColor;
        headerCell.HorizontalAlignment = Element.ALIGN_CENTER;
        headerCell.VerticalAlignment = Element.ALIGN_MIDDLE;
        headerCell.Padding = 5;
        detailTable.AddCell(headerCell);

        headerCell = new PdfPCell(new Phrase("MODEL DESCRIPTION", boldFont));
        headerCell.BackgroundColor = headerColor;
        headerCell.HorizontalAlignment = Element.ALIGN_CENTER;
        headerCell.VerticalAlignment = Element.ALIGN_MIDDLE;
        headerCell.Padding = 5;
        detailTable.AddCell(headerCell);

        headerCell = new PdfPCell(new Phrase("QTY", boldFont));
        headerCell.BackgroundColor = headerColor;
        headerCell.HorizontalAlignment = Element.ALIGN_CENTER;
        headerCell.VerticalAlignment = Element.ALIGN_MIDDLE;
        headerCell.Padding = 5;
        detailTable.AddCell(headerCell);

        headerCell = new PdfPCell(new Phrase("LIST PRICE", boldFont));
        headerCell.BackgroundColor = headerColor;
        headerCell.HorizontalAlignment = Element.ALIGN_CENTER;
        headerCell.VerticalAlignment = Element.ALIGN_MIDDLE;
        headerCell.Padding = 5;
        detailTable.AddCell(headerCell);

        headerCell = new PdfPCell(new Phrase("EXT LIST PRICE", boldFont));
        headerCell.BackgroundColor = headerColor;
        headerCell.HorizontalAlignment = Element.ALIGN_CENTER;
        headerCell.VerticalAlignment = Element.ALIGN_MIDDLE;
        headerCell.Padding = 5;
        detailTable.AddCell(headerCell);

        // Row 1
        detailTable.AddCell(new PdfPCell(new Phrase("000001", normalFont)));
        detailTable.AddCell(new PdfPCell(new Phrase("LIGHT", normalFont)));
        detailTable.AddCell(CreateCenteredCell("3", normalFont));
        detailTable.AddCell(CreateRightAlignedCell("$47,778.06", normalFont));
        detailTable.AddCell(CreateRightAlignedCell("$143,334.18", normalFont));

        // OPTION/ACCESSORY HEADER
        PdfPCell optionHeaderCell = new PdfPCell(new Phrase("PART #", boldFont));
        optionHeaderCell.BackgroundColor = headerColor;
        optionHeaderCell.Padding = 5;
        detailTable.AddCell(optionHeaderCell);

        optionHeaderCell = new PdfPCell(new Phrase("OPTION/ACCESSORY DESCRIPTION", boldFont));
        optionHeaderCell.BackgroundColor = headerColor;
        optionHeaderCell.Padding = 5;
        detailTable.AddCell(optionHeaderCell);

        optionHeaderCell = new PdfPCell(new Phrase("QTY", boldFont));
        optionHeaderCell.BackgroundColor = headerColor;
        optionHeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
        optionHeaderCell.Padding = 5;
        detailTable.AddCell(optionHeaderCell);

        optionHeaderCell = new PdfPCell(new Phrase("LIST PRICE", boldFont));
        optionHeaderCell.BackgroundColor = headerColor;
        optionHeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
        optionHeaderCell.Padding = 5;
        detailTable.AddCell(optionHeaderCell);

        optionHeaderCell = new PdfPCell(new Phrase("EXT LIST PRICE", boldFont));
        optionHeaderCell.BackgroundColor = headerColor;
        optionHeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
        optionHeaderCell.Padding = 5;
        detailTable.AddCell(optionHeaderCell);

        // Accessory Rows
        AddAccessoryRow(detailTable, "0000002", "PREPARATION FOR COMMUNICATION INTERFACE, E-SERIES, F-GENERATION", "3", "$4,344.01", "$13,032.03");
        AddAccessoryRow(detailTable, "0000003", "STRYKECAM FOR F-GEN", "3", "$25,795.40", "$77,386.20");
        AddAccessoryRow(detailTable, "0000004", "BUNDLE, STRYKECAM READY", "3", "$5,444.91", "$16,334.73");
        AddAccessoryRow(detailTable, "0000005", "HANDLE, DEVON SLIP-ON FP", "3", "$306.58", "$919.74");

        // Second MODEL DESCRIPTION HEADER
        PdfPCell model2HeaderCell = new PdfPCell(new Phrase("PART #", boldFont));
        model2HeaderCell.BackgroundColor = headerColor;
        model2HeaderCell.Padding = 5;
        detailTable.AddCell(model2HeaderCell);

        model2HeaderCell = new PdfPCell(new Phrase("MODEL DESCRIPTION", boldFont));
        model2HeaderCell.BackgroundColor = headerColor;
        model2HeaderCell.Padding = 5;
        detailTable.AddCell(model2HeaderCell);

        model2HeaderCell = new PdfPCell(new Phrase("QTY", boldFont));
        model2HeaderCell.BackgroundColor = headerColor;
        model2HeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
        model2HeaderCell.Padding = 5;
        detailTable.AddCell(model2HeaderCell);

        model2HeaderCell = new PdfPCell(new Phrase("LIST PRICE", boldFont));
        model2HeaderCell.BackgroundColor = headerColor;
        model2HeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
        model2HeaderCell.Padding = 5;
        detailTable.AddCell(model2HeaderCell);

        model2HeaderCell = new PdfPCell(new Phrase("EXT LIST PRICE", boldFont));
        model2HeaderCell.BackgroundColor = headerColor;
        model2HeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
        model2HeaderCell.Padding = 5;
        detailTable.AddCell(model2HeaderCell);

        // Second model row
        detailTable.AddCell(new PdfPCell(new Phrase("0000006", normalFont)));
        detailTable.AddCell(new PdfPCell(new Phrase("S-SERIES, STANDARD POWERED, 3 ROW, 2 A", normalFont)));
        detailTable.AddCell(CreateCenteredCell("3", normalFont));
        detailTable.AddCell(CreateRightAlignedCell("$52,626.90", normalFont));
        detailTable.AddCell(CreateRightAlignedCell("$157,880.70", normalFont));

        // Second OPTION/ACCESSORY HEADER
        PdfPCell option2HeaderCell = new PdfPCell(new Phrase("PART #", boldFont));
        option2HeaderCell.BackgroundColor = headerColor;
        option2HeaderCell.Padding = 5;
        detailTable.AddCell(option2HeaderCell);

        option2HeaderCell = new PdfPCell(new Phrase("OPTION/ACCESSORY DESCRIPTION", boldFont));
        option2HeaderCell.BackgroundColor = headerColor;
        option2HeaderCell.Padding = 5;
        detailTable.AddCell(option2HeaderCell);

        option2HeaderCell = new PdfPCell(new Phrase("QTY", boldFont));
        option2HeaderCell.BackgroundColor = headerColor;
        option2HeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
        option2HeaderCell.Padding = 5;
        detailTable.AddCell(option2HeaderCell);

        option2HeaderCell = new PdfPCell(new Phrase("LIST PRICE", boldFont));
        option2HeaderCell.BackgroundColor = headerColor;
        option2HeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
        option2HeaderCell.Padding = 5;
        detailTable.AddCell(option2HeaderCell);

        option2HeaderCell = new PdfPCell(new Phrase("EXT LIST PRICE", boldFont));
        option2HeaderCell.BackgroundColor = headerColor;
        option2HeaderCell.HorizontalAlignment = Element.ALIGN_CENTER;
        option2HeaderCell.Padding = 5;
        detailTable.AddCell(option2HeaderCell);

        // Second accessory rows
        AddAccessoryRow(detailTable, "0000007", "ASM, HANDLE AND SHELF WITH FAIRFIELD R", "3", "$2,363.27", "$7,089.81");
        AddAccessoryRow(detailTable, "0000008", "ASM, SHELF MOUNT HDW", "6", "$650.46", "$3,902.76");
        AddAccessoryRow(detailTable, "0000009", "ASM, SHELF WITH FAIRFIELD RAILS, 515,", "3", "$2,363.27", "$7,089.81");
        AddAccessoryRow(detailTable, "0000010", "ASM, SHELF WITH FAIRFIELD RAILS, 515,", "3", "$2,363.27", "$7,089.81");
        AddAccessoryRow(detailTable, "0000011", "KIT, FOOT PEDAL STORAGE DRAWER, ATLAS", "3", "$824.12", "$2,472.36");

        document.Add(detailTable);
        document.Add(Chunk.NEWLINE);

        // Totals table
        PdfPTable totalsTable = new PdfPTable(2);
        totalsTable.WidthPercentage = 100;
        totalsTable.SetWidths(new float[] { 8, 2 });

        // Total Equipment
        PdfPCell totalsCell = new PdfPCell(new Phrase("TOTAL EQUIPMENT LIST PRICE", boldFont));
        totalsCell.Border = Rectangle.BOX;
        totalsCell.Padding = 5;
        totalsTable.AddCell(totalsCell);

        totalsCell = new PdfPCell(new Phrase("$143,334.18", boldFont));
        totalsCell.Border = Rectangle.BOX;
        totalsCell.HorizontalAlignment = Element.ALIGN_RIGHT;
        totalsCell.Padding = 5;
        totalsTable.AddCell(totalsCell);

        // Discount
        totalsCell = new PdfPCell(new Phrase("DISCOUNT", boldFont));
        totalsCell.Border = Rectangle.BOX;
        totalsCell.Padding = 5;
        totalsTable.AddCell(totalsCell);

        totalsCell = new PdfPCell(new Phrase("$71,667.09", boldFont));
        totalsCell.Border = Rectangle.BOX;
        totalsCell.HorizontalAlignment = Element.ALIGN_RIGHT;
        totalsCell.Padding = 5;
        totalsTable.AddCell(totalsCell);

        // Grand Total
        totalsCell = new PdfPCell(new Phrase("GRAND TOTAL PRICE", boldFont));
        totalsCell.Border = Rectangle.BOX;
        totalsCell.Padding = 5;
        totalsTable.AddCell(totalsCell);

        totalsCell = new PdfPCell(new Phrase("$71,667.09", boldFont));
        totalsCell.Border = Rectangle.BOX;
        totalsCell.HorizontalAlignment = Element.ALIGN_RIGHT;
        totalsCell.Padding = 5;
        totalsTable.AddCell(totalsCell);

        document.Add(totalsTable);
        document.Add(Chunk.NEWLINE);

        // Acceptance and configuration acknowledgement
        //Paragraph acceptanceTitle = new Paragraph("Customer Acceptance and Configuration Acknowledgement", boldFont);
        //acceptanceTitle.SpacingBefore = 10;
        //document.Add(acceptanceTitle);
        //document.Add(Chunk.NEWLINE);

        Paragraph acceptanceTitle = new Paragraph("Notes:", boldFont);
       // acceptanceTitle.SpacingBefore = 5;
        document.Add(acceptanceTitle);
        document.Add(Chunk.NEWLINE);

        Paragraph Notes = new Paragraph("35% deposit. Progress billing to completion. Sales tax to be added to invoices in applicable", boldFont);
       // Notes.SpacingBefore = 10;
        document.Add(Notes);
        document.Add(Chunk.NEWLINE);

        Paragraph Notes2 = new Paragraph("Quote is valid for 90 days from creation date.", notesFont);
       // Notes2.SpacingBefore = 10;
        document.Add(Notes2);
        document.Add(Chunk.NEWLINE);


        // Acceptance details table
        PdfPTable acceptanceTable = new PdfPTable(2);
        acceptanceTable.WidthPercentage = 100;
        acceptanceTable.DefaultCell.Border = Rectangle.NO_BORDER;
        acceptanceTable.SetWidths(new float[] { 60, 40 });

        // Right column
        PdfPCell rightCell = new PdfPCell();
        rightCell.Border = Rectangle.NO_BORDER;
        
        //rightCell.AddElement(new Paragraph("Notes:", notesFont));
        rightCell.AddElement(new Paragraph("Customer Acceptance and Configuration Acknowledgement", notesFont));
        rightCell.AddElement(new Paragraph("Quote is valid for 90 days from creation date.", notesFont));
        rightCell.AddElement(Chunk.NEWLINE);
        rightCell.AddElement(new Paragraph("Signature                 Date", normalFont));
        
        acceptanceTable.AddCell(rightCell);

        // Left column
        PdfPCell leftCell = new PdfPCell();
        leftCell.Border = Rectangle.NO_BORDER;
        leftCell.AddElement(new Paragraph("Account Name: Medical City Lewisville", normalFont));
        leftCell.AddElement(new Paragraph("Account Address: 500 W Main St", normalFont));
        leftCell.AddElement(new Paragraph("Lewisville, TX 75057", normalFont));
        leftCell.AddElement(new Paragraph("Project Name: OR Upgrade", normalFont));
        leftCell.AddElement(new Paragraph("Project Number: IU-10462", normalFont));
        rightCell.AddElement(new Paragraph("Order Reference #:", normalFont));
        acceptanceTable.AddCell(leftCell);

        

        document.Add(acceptanceTable);
        document.Add(Chunk.NEWLINE);

        // Notes about elevations
        //Paragraph elevationNote = new Paragraph("Notes: This can be pulled from the elevations.", notesFont);
        //document.Add(elevationNote);
    }

    private void GeneratePricingPage(Document document)
    {
        // Add company header
        AddCompanyHeader2(document);

        TableTitle(document);

        // Detailed pricing table with part numbers
        PdfPTable detailTable = new PdfPTable(5);
        detailTable.WidthPercentage = 100;
        detailTable.SetWidths(new float[] { 2, 5, 1, 2, 2 });

        // Table headers
        PdfPCell headerCell = new PdfPCell(new Phrase("PART #", boldFont));
        headerCell.BackgroundColor = headerColor;
        headerCell.HorizontalAlignment = Element.ALIGN_CENTER;
        headerCell.VerticalAlignment = Element.ALIGN_MIDDLE;
        headerCell.Padding = 5;
        detailTable.AddCell(headerCell);

        headerCell = new PdfPCell(new Phrase("MODEL DESCRIPTION", boldFont));
        headerCell.BackgroundColor = headerColor;
        headerCell.HorizontalAlignment = Element.ALIGN_CENTER;
        headerCell.VerticalAlignment = Element.ALIGN_MIDDLE;
        headerCell.Padding = 5;
        detailTable.AddCell(headerCell);

        headerCell = new PdfPCell(new Phrase("QTY", boldFont));
        headerCell.BackgroundColor = headerColor;
        headerCell.HorizontalAlignment = Element.ALIGN_CENTER;
        headerCell.VerticalAlignment = Element.ALIGN_MIDDLE;
        headerCell.Padding = 5;
        detailTable.AddCell(headerCell);

        headerCell = new PdfPCell(new Phrase("LIST PRICE", boldFont));
        headerCell.BackgroundColor = headerColor;
        headerCell.HorizontalAlignment = Element.ALIGN_CENTER;
        headerCell.VerticalAlignment = Element.ALIGN_MIDDLE;
        headerCell.Padding = 5;
        detailTable.AddCell(headerCell);

        headerCell = new PdfPCell(new Phrase("EXT LIST PRICE", boldFont));
        headerCell.BackgroundColor = headerColor;
        headerCell.HorizontalAlignment = Element.ALIGN_CENTER;
        headerCell.VerticalAlignment = Element.ALIGN_MIDDLE;
        headerCell.Padding = 5;
        detailTable.AddCell(headerCell);

        // Find all instances of SelectablePrice
        
        foreach (var selectablePrice in selectablePrices)
        {
            var data = selectablePrice.objectPricingData;

            // Add rows to the table
            detailTable.AddCell(new PdfPCell(new Phrase(data.PartNumber, normalFont)));
            detailTable.AddCell(new PdfPCell(new Phrase(data.ObjectName, normalFont)));
            detailTable.AddCell(CreateCenteredCell("1", normalFont)); // Assuming quantity is 1
            detailTable.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont));
            detailTable.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont)); // Assuming EXT LIST PRICE is the same as LIST PRICE
        }

        // Add "Additional Items" row
        PdfPCell additionalItemsCell = new PdfPCell(new Phrase("Additional Items", boldFont));
        additionalItemsCell.Colspan = 5;
        additionalItemsCell.BackgroundColor = headerColor;
        additionalItemsCell.HorizontalAlignment = Element.ALIGN_CENTER;
        additionalItemsCell.Padding = 5;
        detailTable.AddCell(additionalItemsCell);

        // Get data from DropdownPopulator and add rows to the table
        var dropdownStates = DropdownPopulator.GetAllCurrentStates();
        foreach (var state in dropdownStates)
        {
            
            var data = state.Item2;
            if (data.ListPrice == 0) continue; // Skip items with no price

            // Add rows to the table
            detailTable.AddCell(new PdfPCell(new Phrase(data.PartNumber, normalFont)));
            detailTable.AddCell(new PdfPCell(new Phrase(data.ObjectName, normalFont)));
            detailTable.AddCell(CreateCenteredCell("1", normalFont)); // Assuming quantity is 1
            detailTable.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont));
            detailTable.AddCell(CreateRightAlignedCell(data.ListPrice.ToString("C"), normalFont)); // Assuming EXT LIST PRICE is the same as LIST PRICE
        }


        document.Add(detailTable);
        document.Add(Chunk.NEWLINE);

        // Totals table
        PdfPTable totalsTable = new PdfPTable(2);
        totalsTable.WidthPercentage = 100;
        totalsTable.SetWidths(new float[] { 8, 2 });

        // Total Equipment
        PdfPCell totalsCell = new PdfPCell(new Phrase("TOTAL EQUIPMENT LIST PRICE", boldFont));
        totalsCell.Border = Rectangle.BOX;
        totalsCell.Padding = 5;
        totalsTable.AddCell(totalsCell);

        // Calculate total price
        double totalPrice = selectablePrices.Sum(sp => sp.objectPricingData.ListPrice);
        totalsCell = new PdfPCell(new Phrase(totalPrice.ToString("C"), boldFont));
        totalsCell.Border = Rectangle.BOX;
        totalsCell.HorizontalAlignment = Element.ALIGN_RIGHT;
        totalsCell.Padding = 5;
        totalsTable.AddCell(totalsCell);

        document.Add(totalsTable);
        document.Add(Chunk.NEWLINE);


        Paragraph acceptanceTitle = new Paragraph("Notes:", boldFont);
        // acceptanceTitle.SpacingBefore = 5;
        document.Add(acceptanceTitle);
        document.Add(Chunk.NEWLINE);

        Paragraph Notes = new Paragraph("35% deposit. Progress billing to completion. Sales tax to be added to invoices in applicable", boldFont);
        // Notes.SpacingBefore = 10;
        document.Add(Notes);
        document.Add(Chunk.NEWLINE);

        Paragraph Notes2 = new Paragraph("Quote is valid for 90 days from creation date.", notesFont);
        // Notes2.SpacingBefore = 10;
        document.Add(Notes2);
        document.Add(Chunk.NEWLINE);


        // Acceptance details table
        PdfPTable acceptanceTable = new PdfPTable(2);
        acceptanceTable.WidthPercentage = 100;
        acceptanceTable.DefaultCell.Border = Rectangle.NO_BORDER;
        acceptanceTable.SetWidths(new float[] { 60, 40 });

        // Right column
        PdfPCell rightCell = new PdfPCell();
        rightCell.Border = Rectangle.NO_BORDER;

        //rightCell.AddElement(new Paragraph("Notes:", notesFont));
        rightCell.AddElement(new Paragraph("Customer Acceptance and Configuration Acknowledgement", notesFont));
        rightCell.AddElement(new Paragraph("Quote is valid for 90 days from creation date.", notesFont));
        rightCell.AddElement(Chunk.NEWLINE);
        rightCell.AddElement(Chunk.NEWLINE);
        rightCell.AddElement(Chunk.NEWLINE);
        rightCell.AddElement(new Paragraph("Signature                 Date", normalFont));

        acceptanceTable.AddCell(rightCell);

        // Left column
        PdfPCell leftCell = new PdfPCell();
        leftCell.Border = Rectangle.NO_BORDER;
        leftCell.AddElement(new Paragraph("Account Name: Medical City Lewisville", normalFont));
        leftCell.AddElement(new Paragraph("Account Address: 500 W Main St", normalFont));
        leftCell.AddElement(new Paragraph("Lewisville, TX 75057", normalFont));
        leftCell.AddElement(new Paragraph("Project Name: OR Upgrade", normalFont));
        leftCell.AddElement(new Paragraph("Project Number: IU-10462", normalFont));
        rightCell.AddElement(new Paragraph("Order Reference #:", normalFont));
        acceptanceTable.AddCell(leftCell);


        document.Add(acceptanceTable);
        document.Add(Chunk.NEWLINE);

    }


    private void AddCompanyHeader(Document document)
    {
        PdfPTable headerTable = new PdfPTable(2);
        headerTable.WidthPercentage = 100;
        headerTable.DefaultCell.Border = Rectangle.NO_BORDER;

        // Company logo/name cell
        PdfPCell logoCell = new PdfPCell();
        logoCell.Border = Rectangle.NO_BORDER;
        logoCell.AddElement(new Paragraph(companyName, headerFont));
        logoCell.AddElement(new Paragraph(companyAddress, normalFont));
        logoCell.AddElement(new Paragraph(companyCity, normalFont));
        logoCell.AddElement(new Paragraph("Tel: " + companyPhone + " Fax: " + companyFax, normalFont));
        headerTable.AddCell(logoCell);

        // Empty cell for right side
        PdfPCell emptyCell = new PdfPCell();
        emptyCell.Border = Rectangle.NO_BORDER;
        headerTable.AddCell(emptyCell);

        document.Add(headerTable);
        document.Add(Chunk.NEWLINE);
    }


    private void AddCompanyHeader2_Backup(Document document)
    {
        PdfPTable headerTable = new PdfPTable(2);
        headerTable.WidthPercentage = 100;
        headerTable.DefaultCell.Border = Rectangle.NO_BORDER;
        headerTable.SetWidths(new float[] { 70, 30 }); // Adjust column widths as needed

        // First column: Proposal paragraph, Sales Rep paragraph, and Company header information
        PdfPCell leftCell = new PdfPCell();
        leftCell.Border = Rectangle.NO_BORDER;

        // Proposal paragraph
        Paragraph proposalParagraph = new Paragraph("Proposal", titleFont);
        proposalParagraph.Alignment = Element.ALIGN_LEFT;
        proposalParagraph.SpacingAfter = 10;
        leftCell.AddElement(proposalParagraph);

        // Sales Rep paragraph
        Paragraph salesRepParagraph = new Paragraph("Sales Rep: " + salesRepName + "\n" + salesRepEmail, normalFont);
        salesRepParagraph.Alignment = Element.ALIGN_LEFT;
        salesRepParagraph.SpacingAfter = 10;
        leftCell.AddElement(salesRepParagraph);

        // Company header information
        leftCell.AddElement(new Paragraph(companyName, headerFont));
        leftCell.AddElement(new Paragraph(companyAddress, normalFont));
        leftCell.AddElement(new Paragraph(companyCity, normalFont));
        leftCell.AddElement(new Paragraph("Tel: " + companyPhone + " Fax: " + companyFax, normalFont));

        headerTable.AddCell(leftCell);

        // Second column: Logo image
        PdfPCell rightCell = new PdfPCell();
        rightCell.Border = Rectangle.NO_BORDER;
        rightCell.HorizontalAlignment = Element.ALIGN_RIGHT;

        Image logo = Image.GetInstance(Application.streamingAssetsPath + "/Data/quotes/UImagineUnlimited-logo.png");
        logo.ScaleAbsolute(150, 75); // Adjust the size as needed
        rightCell.AddElement(logo);

        headerTable.AddCell(rightCell);

        document.Add(headerTable);
        document.Add(Chunk.NEWLINE);

        Paragraph empty = new Paragraph("" + clientName, boldFont);
        empty.Alignment = Element.ALIGN_LEFT;
        empty.SpacingAfter = 10;
        document.Add(empty);

        Paragraph submittedTo = new Paragraph("Submitted To: " + clientName, boldFont);
        submittedTo.Alignment = Element.ALIGN_LEFT;
        submittedTo.SpacingAfter = 10;
        document.Add(submittedTo);

        Paragraph ProjectTo = new Paragraph("Project: " + projectName, normalFont);
        ProjectTo.Alignment = Element.ALIGN_LEFT;
        ProjectTo.SpacingAfter = 10;
        document.Add(ProjectTo);

    }

    private void AddCompanyHeader2(Document document)
    {
        PdfPTable headerTable = new PdfPTable(2);
        headerTable.WidthPercentage = 100;
        headerTable.DefaultCell.Border = Rectangle.NO_BORDER;
        headerTable.SetWidths(new float[] { 70, 30 }); // Adjust column widths as needed

        // First column: Proposal paragraph, Sales Rep paragraph, and Company header information
        PdfPCell leftCell = new PdfPCell();
        leftCell.Border = Rectangle.NO_BORDER;

        // Proposal paragraph
        Paragraph proposalParagraph = new Paragraph("Proposal", titleFont);
        proposalParagraph.Alignment = Element.ALIGN_LEFT;
        proposalParagraph.SpacingAfter = 10;
        leftCell.AddElement(proposalParagraph);

        // Sales Rep paragraph
        Paragraph salesRepParagraph = new Paragraph("Sales Rep: " + salesRepName + "\n" + salesRepEmail, normalFont);
        salesRepParagraph.Alignment = Element.ALIGN_LEFT;
        salesRepParagraph.SpacingAfter = 10;
        leftCell.AddElement(salesRepParagraph);

        // Company header information
        leftCell.AddElement(new Paragraph(companyName, headerFont));
        leftCell.AddElement(new Paragraph(companyAddress, normalFont));
        leftCell.AddElement(new Paragraph(companyCity, normalFont));
        leftCell.AddElement(new Paragraph("Tel: " + companyPhone + " Fax: " + companyFax, normalFont));

        headerTable.AddCell(leftCell);

        // Second column: Logo image
        PdfPCell rightCell = new PdfPCell();
        rightCell.Border = Rectangle.NO_BORDER;
        rightCell.HorizontalAlignment = Element.ALIGN_RIGHT;

        Image logo = Image.GetInstance(Application.streamingAssetsPath + "/Data/quotes/UImagineUnlimited-logo.png");
        logo.ScaleAbsolute(150, 75); // Adjust the size as needed
        rightCell.AddElement(logo);

        headerTable.AddCell(rightCell);

        document.Add(headerTable);
        document.Add(Chunk.NEWLINE);

        //Paragraph empty = new Paragraph("" + clientName, boldFont);
        //empty.Alignment = Element.ALIGN_LEFT;
        //empty.SpacingAfter = 10;
        //document.Add(empty);

        // "Submitted To" paragraph with light gray background
        PdfPCell submittedToCell = new PdfPCell(new Phrase("Submitted To: " + clientName, boldFont));
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
        Paragraph projectTo = new Paragraph("Project: " + projectName, underlineFont);
        projectTo.Alignment = Element.ALIGN_LEFT;
        projectTo.SpacingAfter = 10;
        document.Add(projectTo);
    }


    private void AddAccessoryRow(PdfPTable table, string partNo, string description, string qty, string price, string extPrice)
    {
        table.AddCell(new PdfPCell(new Phrase(partNo, normalFont)));
        table.AddCell(new PdfPCell(new Phrase(description, normalFont)));
        table.AddCell(CreateCenteredCell(qty, normalFont));
        table.AddCell(CreateRightAlignedCell(price, normalFont));
        table.AddCell(CreateRightAlignedCell(extPrice, normalFont));
    }

    private PdfPCell CreateCenteredCell(string text, Font font)
    {
        PdfPCell cell = new PdfPCell(new Phrase(text, font));
        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        return cell;
    }

    private PdfPCell CreateRightAlignedCell(string text, Font font)
    {
        PdfPCell cell = new PdfPCell(new Phrase(text, font));
        cell.HorizontalAlignment = Element.ALIGN_RIGHT;
        return cell;
    }

    // Page event helper for headers and footers
    public class PageEventHelper : PdfPageEventHelper
    {
        public string clientName;
        public string projectName;
        public string configName;

        public override void OnEndPage(PdfWriter writer, Document document)
        {
            PdfContentByte cb = writer.DirectContent;
            Font footerFont = new Font(BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.EMBEDDED), 8);

            // Add footer with page numbers and project info
            ColumnText.ShowTextAligned(
                cb,
                Element.ALIGN_CENTER,
                new Phrase(string.Format("Effective Date: 90 Days from Delivery          {0} Proposal | Page {1} of {2}",
                    clientName, writer.PageNumber, "3"), footerFont),
                document.Right / 2 + document.LeftMargin,
                document.Bottom - 10,
                0
            );

            // Add project info at the bottom of the page
            if (writer.PageNumber != 1) // Not on first page
            {
                ColumnText.ShowTextAligned(
                    cb,
                    Element.ALIGN_LEFT,
                    new Phrase(string.Format("Project: {0}", projectName), footerFont),
                    document.LeftMargin,
                    document.Bottom - 25,
                    0
                );

                ColumnText.ShowTextAligned(
                    cb,
                    Element.ALIGN_LEFT,
                    new Phrase(string.Format("Configuration 1: {0}", configName), footerFont),
                    document.LeftMargin,
                    document.Bottom - 35,
                    0
                );

                ColumnText.ShowTextAligned(
                    cb,
                    Element.ALIGN_LEFT,
                    new Phrase(string.Format("Submitted To: {0}", clientName), footerFont),
                    document.LeftMargin,
                    document.Bottom - 45,
                    0
                );
            }
        }
    }
}