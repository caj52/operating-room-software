using System;
using System.Collections; // Added for coroutine support
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using iTextSharp.text;
using iTextSharp.text.html;
using iTextSharp.text.pdf;
using UnityEngine;
using Font = iTextSharp.text.Font;

public class PdfExporterLocal
{
    #region Configuration / Options
    /// <summary>
    /// Options to control PDF export behaviour (UI, logging, output path, callbacks).
    /// </summary>
    public class PdfExportOptions
    {
        /// <summary>Directory where PDF will be saved. If null a /pdf folder inside the current room path is used.</summary>
        public string OutputDirectory { get; set; }
        /// <summary>Custom file name (without extension). If null a timestamp based name is used.</summary>
        public string FileNameBase { get; set; }
        /// <summary>Open the PDF automatically after export (default true).</summary>
        public bool OpenAfterExport { get; set; } = true;
        /// <summary>Instead of opening the PDF, show the containing folder (Finder/Explorer) when OpenAfterExport is true.</summary>
        public bool ShowInFolderInsteadOfOpen { get; set; } = false;
        /// <summary>Show in‑app success dialog (default true).</summary>
        public bool ShowSuccessDialog { get; set; } = true;
        /// <summary>Show loading screen (default true).</summary>
        public bool ShowLoadingScreen { get; set; } = true;
        /// <summary>Enable memory logging (debug only).</summary>
        public bool LogMemory { get; set; } = false;
        /// <summary>Enable internal progress tracking (raw instantaneous updates).</summary>
        public bool ReportProgress { get; set; } = false;
        /// <summary>Enable coroutine smooth progress (use with ExportElevationPdfLocalSmooth).</summary>
        public bool SmoothProgress { get; set; } = false;
        /// <summary>Progress callback: (value 0..1, status message).</summary>
        public Action<float, string> OnProgress { get; set; }
        /// <summary>Invoke on success with full file path.</summary>
        public Action<string> OnSuccess { get; set; }
        /// <summary>Invoke on error with the exception.</summary>
        public Action<Exception> OnError { get; set; }
        /// <summary>Minimum seconds to keep loading screen visible (smooth UX).</summary>
        public float MinDisplaySeconds { get; set; } = 0.75f;
    }
    #endregion

    #region Caches / Memory Tracking
    private static readonly Dictionary<string, BaseFont> FontCache = new Dictionary<string, BaseFont>();
    private static readonly Dictionary<string, Font> CachedFonts = new Dictionary<string, Font>();
    private static readonly Dictionary<string, PdfTemplate> TemplateCache = new Dictionary<string, PdfTemplate>();
    private static long InitialMemory;
    private static long PeakMemory;
    #endregion

    #region Progress Internal
    private static float _instProgress;         // instantaneous logical progress (target)
    private static float _smoothedProgress;     // smoothed value (reported when SmoothProgress)
    private static string _progressStatus;
    private static float _progressLerpSpeed = 6f; // smoothing speed

    private static void ResetProgress()
    {
        _instProgress = 0f;
        _smoothedProgress = 0f;
        _progressStatus = string.Empty;
    }

    private static void UpdateProgress(float value, string status, PdfExportOptions options, bool forceInstant = false)
    {
        if (value < 0f) value = 0f; if (value > 1f) value = 1f;
        _instProgress = value;
        _progressStatus = status;
        if (options == null) return;
        if (options.ReportProgress && !options.SmoothProgress)
        {
            options.OnProgress?.Invoke(_instProgress, _progressStatus);
        }
        // For smooth progress we defer actual callback to smoothing step in coroutine
        if (forceInstant && options.SmoothProgress)
        {
            _smoothedProgress = _instProgress;
            options.OnProgress?.Invoke(_smoothedProgress, _progressStatus);
        }
    }

    private static void TickSmoothProgress(float deltaTime, PdfExportOptions options)
    {
        if (options == null || !options.SmoothProgress) return;
        float before = _smoothedProgress;
        _smoothedProgress = Mathf.MoveTowards(_smoothedProgress, _instProgress, deltaTime * _progressLerpSpeed * Mathf.Max(0.05f, Math.Abs(_instProgress - _smoothedProgress)));
        if (!Mathf.Approximately(before, _smoothedProgress))
            options.OnProgress?.Invoke(_smoothedProgress, _progressStatus);
    }
    #endregion

    #region Data Classes
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
    #endregion

    #region Constants
    private const string PrimaryHeaderColorHex = "#001236";
    private static readonly BaseColor PrimaryHeaderColor = WebColors.GetRGBColor(PrimaryHeaderColorHex);
    private static readonly BaseColor TableStripeGray = HexToBaseColor("#E5E7EB");
    #endregion

