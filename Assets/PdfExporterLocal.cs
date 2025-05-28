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
using Unity.Burst.Intrinsics;
using Unity.VisualScripting;
using UnityEngine;
using static iTextSharp.awt.geom.Point2D;
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

        string outputPath = Path.Combine(FullRoomSave.GetRoomPath(), "pdf");
        if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

        string fileName = Path.Combine(outputPath, $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

        using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None))
        using (Document doc = new Document(new Rectangle(1400, 1200,90), 19.08f, 19.08f, 10, 10))
        {
            PdfWriter writer = PdfWriter.GetInstance(doc, fs);
            doc.Open();

            AddTitle(doc, title, subtitle);

            // Main layout table (assemblies on left, image on right)
            PdfPTable mainTable = new PdfPTable(2)
            {
                HorizontalAlignment = Element.ALIGN_LEFT,
                WidthPercentage = 100
            };
            mainTable.SetWidths(new float[] { 40, 60 });

            PdfPCell assembliesCell = CreateAssembliesCell(assemblies);
            assembliesCell.PaddingRight = 0;

            PdfPCell imageCell = CreateImageCell(imageData, doc, writer, GetCeilingHeight());
            imageCell.PaddingRight = 50;
            mainTable.AddCell(assembliesCell);
            mainTable.AddCell(imageCell);
            doc.Add(mainTable);

            AddCompanyLogo(doc);
            AddCustomerAcceptanceSection(doc, metaData);
            doc.Close();
        }

        UI_GeneralLoadingScreen.instance.HideLoadingScreen();
   
        UI_DialogPrompt.Open(
     $"Success! PDF saved to {fileName}",
     new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = fileName),
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

        Application.OpenURL("file:///" + fileName);
    }
    public static void RenderSingleConfigPage(
   Document doc,
   PdfWriter writer,
   List<PdfImageData> images,
   string title,
   string subtitle,
   List<AssemblyJson> assemblies,
   ProjectMetaData metadata)
    {
        AddTitle(doc, title, subtitle);

        PdfPTable mainTable = new PdfPTable(2) { WidthPercentage = 100 };
        mainTable.SetWidths(new float[] { 40f, 60f });

        PdfPCell assembliesCell = CreateAssembliesCell(assemblies);
        PdfPCell imageCell = CreateImageCell(images, doc, writer);

        mainTable.AddCell(assembliesCell);
        mainTable.AddCell(imageCell);
        doc.Add(mainTable);

        AddCompanyLogo(doc);
        AddCustomerAcceptanceSection(doc, metadata);
    }
    private static void AddTitle(Document doc, string title, string subtitle)
    {
        BaseFont tekoLight = BaseFont.CreateFont(
            @"Assets/_DevWIP/Faizan/Fonts/Teko/Teko-Light.ttf",
            BaseFont.IDENTITY_H,
            BaseFont.EMBEDDED);
        Font prefixFont = new Font(tekoLight, 36, Font.NORMAL, BaseColor.WHITE);
        Font subtitleFont = FontFactory.GetFont("Arial", 15, BaseColor.WHITE);


        Phrase titlePhrase = new Phrase();
        titlePhrase.Add(new Chunk(title, prefixFont));
        titlePhrase.Add(new Chunk("\n"));
        titlePhrase.Add(new Chunk(subtitle, subtitleFont));

        PdfPCell titleCell = new PdfPCell(titlePhrase)
        {
            BackgroundColor = WebColors.GetRGBColor("#001236"),
            Border = Rectangle.NO_BORDER,
            Padding = 10,
        };

        PdfPTable tbl = new PdfPTable(1) { WidthPercentage = 100 };
        
        tbl.AddCell(titleCell);
        tbl.SpacingAfter = 20;
        doc.Add(tbl);
    }


    private static PdfPCell CreateAssembliesCell(List<AssemblyJson> assemblies)
    {
        PdfPCell container = new PdfPCell
        {
            Border = Rectangle.NO_BORDER,
            PaddingRight = 20f
        };

        Font headerFont = FontFactory.GetFont("Arial", 12, Font.BOLD, BaseColor.WHITE);
        Font serviceheaderFont = FontFactory.GetFont("Arial", 12, Font.BOLD, BaseColor.WHITE);
        Font itemFont = FontFactory.GetFont("Arial", 10, Font.NORMAL, BaseColor.BLACK);
        Font valueFont = FontFactory.GetFont("Arial", 10, Font.NORMAL, BaseColor.BLACK);

        float rowH = 21.6f;
        BaseColor gray = HexToBaseColor("#E5E7EB");
        BaseColor white = BaseColor.WHITE;

        foreach (var asm in assemblies)
        {
            var hdrPara = new Paragraph(asm.TableName, headerFont) { Alignment = Element.ALIGN_LEFT };
            PdfPCell hdrCell = new PdfPCell(hdrPara)
            {
                BackgroundColor = HexToBaseColor("#001236"),
                Border = Rectangle.NO_BORDER,
                Padding = 6,
                HorizontalAlignment = Element.ALIGN_LEFT,
            };
            var hdrTable = new PdfPTable(1) { WidthPercentage = 60, HorizontalAlignment = Element.ALIGN_LEFT };
            hdrTable.AddCell(hdrCell);
            container.AddElement(hdrTable);

            var fldTbl = new PdfPTable(2)
            {
                WidthPercentage = 60f,
                SpacingBefore = 0f,
                SpacingAfter = 8f,
                HorizontalAlignment = Element.ALIGN_LEFT,
            };
            fldTbl.SetWidths(new float[] { 60, 40 });

            var serviceAttachments = asm.Fields.Where(f => f.Item == "Service Head Attachment").ToList();
            var normalFields = asm.Fields.Where(f => f.Item != "Service Head Attachment").ToList();

            bool stripe = true;
            foreach (var f in normalFields)
            {
                var bg = stripe ? gray : white;
                fldTbl.AddCell(new PdfPCell(new Phrase(f.Item, itemFont))
                {
                    BackgroundColor = bg,
                    FixedHeight = rowH,
                    Border = Rectangle.BOX,
                    HorizontalAlignment = Element.ALIGN_LEFT,
                    Padding = 4
                });
                fldTbl.AddCell(new PdfPCell(new Phrase(f.Value, valueFont))
                {
                    BackgroundColor = bg,
                    FixedHeight = rowH,
                    Border = Rectangle.BOX,
                    HorizontalAlignment = Element.ALIGN_LEFT,
                    Padding = 4
                });
                stripe = !stripe;
            }

            if (asm.TableName == "Flat Panel Arm" || asm.TableName == "U | ONE (Standard)" || asm.TableName == "Spring Arm (Low Ceiling)")
            {
                AddAdditionalRow(fldTbl, "Circuits Required", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
                AddAdditionalRow(fldTbl, "Overall Weight", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
                AddAdditionalRow(fldTbl, "Torque Moment", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
                AddAdditionalRow(fldTbl, "Vertical Force Nm", "", rowH, stripe ? gray : white, itemFont);
            }

            if (asm.TableName == "Boom Service Head")
            {
                AddAdditionalRow(fldTbl, "Med-Gas Connection Type", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
                AddAdditionalRow(fldTbl, "Overall Weight", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
                AddAdditionalRow(fldTbl, "Vertical Force", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
                AddAdditionalRow(fldTbl, "Payload Capacity", "", rowH, stripe ? gray : white, itemFont);
            }

            container.AddElement(fldTbl);

            if (serviceAttachments.Count > 0)
            {
                var attachTbl = new PdfPTable(2)
                {
                    WidthPercentage = 60,
                    SpacingBefore = 8f,
                    HorizontalAlignment = Element.ALIGN_LEFT,
                    SpacingAfter = 8f
                };
                attachTbl.SetWidths(new float[] { 50, 50 });

                var subHdr = new PdfPCell(new Phrase("Service Head Details", serviceheaderFont))
                {
                    Colspan = 2,
                    BackgroundColor = WebColors.GetRGBColor("#001236"),
                    Border = Rectangle.NO_BORDER,
                    Padding = 4,
                    HorizontalAlignment = Element.ALIGN_LEFT,
                };
                attachTbl.AddCell(subHdr);

                bool useGray = true;
                foreach (var f in serviceAttachments)
                {
                    var bg = useGray ? gray : white;
                    attachTbl.AddCell(new PdfPCell(new Phrase(f.Item, itemFont))
                    {
                        BackgroundColor = bg,
                        FixedHeight = rowH,
                        Border = Rectangle.BOX,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        Padding = 4
                    });
                    attachTbl.AddCell(new PdfPCell(new Phrase(f.Value, valueFont))
                    {
                        BackgroundColor = bg,
                        FixedHeight = rowH,
                        Border = Rectangle.BOX,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        Padding = 4
                    });
                    useGray = !useGray;
                }

                container.AddElement(attachTbl);
            }

            container.AddElement(new Paragraph(" "));
        }

        return container;
    }
    private static void AddAdditionalRow(PdfPTable table, string itemText, string valueText, float rowHeight, BaseColor backgroundColor,Font itemfomt)
    {
        PdfPCell itemCell = new PdfPCell(new Phrase(itemText,itemfomt));
        PdfPCell valueCell = new PdfPCell(new Phrase(valueText,itemfomt));

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

    private static PdfPCell CreateImageCell(
        List<PdfImageData> imageData,
        Document doc,
        PdfWriter writer,
        float roomHeight = 300f)
    {
        // 1) compute all our metrics
        float pageWidth = doc.PageSize.Width;
        float usablePageWidth = pageWidth - (doc.LeftMargin + doc.RightMargin);
        float imageColumnWidth = usablePageWidth * 0.6f;
        float paddingBetweenImages = 10f;
        float availableImageWidth = (imageColumnWidth - paddingBetweenImages) / 2f;
        float maxTargetHeight = 300f;
        float scale1 = 100f;  // pts per meter
        float visualHeight = GetCeilingHeight() * scale1;

        // 2) prepare container cell
        PdfPCell imageCell = new PdfPCell
        {
            Border = Rectangle.NO_BORDER,
            VerticalAlignment = Element.ALIGN_BOTTOM,
            HorizontalAlignment = Element.ALIGN_CENTER,
            PaddingLeft = 20f
        };

        // 3) build sub-elements
        var cb = writer.DirectContent;
        Image heightImg = BuildHeightImage(cb, visualHeight, maxTargetHeight);
        PdfPTable content = BuildContentTable(heightImg, imageData, availableImageWidth, maxTargetHeight);
        Image beamImg = BuildBeamImage(cb, usablePageWidth, 10);

        // 4) assemble outer table
        PdfPTable outer = new PdfPTable(1) { WidthPercentage = 100f };
        outer.DefaultCell.Border = Rectangle.NO_BORDER;
        outer.DefaultCell.Padding = 0f;

        outer.AddCell(new PdfPCell(content)
        {
            Border = Rectangle.NO_BORDER,
            Padding = 0f
        });

        outer.AddCell(new PdfPCell(beamImg)
        {
            Border = Rectangle.NO_BORDER,
            Padding = 0f,
            HorizontalAlignment = Element.ALIGN_LEFT,
            VerticalAlignment = Element.ALIGN_TOP
        });

        imageCell.AddElement(outer);
        return imageCell;
    }

    private static Image BuildHeightImage(
        PdfContentByte cb,
        float visualHeight,
        float maxTargetHeight)
    {
        // draw the vertical line + ticks
        PdfTemplate tpl = cb.CreateTemplate(50, visualHeight);
        tpl.SetLineWidth(1.5f);

        // main line
        tpl.MoveTo(10, 0);
        tpl.LineTo(10, visualHeight);
        tpl.Stroke();

        // bottom tick
        tpl.MoveTo(5, 0);
        tpl.LineTo(15, 0);
        tpl.Stroke();

        // top tick
        tpl.MoveTo(5, visualHeight);
        tpl.LineTo(15, visualHeight);
        tpl.Stroke();

        // calculate distance string
        float meters = GetCeilingHeight();
        float ft = Mathf.Floor(meters.ToFeet());
        float inch = Mathf.Round((meters.ToFeet() - ft) * 12f * 10f) / 10f;
        Distance = $"{ft}' {inch}\"";

        // draw the text
        BaseFont teko = BaseFont.CreateFont(
            @"Assets/_DevWIP/Faizan/Fonts/Teko/Teko-Light.ttf",
            BaseFont.IDENTITY_H,
            BaseFont.EMBEDDED);

        tpl.BeginText();
        tpl.SetFontAndSize(teko, 36);
        tpl.ShowTextAligned(
            Element.ALIGN_LEFT,
            Distance,
            40,                   // X pos of text (same as line)
            maxTargetHeight / 2,  // vertically centered
            90);
        tpl.EndText();

        return Image.GetInstance(tpl);
    }

    private static PdfPTable BuildContentTable(
        Image heightImg,
        List<PdfImageData> imageData,
        float availableImageWidth,
        float maxTargetHeight)
    {
        // 2-col table: [ height-marker | image table ]
        PdfPTable table = new PdfPTable(2) { WidthPercentage = 100f };
        table.SetWidths(new float[] { 10f, 90f });

        // height cell
        PdfPCell hCell = new PdfPCell(heightImg)
        {
            Border = Rectangle.NO_BORDER,
            Padding = 0f,
            VerticalAlignment = Element.ALIGN_MIDDLE
        };
        table.AddCell(hCell);

        // nested 2-col table for front/back images
        PdfPTable imgs = new PdfPTable(2) { WidthPercentage = 100f };
        float maxH = maxTargetHeight;
        Font captionFont = FontFactory.GetFont("Arial", 10, BaseColor.BLACK);

        for (int i = 0; i < 2; i++)
        {
            PdfPCell cell = new PdfPCell
            {
                Border = Rectangle.NO_BORDER,
                HorizontalAlignment = Element.ALIGN_CENTER,
                VerticalAlignment = Element.ALIGN_BOTTOM,
                Padding = 0f
            };

            if (imageData.Count > i && File.Exists(imageData[i].Path))
            {
                Image img = Image.GetInstance(imageData[i].Path);
                img.Alignment = Element.ALIGN_BOTTOM;

                // scale to fit
                float scale = Math.Min(
                    availableImageWidth / img.Width,
                    maxH / img.Height);
                img.ScaleAbsolute(img.Width * scale, img.Height * scale);

                cell.AddElement(img);
            }

            imgs.AddCell(cell);
        }

        // wrap the image-grid
        PdfPCell wrap = new PdfPCell(imgs)
        {
            Border = Rectangle.NO_BORDER,
            Padding = 0f
        };
        table.AddCell(wrap);

        return table;
    }

    // 1) Updated helper signature to accept leftMargin
    private static Image BuildBeamImage(
        PdfContentByte cb,
        float usablePageWidth,
        float leftMargin)
    {
        // total template width = margin + usable drawing width
        float totalWidth = leftMargin + usablePageWidth;
        PdfTemplate beam = cb.CreateTemplate(totalWidth, 100f);

        float beamY = 100f;
        float supportH = 20f;
        float spacing = usablePageWidth / 14f;

        beam.SetLineWidth(3f);

        // main beam line, shifted right by leftMargin
        beam.MoveTo(leftMargin, beamY);
        beam.LineTo(leftMargin + usablePageWidth, beamY);
        beam.Stroke();

        // supports, likewise offset
        for (int i = 0; i <= 14; i++)
        {
            float x = leftMargin + (i * spacing);
            beam.MoveTo(x, beamY);
            beam.LineTo(x - supportH, beamY - supportH);
            beam.Stroke();
        }

        // turn into an Image and scale
        Image img = Image.GetInstance(beam);
        img.ScaleToFit(totalWidth, 100f);
        return img;
    }

    private static float GetCeilingHeight() => RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).Height;

    private static void AddCompanyLogo(Document doc)
    {
        PdfPTable logoTable = new PdfPTable(1) { TotalWidth = 300, HorizontalAlignment = Element.ALIGN_RIGHT, LockedWidth = true };
        PdfPCell logoCell = new PdfPCell { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_LEFT, PaddingBottom = 10f };
        Image logo = Image.GetInstance(Application.streamingAssetsPath + "/Data/quotes/UImagineUnlimited-logo.png");
        logo.ScaleToFit(300, 300);
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

    public static BaseColor HexToBaseColor(string hex, int alpha = 255)
    {
        if (string.IsNullOrEmpty(hex)) return BaseColor.WHITE;
        hex = hex.Replace("#", "");
        int r = int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
        int g = int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
        int b = int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
        return new BaseColor(r, g, b, alpha);
    }

    private static void OpenPdfFile(string fileName)
    {
        try
        {
            Process.Start(new ProcessStartInfo(fileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Failed to open PDF: {ex.Message}");
        }
    }

    // Refactored ConvertToAssemblyJsonFull with fix for duplicated service head fields
    // Refactored ConvertToAssemblyJsonFull with fix to include Service Head Rails in attachments only
    public static List<AssemblyJson> ConvertToAssemblyJsonFull(
      List<AssemblyData> assemblyDatas,
      List<AdditionalPdfData> additionalData)
    {
        List<AssemblyJson> allTables = new();
        int assId = 1;

        foreach (var assemblyData in assemblyDatas)
        {
            var assembly = new AssemblyJson
            {
                AssemblyId = assId++,
                TableName = assemblyData.Title
            };

            Dictionary<string, int> serviceHeadItemCounts = new();
            List<string> usedServiceHeadItems = new();

            // First pass — count service head attachments
            foreach (var item in assemblyData.OrderedSelectables)
            {
                var meta = item.GetMetadata();
                string itemName = NormalizeItemName(meta);

                if (IsServiceHeadAttachment(meta))
                {
                    if (serviceHeadItemCounts.ContainsKey(itemName))
                        serviceHeadItemCounts[itemName]++;
                    else
                        serviceHeadItemCounts[itemName] = 1;
                }
            }

            // Second pass — process all fields
            foreach (var item in assemblyData.OrderedSelectables)
            {
                var meta = item.GetMetadata();
                string itemName = NormalizeItemName(meta);

                if (meta.Name.Contains("Blank Plate")) continue;

                bool isServiceHead = IsServiceHeadAttachment(meta);

                if (isServiceHead && !usedServiceHeadItems.Contains(itemName))
                {
                    string label = serviceHeadItemCounts[itemName] > 1 ? $"{itemName} ({serviceHeadItemCounts[itemName]})" : itemName;
                    assembly.Fields.Add(new PdfField { Item = "Service Head Attachment", Value = label });
                    usedServiceHeadItems.Add(itemName);
                    continue;
                }

                if (item.RelatedSelectables[0] == item)
                {
                    foreach (var pdf in meta.PdfData)
                    {
                        string value = pdf.Value.Trim();
                        if (value.Equals("{NAME}", StringComparison.OrdinalIgnoreCase))
                            value = itemName;

                        if (pdf.Table.Trim().Equals("{ASSEMBLY}", StringComparison.OrdinalIgnoreCase))
                        {
                            assembly.Fields.Add(new PdfField { Item = pdf.Key, Value = value });
                        }
                        else
                        {
                            var external = allTables.FirstOrDefault(x => x.TableName == pdf.Table);
                            if (external == null)
                            {
                                external = new AssemblyJson { TableName = pdf.Table };
                                allTables.Add(external);
                            }
                            external.Fields.Add(new PdfField { Item = pdf.Key, Value = value });
                        }
                    }
                }

                bool requiresLengthManually =
     itemName.Contains("Spring XL") ||
     itemName.Contains("Spring") ||
     itemName.Contains("Powered XL") ||
     itemName.Contains("Powered") ||
     itemName.Contains("Fixed");

                bool lengthAlreadyAdded = assembly.Fields.Any(f => f.Item == itemName + " length");

                // Add only if it's not already added
                if (!lengthAlreadyAdded)
                {
                    float size = item.CurrentScaleLevel?.Size ?? 0f;

                    // If size is still 0 and it's a special case, force it to 1m (1000mm)
                    if (size == 0f && requiresLengthManually)
                    {
                        size = 1f; // default 1000mm
                    }

                    // Only add if size is meaningful
                    if (size > 0f)
                    {
                        assembly.Fields.Add(new PdfField
                        {
                            Item = itemName + " length",
                            Value = (size * 1000f).ToString("F0") + "mm"
                        });
                    }
                }

            }

            allTables.Add(assembly);
        }

        // Add additional tables
        foreach (var addTable in additionalData)
        {
            AssemblyJson existing;
            var match = Regex.Match(addTable.Table, "\\{(\\d+)\\}");

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
                existing = new AssemblyJson { TableName = addTable.Table };
                allTables.Add(existing);
            }

            foreach (var kvp in addTable.Data)
            {
                existing.Fields.Add(new PdfField { Item = kvp.Key, Value = kvp.Value });
            }
        }

        return allTables;
    }



    private static string NormalizeItemName(SelectableMetaData meta)
    {
        return string.IsNullOrWhiteSpace(meta.SubPartName)
            ? meta.Name.Trim()
            : $"{meta.Name.Trim()} {meta.SubPartName.Trim()}";
    }

    private static bool IsServiceHeadAttachment(SelectableMetaData meta)
    {
        return meta.Categories.Contains("High Voltage Services") ||
               meta.Categories.Contains("Low Voltage Services") ||
               meta.Categories.Contains("Service Head Rails") ||
               meta.Categories.Contains("Service Head Shelf (500mm)") ||
               meta.Name.Contains("Service Head Shelf (500mm)") ||
               meta.Name.Contains("SHP_Rails");
    }

}
