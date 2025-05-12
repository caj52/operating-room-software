using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using iTextSharp.text;
using iTextSharp.text.html;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.draw;
using Unity.VisualScripting;
using UnityEngine;
using static Measurable;
using Font = iTextSharp.text.Font;

public class PdfExporterLocal
{
    public class PdfImageData
    {
        public string Path { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }
    }

    public class ProjectMetaData
    {
        public string AccountName { get; set; }
        public string AccountAddressLine1 { get; set; }
        public string AccountAddressLine2 { get; set; }
        public string ProjectName { get; set; }
        public string ProjectNumber { get; set; }
        public string OrderReferenceNumber { get; set; }
    }

    public class PdfField
    {
        public string Item { get; set; }
        public string Value { get; set; }
    }

    public class AssemblyJson
    {
        public int AssemblyId { get; set; }
        public string TableName { get; set; }
        public List<PdfField> Fields { get; set; } = new List<PdfField>();
    }

    public static void ExportElevationPdfLocal(
        List<PdfImageData> imageData,
        string title,
        string subtitle,
        List<AssemblyJson> assemblies,
        ProjectMetaData metaData)
    {

        UI_GeneralLoadingScreen.instance.ShowLoadingScreen();
        // Create output directory if it doesn't exist
        string outputPath = Path.Combine(FullRoomSave.GetRoomPath(), "pdf");
        if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

        // Generate unique filename with timestamp
        string fileName = Path.Combine(outputPath, $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

        // Create PDF document
        using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None))
        using (Document doc = new Document(new Rectangle(1224f,1224f), 19.08f, 19.08f, 10, 10))
        {
            PdfWriter writer = PdfWriter.GetInstance(doc, fs);
            doc.Open();

            AddTitle(doc, title, subtitle);

            doc.Add(new Paragraph("\n\n")); // Add spacing at top

            // Create main layout table (assemblies on left, image on right)
            PdfPTable mainTable = new PdfPTable(2);
            mainTable.HorizontalAlignment = Element.ALIGN_LEFT;
            mainTable.WidthPercentage = 100;
            mainTable.SetWidths(new float[] { 40, 60 });

         

            // Left cell for assembly data
            PdfPCell assembliesCell = CreateAssembliesCell(assemblies);
            assembliesCell.Padding = 0;
            // Right cell for image
            PdfPCell imageCell = CreateImageCell(imageData,doc,writer, GetCeilingHeight());
            // Add cells to main table
            mainTable.AddCell(assembliesCell);
            mainTable.AddCell(imageCell);
            
           // AddSeparatorLine(doc);
            doc.Add(mainTable);

            // Add separator line
            

            // Add logo
            AddCompanyLogo(doc);

            // Add customer acceptance section
            AddCustomerAcceptanceSection(doc, metaData);

            doc.Close();
        }

