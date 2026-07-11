using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public static IEnumerator ExportAllConfigsToMultipagePdf(
        string outputDirectory,
        string defaultTitle,
        string defaultSubtitle,
        bool suppressDialog = false)
    {
        var allRoots = FindObjectsOfType<Selectable>()
            .Where(s => s.TryGetArmAssemblyRoot(out _))
            .Select(s =>
            {
                s.TryGetArmAssemblyRoot(out GameObject root);
                return root.GetComponent<Selectable>();
            })
            .Distinct()
            .ToList();

        string folder = outputDirectory ?? ExportPaths.ElevationsDir;
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        string fileName = $"RoomElevations_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        string filePath = Path.Combine(folder, fileName);

        string title = defaultTitle ?? ExportPaths.GetRoomExportName();
        string subtitle = defaultSubtitle ?? string.Empty;

        using (FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (Document doc = new Document(new Rectangle(1400, 1200, 90), 19.08f, 19.08f, 10, 10))
        {
            PdfWriter writer = PdfWriter.GetInstance(doc, stream);
            doc.Open();

            for (int i = 0; i < allRoots.Count; i++)
            {
                var root = allRoots[i];
                string configTitle = $"{title} — Configuration {i + 1}";
                string configSubtitle = string.IsNullOrWhiteSpace(root.MetaData?.Name)
                    ? subtitle
                    : root.MetaData.Name;

                List<PdfExporterLocal.PdfImageData> images = null;
                bool waiting = true;

                yield return root.CapturePdfDataForExport(configTitle, configSubtitle, null, (img, sel) =>
                {
                    images = img;
                    waiting = false;
                });

                yield return new WaitUntil(() => !waiting);

                var assemblyDatas = UI_PdfExportOptions.GenerateAssemblyDataWithTitles(root);
                var additional = UI_PdfExportOptions.GetAdditionalData();
                var meta = UI_PdfExportOptions.GetProjectMetaData();
                var allAssemblyJson = PdfExporterLocal.ConvertToAssemblyJsonFull(assemblyDatas, additional);

                if (images != null && images.Count > 0 && allAssemblyJson.Count > 0)
                {
                    PdfExporterLocal.RenderSingleConfigPage(doc, writer, images, configTitle, configSubtitle, allAssemblyJson, meta);
                    if (i < allRoots.Count - 1)
                        doc.NewPage();
                }
                else
                {
                    Debug.LogWarning($"Skipping config {root.name} — missing image or data.");
                }
            }

            doc.Close();
        }

        if (!suppressDialog)
        {
            UI_GeneralLoadingScreen.instance.HideLoadingScreen();
            UI_DialogPrompt.Open(
                $"Elevation sheets saved to:\n{filePath}",
                new ButtonAction("Open Folder", () => ExportFolderUtility.RevealInFileManager(filePath)),
                new ButtonAction("Done"));
        }
    }

    public static IEnumerator ExportPerAssemblyPdfs(
        string outputDirectory,
        string defaultTitle,
        string defaultSubtitle,
        bool suppressDialog = false)
    {
        var allRoots = FindObjectsOfType<Selectable>()
            .Where(s => s.TryGetArmAssemblyRoot(out _))
            .Select(s =>
            {
                s.TryGetArmAssemblyRoot(out GameObject root);
                return root.GetComponent<Selectable>();
            })
            .Distinct()
            .ToList();

        string folder = outputDirectory ?? ExportPaths.ElevationsDir;
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        for (int i = 0; i < allRoots.Count; i++)
        {
            var root = allRoots[i];
            string title = defaultTitle ?? $"Configuration {i + 1}";
            string subtitle = string.IsNullOrWhiteSpace(root.MetaData?.Name)
                ? (defaultSubtitle ?? string.Empty)
                : root.MetaData.Name;

            yield return ExportSingleConfigToPdf(root, title, subtitle, folder, suppressDialog);
        }
    }

    public static IEnumerator ExportSingleConfigToPdf(Selectable rootSelectable, string title, string subtitle)
    {
        yield return ExportSingleConfigToPdf(rootSelectable, title, subtitle, null, suppressDialog: false);
    }

    public static IEnumerator ExportSingleConfigToPdf(
        Selectable rootSelectable,
        string title,
        string subtitle,
        string outputDirectory,
        bool suppressDialog = false)
    {
        string folder = outputDirectory ?? ExportPaths.ElevationsDir;
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        string safeTitle = string.Join("_", (title ?? "Export").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        string fileName = $"Elevation_{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        string filePath = Path.Combine(folder, fileName);

        using (FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (Document doc = new Document(new Rectangle(1400, 1200, 90), 19.08f, 19.08f, 10, 10))
        {
            PdfWriter writer = PdfWriter.GetInstance(doc, stream);
            doc.Open();

            List<PdfExporterLocal.PdfImageData> images = null;
            bool waiting = true;

            yield return rootSelectable.CapturePdfDataForExport(title, subtitle, null, (img, sel) =>
            {
                images = img;
                waiting = false;
            });

            yield return new WaitUntil(() => !waiting);

            var assemblyDatas = UI_PdfExportOptions.GenerateAssemblyDataWithTitles(rootSelectable);
            var additional = UI_PdfExportOptions.GetAdditionalData();
            var meta = UI_PdfExportOptions.GetProjectMetaData();
            var allAssemblyJson = PdfExporterLocal.ConvertToAssemblyJsonFull(assemblyDatas, additional);

            PdfExporterLocal.RenderSingleConfigPage(doc, writer, images, title, subtitle, allAssemblyJson, meta);
            doc.Close();
        }

        if (!suppressDialog)
        {
            UI_DialogPrompt.Open(
                $"Elevation sheet saved to:\n{filePath}",
                new ButtonAction("Open Folder", () => ExportFolderUtility.RevealInFileManager(filePath)),
                new ButtonAction("Done"));
        }
    }
}
