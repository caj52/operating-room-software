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
using UnityEngine.Rendering.Universal;
using static iTextSharp.awt.geom.Point2D;
using static Measurable;
using Font = iTextSharp.text.Font;

public class PdfExporterLocal
{
    // Memory optimization: Static font cache to prevent repeated font loading
    private static readonly Dictionary<string, BaseFont> FontCache = new Dictionary<string, BaseFont>();
    private static readonly Dictionary<string, Font> CachedFonts = new Dictionary<string, Font>();

    // Memory optimization: Template cache for reuse
    private static readonly Dictionary<string, PdfTemplate> TemplateCache = new Dictionary<string, PdfTemplate>();

    // Memory monitoring
    private static long InitialMemory;
    private static long PeakMemory;

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

    private static BaseFont GetCachedBaseFont(string fontPath)
    {
        if (!FontCache.ContainsKey(fontPath))
        {
            try
            {
                if (!File.Exists(fontPath))
                    throw new FileNotFoundException("Font file not found: " + fontPath);

                // Always use Unicode encoding with embedded font
                FontCache[fontPath] = BaseFont.CreateFont(fontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Font loading failed for {fontPath}: {ex.Message}. Using embedded fallback.");

                try
                {
                    // Use a fallback font that you know works well with IDENTITY_H
                    string fallbackFontPath = Path.Combine(Application.streamingAssetsPath, "Fonts", "Arial.ttf");

                    if (File.Exists(fallbackFontPath))
                    {
                        FontCache[fontPath] = BaseFont.CreateFont(fallbackFontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
                    }
                    else
                    {
                        // Use iTextSharp's built-in Helvetica (very limited range)
                        FontCache[fontPath] = BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED);
                    }
                }
                catch (Exception fallbackEx)
                {
                    UnityEngine.Debug.LogError($"All font loading attempts failed: {fallbackEx.Message}");
                    throw;
                }
            }
        }

        return FontCache[fontPath];
    }

    // Memory optimization: Get cached font instances
    private static Font GetCachedFont(string key, string fontName, float size, int style, BaseColor color)
    {
        if (!CachedFonts.ContainsKey(key))
        {
            CachedFonts[key] = FontFactory.GetFont(fontName, size, style, color);
        }
        return CachedFonts[key];
    }

    // Memory monitoring helper
    private static void MonitorMemory(string operation)
    {
        long currentMemory = GC.GetTotalMemory(false);
        if (currentMemory > PeakMemory)
        {
            PeakMemory = currentMemory;
        }
        UnityEngine.Debug.Log($"Memory after {operation}: {currentMemory / 1024 / 1024}MB (Peak: {PeakMemory / 1024 / 1024}MB)");
    }

    public static void ExportElevationPdfLocal(
        List<PdfImageData> imageData,
        string title,
        string subtitle,
        List<AssemblyJson> assemblies,
        ProjectMetaData metaData)
    {
        // Memory optimization: Initialize memory monitoring
        InitialMemory = GC.GetTotalMemory(true); // Force GC before starting
        PeakMemory = InitialMemory;

        UI_GeneralLoadingScreen.instance.ShowLoadingScreen();

        string outputPath = Path.Combine(FullRoomSave.GetRoomPath(), "pdf");
        if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

        string fileName = Path.Combine(outputPath, $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

        // Memory optimization: Use buffered file stream with smaller buffer
        using (var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None, 4096))
        using (var bufferedStream = new BufferedStream(fileStream, 4096))
        using (var document = new Document(new Rectangle(1400, 1200, 90), 19.08f, 19.08f, 10, 10))
        {
            PdfWriter writer = null;
            try
            {
                writer = PdfWriter.GetInstance(document, bufferedStream);
                writer.CloseStream = false; // Prevent premature stream disposal

                document.Open();
                MonitorMemory("Document opened");

                AddTitleOptimized(document, title, subtitle);
                MonitorMemory("Title added");

                // Memory optimization: Process in smaller chunks and trigger GC between operations
                AddMainContentOptimized(document, writer, assemblies, imageData);

                // Force garbage collection before final operations
                GC.Collect();
                GC.WaitForPendingFinalizers();
                MonitorMemory("Main content added");

                AddCompanyLogoOptimized(document);
                AddCustomerAcceptanceSectionOptimized(document, metaData);

                document.Close();
                MonitorMemory("Document closed");
            }
            finally
            {
                // Memory optimization: Explicit cleanup
                if (writer != null)
                {
                    CleanupPdfWriter(writer);
                    writer?.Close();
                }
            }
        }

        // Final memory cleanup
        CleanupResources();
        UI_GeneralLoadingScreen.instance.HideLoadingScreen();

        UI_DialogPrompt.Open(
            $"Success! PDF saved to {fileName}",
            new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = fileName),
            new ButtonAction("Done"));

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        string location = fileName;
        ProcessStartInfo startInfo = new ProcessStartInfo("/System/Library/CoreServices/Finder.app")
        {
            WindowStyle = ProcessWindowStyle.Normal,
            FileName = location.Trim()
        };
        Process.Start(startInfo);
#endif

        Application.OpenURL("file:///" + fileName);
    }

