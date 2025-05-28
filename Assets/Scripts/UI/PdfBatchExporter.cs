
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using iTextSharp.text;
using iTextSharp.text.pdf;



public class PdfBatchExporter : MonoBehaviour
{

    public void ExportAllConfigs()
    {
       UI_GeneralLoadingScreen.instance.ShowLoadingScreen();
        StartCoroutine(ExportAllConfigsToMultipagePdf());

    }
    public static IEnumerator ExportAllConfigsToMultipagePdf()
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

        string folder = Path.Combine(FullRoomSave.GetRoomPath(), "pdf");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        string fileName = $"AllConfigs_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        string filePath = Path.Combine(folder, fileName);

        using (FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (Document doc = new Document(new Rectangle(1400, 1200, 90), 19.08f, 19.08f, 10, 10))
        {
            PdfWriter writer = PdfWriter.GetInstance(doc, stream);
            doc.Open();

            for (int i = 0; i < allRoots.Count; i++)
            {
                var root = allRoots[i];

           
                string title = $"Configuration {i + 1}";
                string subtitle = root.MetaData.Name;


                List<PdfExporterLocal.PdfImageData> images = null;
                List<Selectable> selectables = null;
                bool waiting = true;

                yield return root.CapturePdfDataForExport(title, subtitle, null, (img, sel) => {
                    images = img;
                    selectables = sel;
                    waiting = false;
                });

                yield return new WaitUntil(() => !waiting);

                var assemblyDatas = UI_PdfExportOptions.GenerateAssemblyDataWithTitles(root);
                var additional = UI_PdfExportOptions.GetAdditionalData();
                var meta = UI_PdfExportOptions.GetProjectMetaData();
                var allAssemblyJson = PdfExporterLocal.ConvertToAssemblyJsonFull(assemblyDatas, additional);

                if (images != null && images.Count > 0 && allAssemblyJson.Count > 0)
                {
                    PdfExporterLocal.RenderSingleConfigPage(doc, writer, images, title, subtitle, allAssemblyJson, meta);
                    if (i < allRoots.Count - 1)
                        doc.NewPage();
                }
                else
                {
                    Debug.LogWarning($"Skipping config {root.name} — Missing image or data.");
                }
            }

            doc.Close();
        }

        UI_GeneralLoadingScreen.instance.HideLoadingScreen();
        UI_DialogPrompt.Open(
      $"Success! PDF saved to {filePath}",
      new ButtonAction("Copy Path", () => GUIUtility.systemCopyBuffer = filePath),
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

        Application.OpenURL("file:///" + filePath);

      
    }

    public static IEnumerator ExportSingleConfigToPdf(Selectable rootSelectable, string title, string subtitle)
    {
        string folder = Path.Combine(FullRoomSave.GetRoomPath(), "pdf");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        string fileName = $"Export_{title}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        string filePath = Path.Combine(folder, fileName);

        using (FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        using (Document doc = new Document(new Rectangle(1400, 1200, 90), 19.08f, 19.08f, 10, 10))
        {
            PdfWriter writer = PdfWriter.GetInstance(doc, stream);
            doc.Open();

            List<PdfExporterLocal.PdfImageData> images = null;
            List<Selectable> selectables = null;
            bool waiting = true;

            yield return rootSelectable.CapturePdfDataForExport(title, subtitle, null, (img, sel) => {
                images = img;
                selectables = sel;
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

        UI_DialogPrompt.Open("Single configuration exported to PDF.");
        Application.OpenURL("file:///" + filePath);
    }

}