    #region Public API
    /// <summary>
    /// User friendly overload. Returns full output file path (or null on error).
    /// </summary>
    public static string ExportElevationPdfLocal(
        List<PdfImageData> images,
        string title,
        string subtitle,
        List<AssemblyJson> assemblies,
        ProjectMetaData metaData = null,
        PdfExportOptions options = null)
    {
        options ??= new PdfExportOptions();
        if (images == null) images = new List<PdfImageData>();
        if (assemblies == null) assemblies = new List<AssemblyJson>();
        metaData ??= new ProjectMetaData();

        try
        {
            string outputDir = !string.IsNullOrEmpty(options.OutputDirectory)
                ? options.OutputDirectory
                : Path.Combine(FullRoomSave.GetRoomPath(), "pdf");
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            string fileName = !string.IsNullOrEmpty(options.FileNameBase)
                ? SanitizeFileName(options.FileNameBase) + ".pdf"
                : $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string fullPath = Path.Combine(outputDir, fileName);

            InternalExport(images, title, subtitle, assemblies, metaData, fullPath, options);
            return fullPath;
        }
        catch (Exception ex)
        {
            options?.OnError?.Invoke(ex);
            UnityEngine.Debug.LogError($"PDF export failed: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Coroutine-based smooth export (non-blocking UI). Requires caller to StartCoroutine.
    /// </summary>
    public static IEnumerator ExportElevationPdfLocalSmooth(
        List<PdfImageData> images,
        string title,
        string subtitle,
        List<AssemblyJson> assemblies,
        ProjectMetaData metaData = null,
        PdfExportOptions options = null)
    {
        options ??= new PdfExportOptions { SmoothProgress = true, ReportProgress = false };
        options.SmoothProgress = true; // enforce
        if (images == null) images = new List<PdfImageData>();
        if (assemblies == null) assemblies = new List<AssemblyJson>();
        metaData ??= new ProjectMetaData();

        ResetProgress();
        float startTime = Time.realtimeSinceStartup;

        string outputDir = !string.IsNullOrEmpty(options.OutputDirectory)
            ? options.OutputDirectory
            : Path.Combine(FullRoomSave.GetRoomPath(), "pdf");
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        string fileName = !string.IsNullOrEmpty(options.FileNameBase)
            ? SanitizeFileName(options.FileNameBase) + ".pdf"
            : $"Export_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        string fullPath = Path.Combine(outputDir, fileName);

        InitialMemory = GC.GetTotalMemory(true);
        PeakMemory = InitialMemory;

        if (options.ShowLoadingScreen && UI_GeneralLoadingScreen.instance != null)
            UI_GeneralLoadingScreen.instance.ShowLoadingScreen();

        // Progress planning
        int dynamicSteps = Mathf.Max(1, assemblies.Count);
        const int fixedSteps = 6;
        int totalSteps = dynamicSteps + fixedSteps;
        int stepIndex = 0;
        void Step(string status)
        {
            stepIndex++;
            float baseProgress = Mathf.Clamp01((float)stepIndex / totalSteps);
            UpdateProgress(baseProgress, status, options);
        }

        FileStream fileStream = null; BufferedStream bufferedStream = null; Document document = null; PdfWriter writer = null;
        Exception caught = null;

        // Open resources (no yield until after constructed)
        try
        {
            fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096);
            bufferedStream = new BufferedStream(fileStream, 4096);
            document = new Document(new Rectangle(1400, 1200, 90), 19.08f, 19.08f, 10, 10);
            writer = PdfWriter.GetInstance(document, bufferedStream);
            writer.CloseStream = false;
            document.Open();
        }
        catch (Exception ex)
        {
            caught = ex;
        }
        Step("Opening document");
        yield return null;

        if (caught != null)
        {
            options.OnError?.Invoke(caught);
            if (options.ShowLoadingScreen && UI_GeneralLoadingScreen.instance != null)
                UI_GeneralLoadingScreen.instance.HideLoadingScreen();
            yield break;
        }

        // Title
        try { AddTitle(document, title, subtitle); } catch (Exception ex) { caught = ex; }
        Step("Title added");
        yield return null;
        if (caught != null) goto CLEANUP;

        // Assemblies + images
        if (assemblies.Count == 0)
        {
            try { AddMainContent(document, writer, assemblies, images); } catch (Exception ex) { caught = ex; }
            Step("Layout built");
            yield return null;
        }
        else
        {
            // Build progressively without extra try/finally
            PdfPCell container = new PdfPCell { Border = Rectangle.NO_BORDER, PaddingRight = 20f };
            Font headerFont = GetCachedFont("header", "Arial", 12, Font.BOLD, BaseColor.WHITE);
            Font serviceHeaderFont = GetCachedFont("serviceheader", "Arial", 12, Font.BOLD, BaseColor.WHITE);
            Font itemFont = GetCachedFont("item", "Arial", 10, Font.NORMAL, BaseColor.BLACK);
            Font valueFont = GetCachedFont("value", "Arial", 10, Font.NORMAL, BaseColor.BLACK);
            float rowH = 21.6f; BaseColor white = BaseColor.WHITE;

            for (int i = 0; i < assemblies.Count; i++)
            {
                try { ProcessSingleAssembly(container, assemblies[i], headerFont, serviceHeaderFont, itemFont, valueFont, rowH, TableStripeGray, white); }
                catch (Exception ex) { caught = ex; break; }
                float logicalBase = (float)(i + 1) / assemblies.Count;
                float spanStart = 2f / totalSteps; // after first 2 fixed steps (open + title)
                float spanEnd = (dynamicSteps + 2f) / totalSteps;
                float mapped = Mathf.Lerp(spanStart, spanEnd, logicalBase);
                UpdateProgress(mapped, $"Assembly {i + 1}/{assemblies.Count}", options);
                TickSmoothProgress(Time.deltaTime, options);
                yield return null;
            }
            if (caught != null) goto CLEANUP;

            // Footer (ceiling height)
            try
            {
                float metersCH = GetCeilingHeight();
                float ftWhole = Mathf.Floor(metersCH.ToFeet());
                float inches = Mathf.Round((metersCH.ToFeet() - ftWhole) * 12f * 10f) / 10f;
                string ceilingText = $"{metersCH:F2} m ({ftWhole}' {inches}\")";
                var chTable = new PdfPTable(2) { WidthPercentage = 60f, SpacingBefore = 0f, SpacingAfter = 8f, HorizontalAlignment = Element.ALIGN_LEFT };
                chTable.SetWidths(new float[] { 60, 40 });
                chTable.AddCell(new PdfPCell(new Phrase("Ceiling Height", itemFont)) { BackgroundColor = white, FixedHeight = rowH, Border = Rectangle.BOX, Padding = 4 });
                chTable.AddCell(new PdfPCell(new Phrase(ceilingText, valueFont)) { BackgroundColor = white, FixedHeight = rowH, Border = Rectangle.BOX, Padding = 4 });
                container.AddElement(chTable);
            }
            catch { }

            PdfPCell imageCell = CreateImageCell(images, document, writer, GetCeilingHeight());
            imageCell.PaddingRight = 50;
            PdfPCell assembliesCell = container; assembliesCell.PaddingRight = 0;
            PdfPTable pageTable = new PdfPTable(2) { HorizontalAlignment = Element.ALIGN_LEFT, WidthPercentage = 100 };
            pageTable.SetWidths(new float[] { 40, 60 });
            pageTable.AddCell(assembliesCell);
            pageTable.AddCell(imageCell);
            try { document.Add(pageTable); } catch (Exception ex) { caught = ex; }
            Step("Layout built");
            yield return null;
            if (caught != null) goto CLEANUP;
        }

        // Logo
        try { AddCompanyLogo(document); } catch (Exception ex) { caught = ex; }
        Step("Logo added");
        yield return null;
        if (caught != null) goto CLEANUP;

        // Acceptance
        try { AddCustomerAcceptanceSection(document, metaData); } catch (Exception ex) { caught = ex; }
        Step("Acceptance section");
        yield return null;
        if (caught != null) goto CLEANUP;

        // Close
        try { document.Close(); writer?.Close(); bufferedStream?.Flush(); } catch (Exception ex) { caught = ex; }
        Step("Finalizing");
        yield return null;

    CLEANUP:
        // Dispose resources (no try/finally to keep yields legal)
        if (writer != null) { /* writer already closed above */ }
        if (document != null && document.IsOpen()) { document.Close(); }
        bufferedStream?.Close();
        fileStream?.Close();

        if (caught != null)
        {
            options.OnError?.Invoke(caught);
            UnityEngine.Debug.LogError($"PDF export (smooth) failed: {caught.Message}\n{caught.StackTrace}");
            if (options.ShowLoadingScreen && UI_GeneralLoadingScreen.instance != null)
                UI_GeneralLoadingScreen.instance.HideLoadingScreen();
            yield break;
        }

        CleanupResources();

        // Smooth finish
        float elapsed = Time.realtimeSinceStartup - startTime;
        if (elapsed < options.MinDisplaySeconds)
        {
            while (elapsed < options.MinDisplaySeconds)
            {
                elapsed += Time.deltaTime;
                TickSmoothProgress(Time.deltaTime, options);
                yield return null;
            }
        }
        UpdateProgress(1f, "Done", options);
        for (int i = 0; i < 2; i++) { TickSmoothProgress(Time.deltaTime, options); yield return null; }

        if (options.ShowLoadingScreen && UI_GeneralLoadingScreen.instance != null)
            UI_GeneralLoadingScreen.instance.HideLoadingScreen();

        if (options.ShowSuccessDialog)
        {
            UI_DialogPrompt.Open(
                $"Success! PDF saved to {fullPath}",
                new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = fullPath),
                new ButtonAction("Done"));
        }

        options.OnSuccess?.Invoke(fullPath);

        if (options.OpenAfterExport)
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            if (options.ShowInFolderInsteadOfOpen)
            {
                try { Process.Start("open", $"-R \"{fullPath}\""); } catch (Exception ex) { UnityEngine.Debug.LogWarning($"Reveal failed: {ex.Message}"); }
            }
            else { Application.OpenURL("file:///" + fullPath); }
#else
            if (options.ShowInFolderInsteadOfOpen)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "/select,\"" + fullPath + "\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex) { UnityEngine.Debug.LogWarning($"Show in Explorer failed: {ex.Message}"); }
            }
            else { Application.OpenURL("file:///" + fullPath); }
#endif
        }
    }
    #endregion

    #region Core Export (Blocking)
    private static void InternalExport(
        List<PdfImageData> imageData,
        string title,
        string subtitle,
        List<AssemblyJson> assemblies,
        ProjectMetaData metaData,
        string filePath,
        PdfExportOptions options)
    {
        InitialMemory = GC.GetTotalMemory(true);
        PeakMemory = InitialMemory;

        ResetProgress();
        UpdateProgress(0f, "Starting", options, forceInstant: true);

        if (options.ShowLoadingScreen && UI_GeneralLoadingScreen.instance != null)
            UI_GeneralLoadingScreen.instance.ShowLoadingScreen();

        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096))
        using (var bufferedStream = new BufferedStream(fileStream, 4096))
        using (var document = new Document(new Rectangle(1400, 1200, 90), 19.08f, 19.08f, 10, 10))
        {
            PdfWriter writer = null;
            try
            {
                writer = PdfWriter.GetInstance(document, bufferedStream);
                writer.CloseStream = false;
                document.Open();
                UpdateProgress(0.05f, "Document opened", options);
                MonitorMemory("Document opened", options);

                AddTitle(doc: document, title: title, subtitle: subtitle);
                UpdateProgress(0.15f, "Title", options);
                MonitorMemory("Title added", options);

                AddMainContent(document, writer, assemblies, imageData);
                UpdateProgress(0.55f, "Content", options);
                MonitorMemory("Main content added", options);

                AddCompanyLogo(document);
                UpdateProgress(0.7f, "Logo", options);

                AddCustomerAcceptanceSection(document, metaData);
                UpdateProgress(0.85f, "Acceptance", options);

                document.Close();
                UpdateProgress(0.95f, "Closing", options);
                MonitorMemory("Document closed", options);
            }
            finally
            {
                if (writer != null)
                {
                    CleanupPdfWriter(writer);
                    writer.Close();
                }
            }
        }

        CleanupResources();
        UpdateProgress(1f, "Done", options, forceInstant: true);

        if (options.ShowLoadingScreen && UI_GeneralLoadingScreen.instance != null)
            UI_GeneralLoadingScreen.instance.HideLoadingScreen();

        if (options.ShowSuccessDialog)
        {
            UI_DialogPrompt.Open(
                $"Success! PDF saved to {filePath}",
                new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = filePath),
                new ButtonAction("Done"));
        }

        options.OnSuccess?.Invoke(filePath);

        if (options.OpenAfterExport)
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            if (options.ShowInFolderInsteadOfOpen)
            {
                try { Process.Start("open", $"-R \"{filePath}\""); } catch (Exception ex) { UnityEngine.Debug.LogWarning($"Failed to reveal in Finder: {ex.Message}"); }
            }
            else { Application.OpenURL("file:///" + filePath); }