    // Memory optimized: Process content in chunks
    private static void AddMainContentOptimized(Document doc, PdfWriter writer, List<AssemblyJson> assemblies, List<PdfImageData> imageData)
    {
        // Main layout table (assemblies on left, image on right)
        PdfPTable mainTable = new PdfPTable(2)
        {
            HorizontalAlignment = Element.ALIGN_LEFT,
            WidthPercentage = 100
        };
        mainTable.SetWidths(new float[] { 40, 60 });

        // Process assemblies in smaller chunks to reduce memory pressure
        PdfPCell assembliesCell = CreateAssembliesCellOptimized(assemblies);
        assembliesCell.PaddingRight = 0;

        PdfPCell imageCell = CreateImageCellOptimized(imageData, doc, writer, GetCeilingHeight());
        imageCell.PaddingRight = 50;

        mainTable.AddCell(assembliesCell);
        mainTable.AddCell(imageCell);
        doc.Add(mainTable);

        // Clear references immediately
        mainTable = null;
        assembliesCell = null;
        imageCell = null;
    }

    // Memory optimized title creation
    private static void AddTitleOptimized(Document doc, string title, string subtitle)
    {
        // Fonts
        var fontPath = Path.Combine(Application.streamingAssetsPath, "Data/Fonts/Teko/Teko-Light.ttf");
        BaseFont tekoLight = GetCachedBaseFont(fontPath);

        Font prefixFont = new Font(tekoLight, 36, Font.NORMAL, BaseColor.WHITE);
        Font subtitleFont = GetCachedFont("subtitle", "Arial", 15, Font.NORMAL, BaseColor.WHITE);

        // Paragraphs (title + subtitle)
        Paragraph pTitle = new Paragraph(title, prefixFont);
        pTitle.SpacingAfter = 6f;              // gap between title and subtitle
        pTitle.SetLeading(0f, 1.0f);           // normal line height for multi-line titles

        Paragraph pSubtitle = new Paragraph(subtitle, subtitleFont);

        // Cell with both paragraphs
        PdfPCell titleCell = new PdfPCell
        {
            BackgroundColor = WebColors.GetRGBColor("#001236"),
            Border = Rectangle.NO_BORDER,
            Padding = 10f
        };
        titleCell.AddElement(pTitle);
        titleCell.AddElement(pSubtitle);

        // Single-column table wrapper
        PdfPTable tbl = new PdfPTable(1) { WidthPercentage = 100f };
        tbl.AddCell(titleCell);
        tbl.SpacingAfter = 20f;

        doc.Add(tbl);

        // Clear references (optional)
        pTitle = null;
        pSubtitle = null;
        titleCell = null;
        tbl = null;
    }

