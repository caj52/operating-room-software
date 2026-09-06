using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Debug = UnityEngine.Debug;

public class PdfBatchExporter : MonoBehaviour
{
    public void ExportAllConfigs()
    {
        UI_GeneralLoadingScreen.instance.ShowLoadingScreen();
        StartCoroutine(ExportAllConfigsToMultipagePdf());
    }

    public static IEnumerator ExportAllConfigsToMultipagePdf()
    {
        yield return ExportAllConfigsToMultipagePdf(null, null, null, suppressDialog: false);
    }

    /// <summary>Boom assembly roots currently in the room (matches export guards).</summary>
    public static List<Selectable> CollectBoomAssemblyRoots()
    {
        var roots = new HashSet<GameObject>();
        var result = new List<Selectable>();

        if (Selectable.ActiveSelectables == null)
            return result;

        foreach (var selectable in Selectable.ActiveSelectables)
        {
            if (selectable == null)
                continue;
            if (!selectable.TryGetArmAssemblyRoot(out GameObject root) || root == null)
                continue;
            if (!roots.Add(root))
                continue;

            var rootSelectable = root.GetComponent<Selectable>();
            if (rootSelectable != null)
                result.Add(rootSelectable);
        }

        // Deterministic page order — HashSet / ActiveSelectables order is not stable
        // across renames/reloads, which made multipage PDFs look "random".
        result.Sort((a, b) =>
        {
            string na = a != null ? a.name : string.Empty;
            string nb = b != null ? b.name : string.Empty;
            int byName = string.CompareOrdinal(na, nb);
            if (byName != 0)
                return byName;
            int ida = a != null ? a.GetInstanceID() : 0;
            int idb = b != null ? b.GetInstanceID() : 0;
            return ida.CompareTo(idb);
        });

        return result;
    }