       // Debug.Log("PDF Exported to: " + fileName);
        UI_GeneralLoadingScreen.instance.HideLoadingScreen();
        // Show success dialog
        UI_DialogPrompt.Open(
            $"Success! PDF saved to {fileName}",
            new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = fileName),
            new ButtonAction("Done"));

        // Open the PDF file
        OpenPdfFile(fileName);
    }
    private static float GetCeilingHeight()
    {
        // You can replace this logic depending on how your room data is structured
        return RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).Height;
    }
    private static void AddTitle(Document doc,string title,string subtitle)
    {
        // Fonts
        var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18, BaseColor.WHITE);
        var subtitleFont = FontFactory.GetFont(FontFactory.HELVETICA, 14, BaseColor.WHITE);

        // Create a table with 1 column
        PdfPTable titleBlock = new PdfPTable(1);
        titleBlock.WidthPercentage = 100;

        // Combine title and subtitle in a single Phrase
        Phrase titlePhrase = new Phrase();
        titlePhrase.Add(new Chunk(title + "\n", titleFont));
        titlePhrase.Add(new Chunk(subtitle, subtitleFont));

        // Create the cell with background color
        PdfPCell titleCell = new PdfPCell(titlePhrase);
        titleCell.BackgroundColor = WebColors.GetRGBColor("#001236"); // Hex #001236
        titleCell.Border = Rectangle.NO_BORDER;
        titleCell.PaddingTop = 76.32f;     // Only top

        titleBlock.AddCell(titleCell);
        doc.Add(titleBlock);
    }
    private static PdfPCell CreateAssembliesCell(List<AssemblyJson> assemblies)
    {
        PdfPCell assembliesCell = new PdfPCell();
        assembliesCell.Border = Rectangle.NO_BORDER;
        assembliesCell.Padding = 0;
        assembliesCell.HorizontalAlignment = Element.ALIGN_LEFT;

        // Add each assembly table
        foreach (var assembly in assemblies)
        {
            // Create header
            Font whiteFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, BaseColor.WHITE);
            Paragraph headerParagraph = new Paragraph(assembly.TableName, whiteFont);
            headerParagraph.Alignment = Element.ALIGN_LEFT;

            PdfPCell headerCell = new PdfPCell(headerParagraph);
            headerCell.BackgroundColor = HexToBaseColor("#001236");
            headerCell.Border = Rectangle.NO_BORDER;
            headerCell.Padding = 6;
            headerCell.HorizontalAlignment = Element.ALIGN_LEFT;

            PdfPTable headerTable = new PdfPTable(1);
            headerTable.TotalWidth = 303;
            headerTable.HorizontalAlignment = Element.ALIGN_LEFT;
            headerTable.LockedWidth = true;
            headerTable.AddCell(headerCell);
            assembliesCell.AddElement(headerTable);

            // Create fields table
            PdfPTable fieldTable = new PdfPTable(2);
            fieldTable.TotalWidth = 303;
            fieldTable.LockedWidth = true;
            fieldTable.HorizontalAlignment = Element.ALIGN_LEFT;
            fieldTable.SetWidths(new float[] { 40, 60 });

            float rowHeight = 21.6f;
            BaseColor grayColor1 = HexToBaseColor("#E5E7EB");
            BaseColor whiteColor1 = BaseColor.WHITE;
            bool useGray1 = true;

            // Add each field row with alternating colors
            foreach (var field in assembly.Fields)
            {
                PdfPCell itemCell = new PdfPCell(new Phrase(field.Item));
                PdfPCell valueCell = new PdfPCell(new Phrase(field.Value));

                itemCell.FixedHeight = rowHeight;
                valueCell.FixedHeight = rowHeight;

                BaseColor currentColor = useGray1 ? grayColor1 : whiteColor1;
                itemCell.BackgroundColor = currentColor;
                valueCell.BackgroundColor = currentColor;

                itemCell.HorizontalAlignment = Element.ALIGN_LEFT;
                valueCell.HorizontalAlignment = Element.ALIGN_LEFT;

                fieldTable.AddCell(itemCell);
                fieldTable.AddCell(valueCell);
                useGray1 = !useGray1;
            }

            // Add additional fields for Flat Panel Arm
            if (assembly.TableName == "Flat Panel Arm")
            {
                // Add "Circuits Required" row
                AddAdditionalRow(fieldTable, "Circuits Required", "", rowHeight, useGray1 ? grayColor1 : whiteColor1);
                useGray1 = !useGray1;

                // Add "Overall Weight" row
                AddAdditionalRow(fieldTable, "Overall Weight", "", rowHeight, useGray1 ? grayColor1 : whiteColor1);
                useGray1 = !useGray1;

                // Add "Torque Moment" row
                AddAdditionalRow(fieldTable, "Torque Moment", "", rowHeight, useGray1 ? grayColor1 : whiteColor1);
                useGray1 = !useGray1;

                // Add "Vertical Force Nm" row
                AddAdditionalRow(fieldTable, "Vertical Force Nm", "", rowHeight, useGray1 ? grayColor1 : whiteColor1);
            }

            if (assembly.TableName == "Boom Service Head")
            {
                // Add "Circuits Required" row
                AddAdditionalRow(fieldTable, "Med-Gas Connection Type", "", rowHeight, useGray1 ? grayColor1 : whiteColor1);
                useGray1 = !useGray1;

                // Add "Overall Weight" row
                AddAdditionalRow(fieldTable, "Overall Weight", "", rowHeight, useGray1 ? grayColor1 : whiteColor1);
                useGray1 = !useGray1;

                AddAdditionalRow(fieldTable, "Vertical Force", "", rowHeight, useGray1 ? grayColor1 : whiteColor1);
                // Add "Torque Moment" row
                useGray1 = !useGray1;

                // Add "Vertical Force Nm" row
                
                AddAdditionalRow(fieldTable, "Payload Capacity", "", rowHeight, useGray1 ? grayColor1 : whiteColor1);

            }


            assembliesCell.AddElement(fieldTable);
            assembliesCell.AddElement(new Paragraph(" ")); // Add spacing
        }
        return assembliesCell;
    }

    // Helper method to add additional rows
    private static void AddAdditionalRow(PdfPTable table, string itemText, string valueText, float rowHeight, BaseColor backgroundColor)
    {
        PdfPCell itemCell = new PdfPCell(new Phrase(itemText));
        PdfPCell valueCell = new PdfPCell(new Phrase(valueText));

        itemCell.FixedHeight = rowHeight;
        valueCell.FixedHeight = rowHeight;

        itemCell.BackgroundColor = backgroundColor;
        valueCell.BackgroundColor = backgroundColor;

        itemCell.HorizontalAlignment = Element.ALIGN_LEFT;
        valueCell.HorizontalAlignment = Element.ALIGN_LEFT;

        table.AddCell(itemCell);
        table.AddCell(valueCell);
    }


    public static string Distance { get; private set; } = string.Empty;
    private static PdfPCell CreateImageCell(List<PdfImageData> imageData, Document doc, PdfWriter writer, float roomHeight = 300f)
    {
        float pageWidth = doc.PageSize.Width;
        float usablePageWidth = pageWidth - (doc.LeftMargin + doc.RightMargin);
        float imageColumnWidth = usablePageWidth * 0.6f;
        float paddingBetweenImages = 10f;
        float availableImageWidth = (imageColumnWidth - paddingBetweenImages) / 2f;
        float maxTargetHeight = 240f;
        float scale1 = 80f; // Points per meter (240/3 = 80)
        float visualHeight = roomHeight * scale1;
        // Main cell to return
        PdfPCell imageCell = new PdfPCell
        {
            Border = Rectangle.NO_BORDER,
            VerticalAlignment = Element.ALIGN_BOTTOM,
            HorizontalAlignment = Element.ALIGN_CENTER,
            PaddingLeft = 20
        };

        // Outer table to stack images + beam + height indicator
        PdfPTable outerTable = new PdfPTable(1)
        {
            WidthPercentage = 100
        };
        outerTable.DefaultCell.Border = Rectangle.NO_BORDER;
        outerTable.DefaultCell.Padding = 0f;

        // Create a cell for the ceiling line (top of room)
        PdfContentByte cb = writer.DirectContent;
       
        // Add height measurement line on the left side
        PdfTemplate heightTemplate = cb.CreateTemplate(50, visualHeight);
        heightTemplate.SetLineWidth(1.5f);

        // Draw vertical line
        heightTemplate.MoveTo(10, 0);
        heightTemplate.LineTo(10, visualHeight);
        heightTemplate.Stroke();

        // Draw small horizontal lines at top and bottom
        heightTemplate.MoveTo(5, 0);
        heightTemplate.LineTo(15, 0);
        heightTemplate.Stroke();

        heightTemplate.MoveTo(5, visualHeight);
        heightTemplate.LineTo(15, visualHeight);
        heightTemplate.Stroke();


        float distanceMeters = GetCeilingHeight();
        float distanceFeet = Mathf.Floor(distanceMeters.ToFeet());
        float distanceInches = Mathf.Round((distanceMeters.ToFeet() - distanceFeet) * 12f * 10f) / 10f;
        Distance = $"{distanceFeet}' {distanceInches}\"";
        // Add height text
        heightTemplate.BeginText();
        BaseFont baseFont = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.CP1252, BaseFont.NOT_EMBEDDED);
        heightTemplate.SetFontAndSize(baseFont, 10);
        heightTemplate.ShowTextAligned(Element.ALIGN_LEFT, Distance, 20, maxTargetHeight / 2, 90);
        heightTemplate.EndText();

        Image heightImg = Image.GetInstance(heightTemplate);

        // Side-by-side table for height indicator and images
        PdfPTable contentTable = new PdfPTable(2);
        float[] columnWidths = new float[] { 10f, 90f };
        contentTable.SetWidths(columnWidths);
        contentTable.WidthPercentage = 100;

        // Height indicator cell
        PdfPCell heightCell = new PdfPCell(heightImg)
        {
            Border = Rectangle.NO_BORDER,
            Padding = 0f,
            VerticalAlignment = Element.ALIGN_MIDDLE
        };
        contentTable.AddCell(heightCell);

        // Table for two images side by side
        PdfPTable imageTable = new PdfPTable(2)
        {
            WidthPercentage = 100
        };

        for (int i = 0; i < 2; i++)
        {
            PdfPCell imgCell = new PdfPCell
            {
                Border = Rectangle.NO_BORDER,
                HorizontalAlignment = Element.ALIGN_CENTER,
                VerticalAlignment = Element.ALIGN_BOTTOM,
                Padding = 0f,
            };

            if (imageData.Count > i && File.Exists(imageData[i].Path))
            {
                Image img = Image.GetInstance(imageData[i].Path);
                img.Alignment = Element.ALIGN_BOTTOM;

                float scale = Math.Min(availableImageWidth / img.Width, maxTargetHeight / img.Height);
                img.ScaleAbsolute(img.Width * scale, img.Height * scale);
                imgCell.AddElement(img);
            }
            imageTable.AddCell(imgCell);
        }

        // Add the image table to the content table
        PdfPCell imageTableCell = new PdfPCell(imageTable)
        {
            Border = Rectangle.NO_BORDER,
            Padding = 0f
        };
        contentTable.AddCell(imageTableCell);

        // Add the content table to outer table
        PdfPCell contentCell = new PdfPCell(contentTable)
        {
            Border = Rectangle.NO_BORDER,
            Padding = 0f
        };
        outerTable.AddCell(contentCell);

        // --- DRAW THE BEAM ---
        PdfTemplate template = cb.CreateTemplate(usablePageWidth, 100);
        float beamY = 100f;
        float supportHeight = 20f;
        float spacing = usablePageWidth / 14f;
        template.SetLineWidth(3);
        template.MoveTo(0, beamY);
        template.LineTo(usablePageWidth, beamY);
        template.Stroke();

        for (int i = 0; i <= 14; i++)
        {
            float x = i * spacing;
            template.MoveTo(x, beamY);
            template.LineTo(x - supportHeight, beamY - supportHeight);
            template.Stroke();
        }

        Image beamImg = Image.GetInstance(template);
        beamImg.ScaleToFit(usablePageWidth, 100);
        PdfPCell beamCell = new PdfPCell(beamImg)
        {
            Border = Rectangle.NO_BORDER,
            Padding = 0f,
            HorizontalAlignment = Element.ALIGN_CENTER,
            VerticalAlignment = Element.ALIGN_TOP
        };
        outerTable.AddCell(beamCell);

        // Add the complete outerTable to the main imageCell
        imageCell.AddElement(outerTable);
        return imageCell;
    }


  public static  string NormalizeFeetInches(string input)
    {
        
        // Expected format: "32'6\""
        int footIndex = input.IndexOf('\'');
        int inchIndex = input.IndexOf('\"');

        if (footIndex == -1 || inchIndex == -1)
            return input; // format not as expected

        // Extract numbers
        string feetStr = input.Substring(0, footIndex);
        string inchesStr = input.Substring(footIndex + 1, inchIndex - footIndex - 1);

        if (!int.TryParse(feetStr, out int feet) || !int.TryParse(inchesStr, out int inches))
            return input;

        // Normalize inches
        feet += inches / 12;
        inches = inches % 12;

        return $"{feet}'{inches}\"";
    }
    private static void AddSeparatorLine(Document doc)
    {
        LineSeparator line = new LineSeparator
        {
            LineWidth = 5f,
            Percentage = 100,
            Offset = 10
        };

        PdfPTable lineTable = new PdfPTable(1);
        lineTable.TotalWidth = 800f;
        lineTable.LockedWidth = true;

        PdfPCell lineCell = new PdfPCell(new Phrase(new Chunk(line)));
        lineCell.Border = Rectangle.NO_BORDER;
        lineCell.PaddingTop = 0f;
        lineCell.PaddingBottom = 0f;

        lineTable.AddCell(lineCell);
        doc.Add(lineTable);
    }
    private static IElement CreateSimpleSeparatorLine()
    {
        return new LineSeparator
        {
            LineWidth = 5f,
            Percentage = 100,
            Offset = 10
        };
    }

    private static void AddCompanyLogo(Document doc)
    {
        PdfPTable logoTable = new PdfPTable(1);
        logoTable.TotalWidth = 300;
        logoTable.HorizontalAlignment = Element.ALIGN_RIGHT;
        logoTable.LockedWidth = true;
       
        PdfPCell logoCell = new PdfPCell();
        logoCell.Border = Rectangle.NO_BORDER;
        logoCell.HorizontalAlignment = Element.ALIGN_LEFT;
        logoCell.PaddingBottom= 10f;
        Image logo = Image.GetInstance(Application.streamingAssetsPath + "/Data/quotes/UImagineUnlimited-logo.png");
        logo.ScaleToFit(300,300);
        logoCell.AddElement(logo);

        logoTable.AddCell(logoCell);
        doc.Add(logoTable);
    }

    private static void AddCustomerAcceptanceSection(Document doc, ProjectMetaData metaData)
    {
        Font normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
        Font notesFont = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 10);

        PdfPTable acceptanceTable = new PdfPTable(2);
        acceptanceTable.TotalWidth = 800;
        acceptanceTable.HorizontalAlignment = Element.ALIGN_RIGHT;
        acceptanceTable.LockedWidth = true;
        acceptanceTable.SetWidths(new float[] { 60, 40 });

        // Left side - acceptance and signature
        PdfPCell leftCell = new PdfPCell();
        leftCell.Border = Rectangle.BOX;
        leftCell.Padding = 5f;

        leftCell.AddElement(new Paragraph("Customer Acceptance and Configuration Acknowledgement", notesFont));
        leftCell.AddElement(Chunk.NEWLINE);
        leftCell.AddElement(Chunk.NEWLINE);
        leftCell.AddElement(Chunk.NEWLINE);

        // Signature table
        PdfPTable signatureTable = new PdfPTable(2);
        signatureTable.WidthPercentage = 90;
        signatureTable.SetWidths(new float[] { 2f, 1f });

        PdfPCell signatureCell = new PdfPCell(new Phrase("Signature", normalFont));
        signatureCell.FixedHeight = 30f;
        signatureCell.VerticalAlignment = Element.ALIGN_BOTTOM;
        signatureCell.Border = Rectangle.TOP_BORDER;
       
       // signatureCell.BorderWidthBottom = 1f;

        PdfPCell dateCell = new PdfPCell(new Phrase("Date", normalFont));
        dateCell.FixedHeight = 30f;
        dateCell.VerticalAlignment = Element.ALIGN_BOTTOM;
        dateCell.Border = Rectangle.TOP_BORDER;
      //  dateCell.BorderWidthBottom = 1f;

        signatureTable.AddCell(signatureCell);
        signatureTable.AddCell(dateCell);
        leftCell.AddElement(signatureTable);

        acceptanceTable.AddCell(leftCell);

        // Right side - account/project details
        PdfPTable detailTable = new PdfPTable(1);
        detailTable.WidthPercentage = 100;

        string[] detailLabels = {
            "Account Name: " + metaData.AccountName,
            "Account Address: " + metaData.AccountAddressLine1,
            " "+metaData.AccountAddressLine2,
            "Project Name: " + metaData.ProjectName,
            "Project #: " + metaData.ProjectNumber,
            "Order Reference #: " + metaData.OrderReferenceNumber
        };

        foreach (var label in detailLabels)
        {
            PdfPCell labelCell = new PdfPCell(new Phrase(label, normalFont));
            labelCell.Padding = 4f;
            detailTable.AddCell(labelCell);
        }

        PdfPCell rightCell = new PdfPCell(detailTable);
        rightCell.Border = Rectangle.BOX;
        rightCell.Padding = 0f;

        acceptanceTable.AddCell(rightCell);
        doc.Add(acceptanceTable);
    }
    /// <summary>
    /// Converts a hexadecimal color string to a BaseColor object
    /// </summary>
    /// <param name="hex">Hex color code (with or without #)</param>
    /// <param name="alpha">Optional alpha value (0-255, default 255)</param>
    /// <returns>BaseColor object representing the color</returns>
    public static BaseColor HexToBaseColor(string hex, int alpha = 255)
    {
        // Validate input
        if (string.IsNullOrEmpty(hex))
            throw new ArgumentNullException(nameof(hex), "Hex color code cannot be null or empty");

        // Remove # if present
        hex = hex.Replace("#", "").Trim();

        // Handle different hex formats (3 digits or 6 digits)
        if (hex.Length == 3) // Convert short format (#RGB) to long format (#RRGGBB)
        {
            hex = string.Format("{0}{0}{1}{1}{2}{2}", hex[0], hex[1], hex[2]);
        }

        if (hex.Length != 6)
            throw new ArgumentException("Hex color code must be 3 or 6 characters in length", nameof(hex));

        try
        {
            // Parse RGB values
            byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);

            // Ensure alpha is in valid range
            alpha = Math.Max(0, Math.Min(255, alpha));

            // Check if the library supports alpha channel
            try
            {
                // Try to create BaseColor with RGBA (for newer versions)
                return new BaseColor(r, g, b, alpha);
            }
            catch
            {
                // Fall back to RGB for older versions
                return new BaseColor(r, g, b);
            }
        }
        catch (Exception ex)
        {
            throw new FormatException($"Failed to parse hex color: {hex}", ex);
        }
    }
    private static void OpenPdfFile(string fileName)
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            // Hack fix for macOS not liking Application.OpenURL
            string location = fileName;
            ProcessStartInfo startInfo = new ProcessStartInfo("/System/Library/CoreServices/Finder.app")
            {
                WindowStyle = ProcessWindowStyle.Normal,
                FileName = location.Trim()
            };
            Process.Start(startInfo);