    // Memory optimized assemblies processing
    private static PdfPCell CreateAssembliesCellOptimized(List<AssemblyJson> assemblies)
    {
        PdfPCell container = new PdfPCell
        {
            Border = Rectangle.NO_BORDER,
            PaddingRight = 20f
        };

        // Cache fonts to prevent repeated creation
        Font headerFont = GetCachedFont("header", "Arial", 12, Font.BOLD, BaseColor.WHITE);
        Font serviceheaderFont = GetCachedFont("serviceheader", "Arial", 12, Font.BOLD, BaseColor.WHITE);
        Font itemFont = GetCachedFont("item", "Arial", 10, Font.NORMAL, BaseColor.BLACK);
        Font valueFont = GetCachedFont("value", "Arial", 10, Font.NORMAL, BaseColor.BLACK);

        float rowH = 21.6f;
        BaseColor gray = HexToBaseColor("#E5E7EB");
        BaseColor white = BaseColor.WHITE;

        // Process assemblies in batches to reduce memory pressure
        const int batchSize = 5; // Process 5 assemblies at a time
        for (int batchStart = 0; batchStart < assemblies.Count; batchStart += batchSize)
        {
            int batchEnd = Math.Min(batchStart + batchSize, assemblies.Count);

            for (int i = batchStart; i < batchEnd; i++)
            {
                var asm = assemblies[i];
                ProcessSingleAssembly(container, asm, headerFont, serviceheaderFont, itemFont, valueFont, rowH, gray, white);
            }

            // Force garbage collection between batches if memory is getting high
            long currentMemory = GC.GetTotalMemory(false);
            if (currentMemory > InitialMemory * 2) // If memory doubled
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }


        // === Single global footer row: "Cieling Height" (once after all tables) ===
        try
        {
            float metersCH = GetCeilingHeight();
            float feetWholeCH = Mathf.Floor(metersCH.ToFeet());
            float inchesCH = Mathf.Round((metersCH.ToFeet() - feetWholeCH) * 12f * 10f) / 10f;
            string ceilingTextCH = $"{metersCH:F2} m ({feetWholeCH}' {inchesCH}\")";

            var chTable = new PdfPTable(2)
            {
                WidthPercentage = 60f,
                SpacingBefore = 0f,
                SpacingAfter = 8f,
                HorizontalAlignment = Element.ALIGN_LEFT,
            };
            chTable.SetWidths(new float[] { 60, 40 });

            var chLabelCell = new PdfPCell(new Phrase("Cieling Height", itemFont))
            {
                BackgroundColor = white,
                FixedHeight = rowH,
                Border = Rectangle.BOX,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 4
            };
            var chValueCell = new PdfPCell(new Phrase(ceilingTextCH, valueFont))
            {
                BackgroundColor = white,
                FixedHeight = rowH,
                Border = Rectangle.BOX,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 4
            };

            chTable.AddCell(chLabelCell);
            chTable.AddCell(chValueCell);

            container.AddElement(chTable);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogWarning($"Failed adding Cieling Height footer: {ex.Message}");
        }
        return container;
    }

    // Helper method to process individual assembly
    private static void ProcessSingleAssembly(PdfPCell container, AssemblyJson asm, Font headerFont, Font serviceheaderFont, Font itemFont, Font valueFont, float rowH, BaseColor gray, BaseColor white)
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

        // Memory optimization: Process fields in smaller chunks
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

        // Add conditional rows based on table name
        AddConditionalRows(fldTbl, asm.TableName, rowH, gray, white, itemFont, ref stripe);
        container.AddElement(fldTbl);

        // Process service attachments if any
        if (serviceAttachments.Count > 0)
        {
            ProcessServiceAttachments(container, serviceAttachments, serviceheaderFont, itemFont, valueFont, rowH, gray, white);
        }

        container.AddElement(new Paragraph(" "));