#else
            if (options.ShowInFolderInsteadOfOpen)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "/select,\"" + filePath + "\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex) { UnityEngine.Debug.LogWarning($"Failed to show in Explorer: {ex.Message}"); }
            }
            else { Application.OpenURL("file:///" + filePath); }
#endif
        }
    }
    #endregion

    #region Layout Builders
    private static void AddMainContent(Document doc, PdfWriter writer, List<AssemblyJson> assemblies, List<PdfImageData> imageData)
    {
        var mainTable = new PdfPTable(2)
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
    }

    private static void AddTitle(Document doc, string title, string subtitle)
    {
        var fontPath = Path.Combine(Application.streamingAssetsPath, "Data/Fonts/Teko/Teko-Light.ttf");
        BaseFont tekoLight = GetCachedBaseFont(fontPath);
        Font titleFont = new Font(tekoLight, 36, Font.NORMAL, BaseColor.WHITE);
        Font subtitleFont = GetCachedFont("subtitle", "Arial", 15, Font.NORMAL, BaseColor.WHITE);

        Paragraph pTitle = new Paragraph(string.IsNullOrEmpty(title) ? "Untitled" : title, titleFont)
        {
            SpacingAfter = 6f
        };
        pTitle.SetLeading(0f, 1.0f);
        Paragraph pSubtitle = new Paragraph(subtitle ?? string.Empty, subtitleFont);

        PdfPCell titleCell = new PdfPCell
        {
            BackgroundColor = PrimaryHeaderColor,
            Border = Rectangle.NO_BORDER,
            Padding = 10f
        };
        titleCell.AddElement(pTitle);
        if (!string.IsNullOrEmpty(subtitle))
            titleCell.AddElement(pSubtitle);

        PdfPTable wrapper = new PdfPTable(1) { WidthPercentage = 100f };
        wrapper.AddCell(titleCell);
        wrapper.SpacingAfter = 20f;
        doc.Add(wrapper);
    }

    private static PdfPCell CreateAssembliesCell(List<AssemblyJson> assemblies)
    {
        PdfPCell container = new PdfPCell { Border = Rectangle.NO_BORDER, PaddingRight = 20f };

        Font headerFont = GetCachedFont("header", "Arial", 12, Font.BOLD, BaseColor.WHITE);
        Font serviceHeaderFont = GetCachedFont("serviceheader", "Arial", 12, Font.BOLD, BaseColor.WHITE);
        Font itemFont = GetCachedFont("item", "Arial", 10, Font.NORMAL, BaseColor.BLACK);
        Font valueFont = GetCachedFont("value", "Arial", 10, Font.NORMAL, BaseColor.BLACK);

        float rowH = 21.6f;
        BaseColor white = BaseColor.WHITE;

        if (assemblies == null || assemblies.Count == 0)
        {
            container.AddElement(new Paragraph("No assemblies available", itemFont));
        }
        else
        {
            const int batchSize = 5;
            for (int batchStart = 0; batchStart < assemblies.Count; batchStart += batchSize)
            {
                int batchEnd = Math.Min(batchStart + batchSize, assemblies.Count);
                for (int i = batchStart; i < batchEnd; i++)
                {
                    var asm = assemblies[i];
                    ProcessSingleAssembly(container, asm, headerFont, serviceHeaderFont, itemFont, valueFont, rowH, TableStripeGray, white);
                }
            }
        }

        // Footer: Ceiling Height (fixed spelling)
        try
        {
            float metersCH = GetCeilingHeight();
            float ftWhole = Mathf.Floor(metersCH.ToFeet());
            float inches = Mathf.Round((metersCH.ToFeet() - ftWhole) * 12f * 10f) / 10f;
            string ceilingText = $"{metersCH:F2} m ({ftWhole}' {inches}\")";

            var chTable = new PdfPTable(2)
            {
                WidthPercentage = 60f,
                SpacingBefore = 0f,
                SpacingAfter = 8f,
                HorizontalAlignment = Element.ALIGN_LEFT,
            };
            chTable.SetWidths(new float[] { 60, 40 });

            var labelCell = new PdfPCell(new Phrase("Ceiling Height", itemFont))
            {
                BackgroundColor = white,
                FixedHeight = rowH,
                Border = Rectangle.BOX,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 4
            };
            var valueCell = new PdfPCell(new Phrase(ceilingText, valueFont))
            {
                BackgroundColor = white,
                FixedHeight = rowH,
                Border = Rectangle.BOX,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 4
            };
            chTable.AddCell(labelCell);
            chTable.AddCell(valueCell);
            container.AddElement(chTable);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"Failed adding Ceiling Height footer: {ex.Message}");
        }
        return container;
    }

    private static void ProcessSingleAssembly(PdfPCell container, AssemblyJson asm, Font headerFont, Font serviceHeaderFont, Font itemFont, Font valueFont, float rowH, BaseColor gray, BaseColor white)
    {
        if (asm == null) return;
        // Skip empty placeholder assemblies (e.g., tandem mount slot left empty)
        if ((asm.Fields == null || asm.Fields.Count == 0) && string.IsNullOrWhiteSpace(asm.TableName)) return;
        var hdrPara = new Paragraph(string.IsNullOrEmpty(asm.TableName) ? "Assembly" : asm.TableName, headerFont) { Alignment = Element.ALIGN_LEFT };
        PdfPCell hdrCell = new PdfPCell(hdrPara)
        {
            BackgroundColor = PrimaryHeaderColor,
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
            fldTbl.AddCell(new PdfPCell(new Phrase(f.Item ?? string.Empty, itemFont))
            {
                BackgroundColor = bg,
                FixedHeight = rowH,
                Border = Rectangle.BOX,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 4
            });
            fldTbl.AddCell(new PdfPCell(new Phrase(f.Value ?? string.Empty, valueFont))
            {
                BackgroundColor = bg,
                FixedHeight = rowH,
                Border = Rectangle.BOX,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 4
            });
            stripe = !stripe;
        }

        AddConditionalRows(fldTbl, asm.TableName, rowH, gray, white, itemFont, ref stripe);
        container.AddElement(fldTbl);

        if (serviceAttachments.Count > 0)
        {
            ProcessServiceAttachments(container, serviceAttachments, serviceHeaderFont, itemFont, valueFont, rowH, gray, white);
        }

        container.AddElement(new Paragraph(" "));
    }

    private static void AddConditionalRows(PdfPTable fldTbl, string tableName, float rowH, BaseColor gray, BaseColor white, Font itemFont, ref bool stripe)
    {
        if (string.IsNullOrEmpty(tableName)) return;
        if (tableName == "Flat Panel Arm" || tableName == "Lights - U | ONE" || tableName == "Spring Arm (Low Ceiling)")
        {
            AddAdditionalRow(fldTbl, "Circuits Required", string.Empty, rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Overall Weight", string.Empty, rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Torque Moment", string.Empty, rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Vertical Force Nm", string.Empty, rowH, stripe ? gray : white, itemFont);
        }
        if (tableName.Contains("Boom - Service Head"))
        {
            AddAdditionalRow(fldTbl, "Med-Gas Connection Type", string.Empty, rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Overall Weight", string.Empty, rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Vertical Force", string.Empty, rowH, stripe ? gray : white, itemFont); stripe = !stripe;
            AddAdditionalRow(fldTbl, "Payload Capacity", string.Empty, rowH, stripe ? gray : white, itemFont);
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
            BackgroundColor = PrimaryHeaderColor,
            Border = Rectangle.NO_BORDER,
            Padding = 4,
            HorizontalAlignment = Element.ALIGN_LEFT,
        };
        attachTbl.AddCell(subHdr);

        bool useGray = true;
        foreach (var f in serviceAttachments)
        {
            var bg = useGray ? gray : white;
            attachTbl.AddCell(new PdfPCell(new Phrase(f.Item ?? string.Empty, itemFont))
            {
                BackgroundColor = bg,
                FixedHeight = rowH,
                Border = Rectangle.BOX,
                HorizontalAlignment = Element.ALIGN_LEFT,
                Padding = 4
            });
            attachTbl.AddCell(new PdfPCell(new Phrase(f.Value ?? string.Empty, valueFont))
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
        PdfPCell itemCell = new PdfPCell(new Phrase(itemText, itemFont))
        {
            FixedHeight = rowHeight,
            BackgroundColor = backgroundColor,
            HorizontalAlignment = Element.ALIGN_LEFT,
            Border = Rectangle.BOX,
            Padding = 4
        };
        PdfPCell valueCell = new PdfPCell(new Phrase(valueText, itemFont))
        {
            FixedHeight = rowHeight,
            BackgroundColor = backgroundColor,
            HorizontalAlignment = Element.ALIGN_LEFT,
            Border = Rectangle.BOX,
            Padding = 4
        };
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
        float maxTargetHeight = 300f;

        PdfPCell imageCell = new PdfPCell
        {
            Border = Rectangle.NO_BORDER,
            VerticalAlignment = Element.ALIGN_BOTTOM,
            HorizontalAlignment = Element.ALIGN_CENTER,
            PaddingLeft = 20f
        };

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

            if (imageData.Count > i && !string.IsNullOrEmpty(imageData[i].Path) && File.Exists(imageData[i].Path))
            {
                try
                {
                    Image img = Image.GetInstance(imageData[i].Path);
                    img.Alignment = Element.ALIGN_BOTTOM;
                    float scale = Math.Min(availableImageWidth / img.Width, maxTargetHeight / img.Height);
                    img.ScaleAbsolute(img.Width * scale, img.Height * scale);
                    cell.AddElement(img);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Failed to load image {imageData[i].Path}: {ex.Message}");
                }
            }
            imgs.AddCell(cell);
        }

        var outer = new PdfPTable(1) { WidthPercentage = 100f };
        outer.DefaultCell.Border = Rectangle.NO_BORDER;
        outer.DefaultCell.Padding = 0f;
        outer.AddCell(new PdfPCell(imgs) { Border = Rectangle.NO_BORDER, Padding = 0f });

        var cb = writer.DirectContent;
        Image beamImg = BuildBeamImage(cb, usablePageWidth, 10f);
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

    private static Image BuildHeightImage(PdfContentByte cb, float visualHeight, float maxTargetHeight)
    {
        string cacheKey = $"height_{visualHeight}_{maxTargetHeight}";
        if (TemplateCache.ContainsKey(cacheKey))
            return Image.GetInstance(TemplateCache[cacheKey]);

        PdfTemplate tpl = cb.CreateTemplate(50, visualHeight);
        tpl.SetLineWidth(1.5f);
        tpl.MoveTo(10, 0); tpl.LineTo(10, visualHeight); tpl.Stroke();
        tpl.MoveTo(5, 0); tpl.LineTo(15, 0); tpl.Stroke();
        tpl.MoveTo(5, visualHeight); tpl.LineTo(15, visualHeight); tpl.Stroke();

        float meters = GetCeilingHeight();
        float ft = Mathf.Floor(meters.ToFeet());
        float inch = Mathf.Round((meters.ToFeet() - ft) * 12f * 10f) / 10f;
        Distance = $"{ft}' {inch}\"";

        var fontPath = Path.Combine(Application.streamingAssetsPath, "Data/Fonts/Teko/Teko-Regular.ttf");
        BaseFont teko = GetCachedBaseFont(fontPath);
        tpl.BeginText();
        tpl.SetFontAndSize(teko, 36);
        tpl.ShowTextAligned(Element.ALIGN_LEFT, Distance, 40, maxTargetHeight / 2, 90);
        tpl.EndText();
        TemplateCache[cacheKey] = tpl;
        return Image.GetInstance(tpl);
    }

    private static PdfPTable BuildContentTable(Image heightImg, List<PdfImageData> imageData, float availableImageWidth, float maxTargetHeight)
    {
        PdfPTable table = new PdfPTable(2) { WidthPercentage = 100f };
        table.SetWidths(new float[] { 10f, 90f });
        PdfPCell hCell = new PdfPCell(heightImg) { Border = Rectangle.NO_BORDER, Padding = 0f, VerticalAlignment = Element.ALIGN_MIDDLE };
        table.AddCell(hCell);

        PdfPTable imgs = new PdfPTable(2) { WidthPercentage = 100f };
        for (int i = 0; i < 2; i++)
        {
            PdfPCell cell = new PdfPCell { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_CENTER, VerticalAlignment = Element.ALIGN_BOTTOM, Padding = 0f };
            if (imageData.Count > i && File.Exists(imageData[i].Path))
            {
                try
                {
                    Image img = Image.GetInstance(imageData[i].Path);
                    float scale = Math.Min(availableImageWidth / img.Width, maxTargetHeight / img.Height);
                    img.ScaleAbsolute(img.Width * scale, img.Height * scale);
                    cell.AddElement(img);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Failed to load image {imageData[i].Path}: {ex.Message}");
                }
            }
            imgs.AddCell(cell);
        }
        table.AddCell(new PdfPCell(imgs) { Border = Rectangle.NO_BORDER, Padding = 0f });
        return table;
    }

    private static Image BuildBeamImage(PdfContentByte cb, float usablePageWidth, float leftMargin)
    {
        string cacheKey = $"beam_{usablePageWidth}_{leftMargin}";
        if (TemplateCache.ContainsKey(cacheKey))
            return Image.GetInstance(TemplateCache[cacheKey]);

        float totalWidth = leftMargin + usablePageWidth;
        PdfTemplate beam = cb.CreateTemplate(totalWidth, 100f);
        float beamY = 100f;
        float supportH = 20f;
        float spacing = usablePageWidth / 14f;
        beam.SetLineWidth(3f);
        beam.MoveTo(leftMargin, beamY);
        beam.LineTo(leftMargin + usablePageWidth, beamY);
        beam.Stroke();
        for (int i = 0; i <= 14; i++)
        {
            float x = leftMargin + (i * spacing);
            beam.MoveTo(x, beamY);
            beam.LineTo(x - supportH, beamY - supportH);
            beam.Stroke();
        }
        TemplateCache[cacheKey] = beam;
        Image img = Image.GetInstance(beam);
        img.ScaleToFit(totalWidth, 100f);
        return img;
    }
    #endregion

    #region Helper / Utilities
    private static float GetCeilingHeight()
    {
        var ceiling = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling);
        return ceiling != null ? ceiling.Height : 0f;
    }

    private static void AddCompanyLogo(Document doc)
    {
        try
        {
            string logoPath = Application.streamingAssetsPath + "/Data/quotes/UImagineUnlimited-logo.png";
            if (!File.Exists(logoPath)) { UnityEngine.Debug.LogWarning($"Logo file not found: {logoPath}"); return; }
            PdfPTable logoTable = new PdfPTable(1) { TotalWidth = 300, HorizontalAlignment = Element.ALIGN_RIGHT, LockedWidth = true };
            PdfPCell logoCell = new PdfPCell { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_LEFT, PaddingBottom = 10f };
            Image logo = Image.GetInstance(logoPath);
            logo.ScaleToFit(300, 300);
            logoCell.AddElement(logo);
            logoTable.AddCell(logoCell);
            doc.Add(logoTable);
        }
        catch (Exception ex) { UnityEngine.Debug.LogError($"Failed to add company logo: {ex.Message}"); }
    }

    private static void AddCustomerAcceptanceSection(Document doc, ProjectMetaData metaData)
    {
        metaData ??= new ProjectMetaData();
        Font normalFont = GetCachedFont("normal", FontFactory.HELVETICA, 10, Font.NORMAL, BaseColor.BLACK);
        Font notesFont = GetCachedFont("notes", FontFactory.HELVETICA_OBLIQUE, 10, Font.NORMAL, BaseColor.BLACK);

        PdfPTable acceptanceTable = new PdfPTable(2)
        {
            TotalWidth = 800,
            HorizontalAlignment = Element.ALIGN_RIGHT,
            LockedWidth = true
        };
        acceptanceTable.SetWidths(new float[] { 60, 40 });

        PdfPCell leftCell = new PdfPCell { Border = Rectangle.BOX, Padding = 5f };
        leftCell.AddElement(new Paragraph("Customer Acceptance and Configuration Acknowledgement", notesFont));
        leftCell.AddElement(Chunk.NEWLINE);
        leftCell.AddElement(Chunk.NEWLINE);
        leftCell.AddElement(Chunk.NEWLINE);

        PdfPTable signatureTable = new PdfPTable(2) { WidthPercentage = 90 };
        signatureTable.SetWidths(new float[] { 2f, 1f });
        PdfPCell signatureCell = new PdfPCell(new Phrase("Signature", normalFont)) { FixedHeight = 30f, VerticalAlignment = Element.ALIGN_BOTTOM, Border = Rectangle.TOP_BORDER };
        PdfPCell dateCell = new PdfPCell(new Phrase("Date", normalFont)) { FixedHeight = 30f, VerticalAlignment = Element.ALIGN_BOTTOM, Border = Rectangle.TOP_BORDER };
        signatureTable.AddCell(signatureCell);
        signatureTable.AddCell(dateCell);
        leftCell.AddElement(signatureTable);
        acceptanceTable.AddCell(leftCell);

        PdfPTable detailTable = new PdfPTable(1) { WidthPercentage = 100 };
        string[] details =
        {
            "Account Name: " + (metaData.AccountName ?? string.Empty),
            "Account Address: " + (metaData.AccountAddressLine1 ?? string.Empty),
            " " + (metaData.AccountAddressLine2 ?? string.Empty),
            "Project Name: " + (metaData.ProjectName ?? string.Empty),
            "Project #: " + (metaData.ProjectNumber ?? string.Empty),
            "Order Reference #: " + (metaData.OrderReferenceNumber ?? string.Empty)
        };
        foreach (var d in details)
        {
            PdfPCell labelCell = new PdfPCell(new Phrase(d, normalFont)) { Padding = 4f };
            detailTable.AddCell(labelCell);
        }
        PdfPCell rightCell = new PdfPCell(detailTable) { Border = Rectangle.BOX, Padding = 0f };
        acceptanceTable.AddCell(rightCell);
        doc.Add(acceptanceTable);
    }

    private static void CleanupPdfWriter(PdfWriter writer)
    {
        try
        {
            foreach (var template in TemplateCache.Values)
            {
                try { writer.ReleaseTemplate(template); }
                catch (Exception ex) { UnityEngine.Debug.LogWarning($"Failed to release template: {ex.Message}"); }
            }
        }
        catch (Exception ex) { UnityEngine.Debug.LogError($"Error during PdfWriter cleanup: {ex.Message}"); }
    }

    public static void CleanupResources()
    {
        try
        {
            TemplateCache.Clear();
            FontCache.Clear();
            CachedFonts.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            UnityEngine.Debug.Log($"Memory cleaned. Final memory: {GC.GetTotalMemory(false) / 1024 / 1024}MB");
        }
        catch (Exception ex) { UnityEngine.Debug.LogError($"Error during resource cleanup: {ex.Message}"); }
    }

    private static void MonitorMemory(string operation, PdfExportOptions options)
    {
        if (options == null || !options.LogMemory) return;
        long current = GC.GetTotalMemory(false);
        if (current > PeakMemory) PeakMemory = current;
        UnityEngine.Debug.Log($"[PDF] Memory after {operation}: {current / 1024 / 1024}MB (Peak {PeakMemory / 1024 / 1024}MB)");
    }

    private static BaseFont GetCachedBaseFont(string fontPath)
    {
        if (string.IsNullOrEmpty(fontPath)) throw new ArgumentException("Font path null/empty");
        if (!FontCache.TryGetValue(fontPath, out var baseFont))
        {
            try
            {
                if (!File.Exists(fontPath)) throw new FileNotFoundException("Font file not found: " + fontPath);
                baseFont = BaseFont.CreateFont(fontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Font loading failed for {fontPath}: {ex.Message}. Using fallback.");
                try
                {
                    string fallbackPath = Path.Combine(Application.streamingAssetsPath, "Fonts", "Arial.ttf");
                    baseFont = File.Exists(fallbackPath)
                        ? BaseFont.CreateFont(fallbackPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED)
                        : BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED);
                }
                catch (Exception fallbackEx)
                {
                    UnityEngine.Debug.LogError($"All font loading attempts failed: {fallbackEx.Message}");
                    throw;
                }
            }
            FontCache[fontPath] = baseFont;
        }
        return baseFont;
    }

    private static Font GetCachedFont(string key, string fontName, float size, int style, BaseColor color)
    {
        if (!CachedFonts.TryGetValue(key, out var font))
        {
            font = FontFactory.GetFont(fontName, size, style, color);
            CachedFonts[key] = font;
        }
        return font;
    }

    public static BaseColor HexToBaseColor(string hex, int alpha = 255)
    {
        if (string.IsNullOrEmpty(hex)) return BaseColor.WHITE;
        hex = hex.Replace("#", "");
        if (hex.Length < 6) return BaseColor.WHITE;
        int r = int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
        int g = int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
        int b = int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
        return new BaseColor(r, g, b, alpha);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid) fileName = fileName.Replace(c, '_');
        return fileName.Trim();
    }

    private static string NormalizeItemName(SelectableMetaData meta)
    {
        if (meta == null) return string.Empty;
        return string.IsNullOrWhiteSpace(meta.SubPartName) ? (meta.Name ?? string.Empty).Trim() : $"{meta.Name?.Trim()} {meta.SubPartName?.Trim()}";
    }

    private static bool IsServiceHeadAttachment(SelectableMetaData meta)
    {
        if (meta == null || meta.Categories == null) return false;
        return meta.Categories.Contains("Boom - SH High Voltage") ||
               meta.Categories.Contains("Boom - SH Low Voltage") ||
               meta.Categories.Contains("Boom - SH Accessories") ||
               meta.Categories.Contains("Service Head Shelf (500mm)") ||
               (meta.Name != null && (meta.Name.Contains("Service Head Shelf (500mm)") || meta.Name.Contains("SHP_Rails") || meta.Name.Contains("NitrogenRegulator")));
    }
    #endregion

    #region Data Conversion
    public static List<AssemblyJson> ConvertToAssemblyJsonFull(List<AssemblyData> assemblyDatas, List<AdditionalPdfData> additionalData)
    {
        List<AssemblyJson> allTables = new List<AssemblyJson>();
        if (assemblyDatas == null) return allTables;
        int assId = 1;
        const int batchSize = 10;
        for (int batchStart = 0; batchStart < assemblyDatas.Count; batchStart += batchSize)
        {
            int batchEnd = Math.Min(batchStart + batchSize, assemblyDatas.Count);
            for (int i = batchStart; i < batchEnd; i++)
            {
                var assemblyData = assemblyDatas[i];
                if (assemblyData == null) continue;
                var assembly = ProcessSingleAssemblyData(assemblyData, assId++);
                allTables.Add(assembly);
            }
        }
        ProcessAdditionalData(allTables, additionalData ?? new List<AdditionalPdfData>());
        return allTables;
    }

    private static AssemblyJson ProcessSingleAssemblyData(AssemblyData assemblyData, int assId)
    {
        var assembly = new AssemblyJson { AssemblyId = assId, TableName = assemblyData.Title };
        if (assemblyData.OrderedSelectables == null) return assembly;

        Dictionary<string, int> serviceHeadItemCounts = new Dictionary<string, int>();
        List<string> usedServiceHeadItems = new List<string>();

        foreach (var item in assemblyData.OrderedSelectables)
        {
            var meta = item.GetMetadata();
            if (meta == null) continue;
            string itemName = NormalizeItemName(meta);
            if (IsServiceHeadAttachment(meta))
            {
                if (!serviceHeadItemCounts.TryGetValue(itemName, out var count)) count = 0;
                serviceHeadItemCounts[itemName] = count + 1;
            }
        }

        foreach (var item in assemblyData.OrderedSelectables)
        {
            var meta = item.GetMetadata();
            if (meta == null || meta.Name == null) continue;
            if (meta.Name.Contains("Blank Plate")) continue;
            string itemName = NormalizeItemName(meta);
            bool isServiceHead = IsServiceHeadAttachment(meta);

            if (isServiceHead && !usedServiceHeadItems.Contains(itemName))
            {
                string label = serviceHeadItemCounts[itemName] > 1 ? $"{itemName} ({serviceHeadItemCounts[itemName]})" : itemName;
                assembly.Fields.Add(new PdfField { Item = "Service Head Attachment", Value = label });
                usedServiceHeadItems.Add(itemName);
                continue;
            }

            if (item.RelatedSelectables != null && item.RelatedSelectables.Count > 0 && item.RelatedSelectables[0] == item)
            {
                if (meta.PdfData != null)
                {
                    foreach (var pdf in meta.PdfData)
                    {
                        string value = (pdf.Value ?? string.Empty).Trim();
                        if (value.Equals("{NAME}", StringComparison.OrdinalIgnoreCase)) value = itemName;
                        if (pdf.Table != null && pdf.Table.Trim().Equals("{ASSEMBLY}", StringComparison.OrdinalIgnoreCase))
                        {
                            assembly.Fields.Add(new PdfField { Item = pdf.Key, Value = value });
                        }
                    }
                }
            }

            bool lengthAlreadyAdded = assembly.Fields.Any(f => f.Item == itemName + " length");
            if (!lengthAlreadyAdded)
            {
                float size = item.CurrentScaleLevel?.Size ?? 0f;
                if (size > 0f)
                {
                    assembly.Fields.Add(new PdfField { Item = itemName + " length", Value = (size * 1000f).ToString("F0") + "mm" });
                }
            }
        }
        return assembly;
    }

    private static void ProcessAdditionalData(List<AssemblyJson> allTables, List<AdditionalPdfData> additionalData)
    {
        foreach (var addTable in additionalData)
        {
            if (addTable == null) continue;
            AssemblyJson existing;
            var match = Regex.Match(addTable.Table ?? string.Empty, "\\{(\\d+)\\}");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
                existing = allTables.FirstOrDefault(x => x.AssemblyId == id);
            else
                existing = allTables.FirstOrDefault(x => x.TableName == addTable.Table);

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
    #endregion

    #region Single Page Renderer (Existing API)
    public static void RenderSingleConfigPage(
        Document doc,
        PdfWriter writer,
        List<PdfImageData> images,
        string title,
        string subtitle,
        List<AssemblyJson> assemblies,
        ProjectMetaData metadata)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

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
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
    #endregion
}