#else
        Application.OpenURL("file:///" + fileName);
#endif
    }

    public static List<AssemblyJson> ConvertToAssemblyJsonFull(
     List<AssemblyData> assemblyDatas,
     List<AdditionalPdfData> additionalData)
    {
        List<AssemblyJson> allTables = new List<AssemblyJson>();
        int assId = 1;

        foreach (var assemblyData in assemblyDatas)
        {
            var assembly = new AssemblyJson
            {
                AssemblyId = assId++,
                TableName = assemblyData.Title
            };

            List<string> serviceHeadItems = new List<string>();
            List<string> usedServiceHeadItems = new List<string>();

            // First pass: collect service head items
            foreach (var item in assemblyData.OrderedSelectables)
            {
                var metaData = item.GetMetadata();
                string itemName = metaData.Name;

                if (metaData.Categories.Contains("High Voltage Services") ||
                    metaData.Categories.Contains("Low Voltage Services"))
                {
                    var existing = serviceHeadItems.FirstOrDefault(x => x.StartsWith(itemName));
                    if (existing != null)
                    {
                        var count = 1;
                        var match = Regex.Match(existing, @"\((\d+)\)");
                        if (match.Success)
                            count = int.Parse(match.Groups[1].Value);

                        serviceHeadItems.Remove(existing);
                        serviceHeadItems.Add(itemName + $" ({count + 1})");
                    }
                    else
                    {
                        serviceHeadItems.Add(itemName);
                    }
                }
            }

            // Second pass: process all items
            foreach (var item in assemblyData.OrderedSelectables)
            {
                var metaData = item.GetMetadata();
                string itemName = metaData.Name;

                if (!string.IsNullOrWhiteSpace(item.MetaData.SubPartName))
                    itemName += " " + item.MetaData.SubPartName;

                if (metaData.Categories.Contains("Service Head Services") ||
                    metaData.Name.Contains("Blank Plate") ||
                    metaData.Name.Contains("Service Head Rails"))
                    continue;

                if (metaData.Categories.Contains("High Voltage Services") ||
                    metaData.Categories.Contains("Low Voltage Services"))
                {
                    if (!usedServiceHeadItems.Contains(itemName))
                    {
                        assembly.Fields.Add(new PdfField
                        {
                            Item = "Service Head Attachment",
                            Value = serviceHeadItems.First(x => x.StartsWith(itemName))
                        });
                        usedServiceHeadItems.Add(itemName);
                    }
                }
                else if (item.RelatedSelectables[0] == item)
                {
                    foreach (var pdfData in metaData.PdfData)
                    {
                        string value = pdfData.Value.Trim();
                        if (value.Equals("{NAME}", StringComparison.OrdinalIgnoreCase))
                            value = itemName;

                        if (pdfData.Table.Trim().Equals("{ASSEMBLY}", StringComparison.OrdinalIgnoreCase))
                        {
                            assembly.Fields.Add(new PdfField
                            {
                                Item = pdfData.Key,
                                Value = value
                            });
                        }
                        else
                        {
                            var existing = allTables.FirstOrDefault(x => x.TableName == pdfData.Table);
                            if (existing == null)
                            {
                                existing = new AssemblyJson
                                {
                                    TableName = pdfData.Table
                                };
                                allTables.Add(existing);
                            }
                            existing.Fields.Add(new PdfField
                            {
                                Item = pdfData.Key,
                                Value = value
                            });
                        }
                    }
                }

                if (item.ScaleLevels.Count > 0)
                {
                    assembly.Fields.Add(new PdfField
                    {
                        Item = itemName + " length",
                        Value = item.CurrentScaleLevel.Size * 1000f + "mm"
                    });
                }
            }

            allTables.Add(assembly);
        }

        // Add additional data
        foreach (var addTable in additionalData)
        {
            AssemblyJson existing;
            var match = Regex.Match(addTable.Table, @"{(\d)}");

            if (match.Success)
            {
                int id = int.Parse(match.Groups[1].Value);
                existing = allTables.FirstOrDefault(x => x.AssemblyId == id);
            }
            else
            {
                existing = allTables.FirstOrDefault(x => x.TableName == addTable.Table);
            }

            if (existing == null)
            {
                existing = new AssemblyJson
                {
                    TableName = addTable.Table
                };
                allTables.Add(existing);
            }

            foreach (var kvp in addTable.Data)
            {
                existing.Fields.Add(new PdfField
                {
                    Item = kvp.Key,
                    Value = kvp.Value
                });
            }
        }

        return allTables;
    }

}