        // Clear local references
        hdrPara = null;
        hdrCell = null;
        hdrTable = null;
        fldTbl = null;
    }

    private static void AddConditionalRows(PdfPTable fldTbl, string tableName, float rowH, BaseColor gray, BaseColor white, Font itemFont, ref bool stripe)
    {
        if (tableName == "Flat Panel Arm" || tableName == "Lights - U | ONE" || tableName == "Spring Arm (Low Ceiling)")
        {
            AddAdditionalRow(fldTbl, "Circuits Required", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Overall Weight", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Torque Moment", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Vertical Force Nm", "", rowH, stripe ? gray : white, itemFont);
        }

        if (tableName.Contains("Boom - Service Head"))
        {
            AddAdditionalRow(fldTbl, "Med-Gas Connection Type", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Overall Weight", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Vertical Force", "", rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Payload Capacity", "", rowH, stripe ? gray : white, itemFont);
        }
    }

    private static void ProcessServiceAttachments(PdfPCell container, List<PdfField> serviceAttachments, Font serviceheaderFont, Font itemFont, Font valueFont, float rowH, BaseColor gray, BaseColor white)
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

    private static void AddAdditionalRow(PdfPTable table, string itemText, string valueText, float rowHeight, BaseColor backgroundColor, Font itemFont)
    {
        PdfPCell itemCell = new PdfPCell(new Phrase(itemText, itemFont));
        PdfPCell valueCell = new PdfPCell(new Phrase(valueText, itemFont));

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

    // Memory optimized image cell creation

    private static PdfPCell CreateImageCellOptimized(
            List<PdfImageData> imageData,
            Document doc,
            PdfWriter writer,
            float roomHeight = 300f)
    {
        float pageWidth = doc.PageSize.Width;
        float usablePageWidth = pageWidth - (doc.LeftMargin + doc.RightMargin);
        float imageColumnWidth = usablePageWidth * 0.6f;
        float paddingBetweenImages = 10f;
        float availableImageWidth = (imageColumnWidth - paddingBetweenImages) / 2f;
        float maxTargetHeight = 300f;

        PdfPCell imageCell = new PdfPCell
        {
            Border = Rectangle.NO_BORDER,
            VerticalAlignment = Element.ALIGN_BOTTOM,
            HorizontalAlignment = Element.ALIGN_CENTER,
            PaddingLeft = 20f
        };

        // Only show main images (no side height ruler)
        PdfPTable imgs = new PdfPTable(2) { WidthPercentage = 100f };
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
                try
                {
                    Image img = Image.GetInstance(imageData[i].Path);
                    img.Alignment = Element.ALIGN_BOTTOM;

                    float scale = Math.Min(availableImageWidth / img.Width, maxTargetHeight / img.Height);
                    img.ScaleAbsolute(img.Width * scale, img.Height * scale);

                    cell.AddElement(img);
                    img = null;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Failed to load image {imageData[i].Path}: {ex.Message}\"");
                }
            }

            imgs.AddCell(cell);
        }

        // Wrap images + beam in an outer single-column table
        var outer = new PdfPTable(1) { WidthPercentage = 100f };
        outer.DefaultCell.Border = Rectangle.NO_BORDER;
        outer.DefaultCell.Padding = 0f;

        outer.AddCell(new PdfPCell(imgs)
        {
            Border = Rectangle.NO_BORDER,
            Padding = 0f
        });

        // Add the bottom beam back
        var cb = writer.DirectContent;
        Image beamImg = BuildBeamImageOptimized(cb, usablePageWidth, 10f);

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
    private static Image BuildHeightImageOptimized(PdfContentByte cb, float visualHeight, float maxTargetHeight)
    {
        string cacheKey = $"height_{visualHeight}_{maxTargetHeight}";

        if (TemplateCache.ContainsKey(cacheKey))
        {
            return Image.GetInstance(TemplateCache[cacheKey]);
        }

        PdfTemplate tpl = cb.CreateTemplate(50, visualHeight);
        tpl.SetLineWidth(1.5f);

        // Main line
        tpl.MoveTo(10, 0);
        tpl.LineTo(10, visualHeight);
        tpl.Stroke();

        // Bottom tick
        tpl.MoveTo(5, 0);
        tpl.LineTo(15, 0);
        tpl.Stroke();

        // Top tick
        tpl.MoveTo(5, visualHeight);
        tpl.LineTo(15, visualHeight);
        tpl.Stroke();

        // Calculate distance string
        float meters = GetCeilingHeight();
        float ft = Mathf.Floor(meters.ToFeet());
        float inch = Mathf.Round((meters.ToFeet() - ft) * 12f * 10f) / 10f;
        Distance = $"{ft}' {inch}\"";

        var fontPath = Path.Combine(Application.streamingAssetsPath, "Data/Fonts/Teko/Teko-Regular.ttf");
        // Draw the text with cached font
        BaseFont teko = GetCachedBaseFont(fontPath);

        tpl.BeginText();
        tpl.SetFontAndSize(teko, 36);
        tpl.ShowTextAligned(
            Element.ALIGN_LEFT,
            Distance,
            40,
            maxTargetHeight / 2,
            90);
        tpl.EndText();

        // Cache the template for reuse
        TemplateCache[cacheKey] = tpl;

        return Image.GetInstance(tpl);
    }

    // Memory optimized content table building
    private static PdfPTable BuildContentTableOptimized(Image heightImg, List<PdfImageData> imageData, float availableImageWidth, float maxTargetHeight)
    {
        PdfPTable table = new PdfPTable(2) { WidthPercentage = 100f };
        table.SetWidths(new float[] { 10f, 90f });

        PdfPCell hCell = new PdfPCell(heightImg)
        {
            Border = Rectangle.NO_BORDER,
            Padding = 0f,
            VerticalAlignment = Element.ALIGN_MIDDLE
        };
        table.AddCell(hCell);

        PdfPTable imgs = new PdfPTable(2) { WidthPercentage = 100f };
        float maxH = maxTargetHeight;

        // Memory optimization: Process images with proper scaling and disposal
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
                try
                {
                    Image img = Image.GetInstance(imageData[i].Path);
                    img.Alignment = Element.ALIGN_BOTTOM;

                    // Memory optimization: Pre-scale images to reduce memory usage
                    float scale = Math.Min(availableImageWidth / img.Width, maxH / img.Height);
                    img.ScaleAbsolute(img.Width * scale, img.Height * scale);

                    cell.AddElement(img);

                    // Clear image reference immediately
                    img = null;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Failed to load image {imageData[i].Path}: {ex.Message}");
                }
            }

            imgs.AddCell(cell);
        }

        PdfPCell wrap = new PdfPCell(imgs)
        {
            Border = Rectangle.NO_BORDER,
            Padding = 0f
        };
        table.AddCell(wrap);

        return table;
    }

    // Memory optimized beam image building
    private static Image BuildBeamImageOptimized(PdfContentByte cb, float usablePageWidth, float leftMargin)
    {
        string cacheKey = $"beam_{usablePageWidth}_{leftMargin}";

        if (TemplateCache.ContainsKey(cacheKey))
        {
            return Image.GetInstance(TemplateCache[cacheKey]);
        }

        float totalWidth = leftMargin + usablePageWidth;
        PdfTemplate beam = cb.CreateTemplate(totalWidth, 100f);

        float beamY = 100f;
        float supportH = 20f;
        float spacing = usablePageWidth / 14f;

        beam.SetLineWidth(3f);

        // Main beam line
        beam.MoveTo(leftMargin, beamY);
        beam.LineTo(leftMargin + usablePageWidth, beamY);
        beam.Stroke();

        // Supports
        for (int i = 0; i <= 14; i++)
        {
            float x = leftMargin + (i * spacing);
            beam.MoveTo(x, beamY);
            beam.LineTo(x - supportH, beamY - supportH);
            beam.Stroke();
        }

        // Cache for reuse
        TemplateCache[cacheKey] = beam;

        Image img = Image.GetInstance(beam);
        img.ScaleToFit(totalWidth, 100f);
        return img;
    }

    private static float GetCeilingHeight() => RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).Height;

    // Memory optimized logo addition
    private static void AddCompanyLogoOptimized(Document doc)
    {
        try
        {
            string logoPath = Application.streamingAssetsPath + "/Data/quotes/UImagineUnlimited-logo.png";
            if (!File.Exists(logoPath))
            {
                UnityEngine.Debug.LogWarning($"Logo file not found: {logoPath}");
                return;
            }

            PdfPTable logoTable = new PdfPTable(1) { TotalWidth = 300, HorizontalAlignment = Element.ALIGN_RIGHT, LockedWidth = true };
            PdfPCell logoCell = new PdfPCell { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_LEFT, PaddingBottom = 10f };

            Image logo = Image.GetInstance(logoPath);
            logo.ScaleToFit(300, 300);
            logoCell.AddElement(logo);
            logoTable.AddCell(logoCell);
            doc.Add(logoTable);

            // Clear references
            logo = null;
            logoCell = null;
            logoTable = null;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Failed to add company logo: {ex.Message}");
        }
    }

    // Memory optimized customer acceptance section
    private static void AddCustomerAcceptanceSectionOptimized(Document doc, ProjectMetaData metaData)
    {
        Font normalFont = GetCachedFont("normal", FontFactory.HELVETICA, 10, Font.NORMAL, BaseColor.BLACK);
        Font notesFont = GetCachedFont("notes", FontFactory.HELVETICA_OBLIQUE, 10, Font.NORMAL, BaseColor.BLACK);

        PdfPTable acceptanceTable = new PdfPTable(2);
        acceptanceTable.TotalWidth = 800;
        acceptanceTable.HorizontalAlignment = Element.ALIGN_RIGHT;
        acceptanceTable.LockedWidth = true;
        acceptanceTable.SetWidths(new float[] { 60, 40 });

        // Left side
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

        PdfPCell dateCell = new PdfPCell(new Phrase("Date", normalFont));
        dateCell.FixedHeight = 30f;
        dateCell.VerticalAlignment = Element.ALIGN_BOTTOM;
        dateCell.Border = Rectangle.TOP_BORDER;

        signatureTable.AddCell(signatureCell);
        signatureTable.AddCell(dateCell);
        leftCell.AddElement(signatureTable);

        acceptanceTable.AddCell(leftCell);

        // Right side
        PdfPTable detailTable = new PdfPTable(1);
        detailTable.WidthPercentage = 100;

        string[] detailLabels = {
            "Account Name: " + (metaData.AccountName ?? ""),
            "Account Address: " + (metaData.AccountAddressLine1 ?? ""),
            " " + (metaData.AccountAddressLine2 ?? ""),
            "Project Name: " + (metaData.ProjectName ?? ""),
            "Project #: " + (metaData.ProjectNumber ?? ""),
            "Order Reference #: " + (metaData.OrderReferenceNumber ?? "")
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

        // Clear references
        leftCell = null;
        rightCell = null;
        acceptanceTable = null;
        detailTable = null;
        signatureTable = null;
    }

    // Memory optimization: Cleanup method for PdfWriter resources
    private static void CleanupPdfWriter(PdfWriter writer)
    {
        try
        {
            // Release all cached templates
            foreach (var template in TemplateCache.Values)
            {
                try
                {
                    writer.ReleaseTemplate(template);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"Failed to release template: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Error during PdfWriter cleanup: {ex.Message}");
        }
    }

    // Memory optimization: Clear all cached resources
    public static void CleanupResources()
    {
        try
        {
            // Clear template cache
            TemplateCache.Clear();

            // Clear font caches
            FontCache.Clear();
            CachedFonts.Clear();

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            UnityEngine.Debug.Log($"Memory cleaned. Final memory: {GC.GetTotalMemory(false) / 1024 / 1024}MB");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"Error during resource cleanup: {ex.Message}");
        }
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

    // Memory optimized version of ConvertToAssemblyJsonFull
    public static List<AssemblyJson> ConvertToAssemblyJsonFull(
        List<AssemblyData> assemblyDatas,
        List<AdditionalPdfData> additionalData)
    {
        List<AssemblyJson> allTables = new();
        int assId = 1;

        // Memory optimization: Process assemblies in batches
        const int batchSize = 10;
        for (int batchStart = 0; batchStart < assemblyDatas.Count; batchStart += batchSize)
        {
            int batchEnd = Math.Min(batchStart + batchSize, assemblyDatas.Count);

            for (int i = batchStart; i < batchEnd; i++)
            {
                var assemblyData = assemblyDatas[i];
                var assembly = ProcessSingleAssemblyData(assemblyData, assId++);
                allTables.Add(assembly);
            }

            // Force GC between batches if memory is getting high
            long currentMemory = GC.GetTotalMemory(false);
            if (currentMemory > InitialMemory * 1.5f)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // Process additional data
        ProcessAdditionalData(allTables, additionalData);

        return allTables;
    }

    // Helper method to process single assembly data
    private static AssemblyJson ProcessSingleAssemblyData(AssemblyData assemblyData, int assId)
    {
        var assembly = new AssemblyJson
        {
            AssemblyId = assId,
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
                    // Note: External table processing removed to reduce memory complexity
                }
            }

            bool lengthAlreadyAdded = assembly.Fields.Any(f => f.Item == itemName + " length");

            if (!lengthAlreadyAdded)
            {
                float size = item.CurrentScaleLevel?.Size ?? 0f;

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

        return assembly;
    }

    // Helper method to process additional data
    private static void ProcessAdditionalData(List<AssemblyJson> allTables, List<AdditionalPdfData> additionalData)
    {
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
    }

    private static string NormalizeItemName(SelectableMetaData meta)
    {
        return string.IsNullOrWhiteSpace(meta.SubPartName)
            ? meta.Name.Trim()
            : $"{meta.Name.Trim()} {meta.SubPartName.Trim()}";
    }

    private static bool IsServiceHeadAttachment(SelectableMetaData meta)
    {
        return meta.Categories.Contains("Boom - SH High Voltage") ||
               meta.Categories.Contains("Boom - SH Low Voltage") ||
               meta.Categories.Contains("Boom - SH Accessories") ||
               meta.Categories.Contains("Service Head Shelf (500mm)") ||
               meta.Name.Contains("Service Head Shelf (500mm)") ||
               meta.Name.Contains("SHP_Rails") ||
               meta.Name.Contains("NitrogenRegulator");
    }

    // Memory optimized version of RenderSingleConfigPage
    public static void RenderSingleConfigPage(
        Document doc,
        PdfWriter writer,
        List<PdfImageData> images,
        string title,
        string subtitle,
        List<AssemblyJson> assemblies,
        ProjectMetaData metadata)
    {
        // Force GC before starting
        GC.Collect();
        GC.WaitForPendingFinalizers();

        AddTitleOptimized(doc, title, subtitle);

        PdfPTable mainTable = new PdfPTable(2) { WidthPercentage = 100 };
        mainTable.SetWidths(new float[] { 40f, 60f });

        PdfPCell assembliesCell = CreateAssembliesCellOptimized(assemblies);
        PdfPCell imageCell = CreateImageCellOptimized(images, doc, writer);

        mainTable.AddCell(assembliesCell);
        mainTable.AddCell(imageCell);
        doc.Add(mainTable);

        // Clear references immediately
        mainTable = null;
        assembliesCell = null;
        imageCell = null;

        AddCompanyLogoOptimized(doc);
        AddCustomerAcceptanceSectionOptimized(doc, metadata);

        // Force GC after completion
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}