    public static IEnumerator ExportAllConfigsToMultipagePdf(
        string outputDirectory,
        string defaultTitle,
        string defaultSubtitle,
        bool suppressDialog = false,
        Func<bool> shouldCancel = null)
    {
        var allRoots = CollectBoomAssemblyRoots();

        string folder = outputDirectory ?? ExportPaths.ElevationsDir;
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        string fileName = $"RoomElevations_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        string filePath = Path.Combine(folder, fileName);

        string title = defaultTitle ?? ExportPaths.GetRoomExportName();
        string subtitle = defaultSubtitle ?? string.Empty;
        int pagesWritten = 0;
        LastMultipageExportOk = false;
        LastExportedPath = null;

        using (FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (Document doc = new Document(new Rectangle(1400, 1200, 90), 19.08f, 19.08f, 10, 10))
        {
            PdfWriter writer = PdfWriter.GetInstance(doc, stream);
            doc.Open();

            for (int i = 0; i < allRoots.Count; i++)
            {
                if (shouldCancel != null && shouldCancel())
                    yield break;

                var root = allRoots[i];
                string configTitle = $"{title} — Configuration {i + 1}";
                string configSubtitle = ExportPaths.GetSelectableExportName(root, subtitle);

                var assemblyDatas = UI_PdfExportOptions.GenerateAssemblyDataWithTitles(root);

                List<PdfExporterLocal.PdfImageData> images = null;
                bool waiting = true;

                yield return root.CapturePdfDataForExport(configTitle, configSubtitle, assemblyDatas, (img, sel) =>
                {
                    images = img;
                    waiting = false;
                });

                yield return new WaitUntil(() => !waiting || (shouldCancel != null && shouldCancel()));
                if (shouldCancel != null && shouldCancel())
                    yield break;

                var additional = UI_PdfExportOptions.GetAdditionalData();
                var meta = UI_PdfExportOptions.GetProjectMetaData();
                var allAssemblyJson = PdfExporterLocal.ConvertToAssemblyJsonFull(assemblyDatas, additional);

                if (images != null && images.Count > 0 && allAssemblyJson.Count > 0)
                {
                    if (pagesWritten > 0)
                        doc.NewPage();
                    PdfExporterLocal.RenderSingleConfigPage(doc, writer, images, configTitle, configSubtitle, allAssemblyJson, meta);
                    pagesWritten++;
                }
                else
                {
                    Debug.LogWarning(
                        $"Skipping config {root.name} — missing image or data. " +
                        $"images={(images == null ? "null" : images.Count.ToString())} " +
                        $"assemblyJson={allAssemblyJson.Count}");
                }
            }

            doc.Close();
        }

        if (pagesWritten == 0)
        {
            try { if (File.Exists(filePath)) File.Delete(filePath); }
            catch (Exception e) { Debug.LogWarning($"Could not delete empty elevations PDF: {e.Message}"); }
        }

        LastMultipageExportOk = pagesWritten > 0;
        LastExportedPath = LastMultipageExportOk ? filePath : null;

        if (!suppressDialog)
        {
            UI_GeneralLoadingScreen.instance.HideLoadingScreen();
            UI_DialogPrompt.Open(
                "Elevation sheets saved.",
                new ButtonAction("Done"));
            ExportFolderUtility.RevealInFileManager(filePath);
        }
    }

    /// <summary>Result of the most recent multipage room elevations export.</summary>
    public static bool LastMultipageExportOk { get; private set; } = true;

    /// <summary>Path of the most recently written elevation PDF (file or folder for per-boom batches).</summary>
    public static string LastExportedPath { get; private set; }

    public static IEnumerator ExportPerAssemblyPdfs(
        string outputDirectory,
        string defaultTitle,
        string defaultSubtitle,
        bool suppressDialog = false,
        Func<bool> shouldCancel = null)
    {
        var allRoots = CollectBoomAssemblyRoots();
        int written = 0;
        LastPerAssemblyExportOk = false;

        string folder = outputDirectory ?? ExportPaths.ElevationsDir;
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        for (int i = 0; i < allRoots.Count; i++)
        {
            if (shouldCancel != null && shouldCancel())
                yield break;

            var root = allRoots[i];
            // Filename / sheet title should be the boom's catalog name, not a GUID instance id.
            string title = ExportPaths.GetSelectableExportName(root, defaultTitle ?? $"Configuration_{i + 1}");
            string subtitle = defaultSubtitle ?? string.Empty;

            yield return ExportSingleConfigToPdf(root, title, subtitle, folder, suppressDialog, shouldCancel);
            if (LastSingleConfigExportOk)
                written++;
        }

        LastPerAssemblyExportOk = written > 0;
        if (written > 1)
            LastExportedPath = folder;
    }

    /// <summary>Result of the most recent per-assembly elevations export.</summary>
    public static bool LastPerAssemblyExportOk { get; private set; } = true;

    public static IEnumerator ExportSingleConfigToPdf(Selectable rootSelectable, string title, string subtitle)
    {
        yield return ExportSingleConfigToPdf(rootSelectable, title, subtitle, null, suppressDialog: false);
    }

    public static IEnumerator ExportSingleConfigToPdf(
        Selectable rootSelectable,
        string title,
        string subtitle,
        string outputDirectory,
        bool suppressDialog = false,
        Func<bool> shouldCancel = null)
    {
        LastSingleConfigExportOk = false;

        string folder = outputDirectory ?? ExportPaths.ElevationsDir;
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        var assemblyDatas = UI_PdfExportOptions.GenerateAssemblyDataWithTitles(rootSelectable);

        List<PdfExporterLocal.PdfImageData> images = null;
        bool waiting = true;

        yield return rootSelectable.CapturePdfDataForExport(title, subtitle, assemblyDatas, (img, sel) =>
        {
            images = img;
            waiting = false;
        });

        yield return new WaitUntil(() => !waiting || (shouldCancel != null && shouldCancel()));
        if (shouldCancel != null && shouldCancel())
            yield break;

        if (images == null || images.Count == 0)
        {
            Debug.LogWarning($"Skipping elevation PDF for {rootSelectable?.name} — no captured images.");
            LastSingleConfigExportOk = false;
            yield break;
        }

        var additional = UI_PdfExportOptions.GetAdditionalData();
        var meta = UI_PdfExportOptions.GetProjectMetaData();
        var allAssemblyJson = PdfExporterLocal.ConvertToAssemblyJsonFull(assemblyDatas, additional);

        string safeTitle = string.Join("_", (title ?? "Export").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        string fileName = $"Elevation_{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        string filePath = Path.Combine(folder, fileName);

        using (FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (Document doc = new Document(new Rectangle(1400, 1200, 90), 19.08f, 19.08f, 10, 10))
        {
            PdfWriter writer = PdfWriter.GetInstance(doc, stream);
            doc.Open();
            PdfExporterLocal.RenderSingleConfigPage(doc, writer, images, title, subtitle, allAssemblyJson, meta);
            doc.Close();
        }

        LastSingleConfigExportOk = true;
        LastExportedPath = filePath;

        if (!suppressDialog)
        {
            UI_DialogPrompt.Open(
                "Elevation sheet saved.",
                new ButtonAction("Done"));
            ExportFolderUtility.RevealInFileManager(filePath);
        }
    }

    /// <summary>Result of the most recent <see cref="ExportSingleConfigToPdf"/> call.</summary>
    public static bool LastSingleConfigExportOk { get; private set; } = true;
}
