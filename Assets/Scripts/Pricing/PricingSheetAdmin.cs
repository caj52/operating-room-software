using System;
using System.IO;
using System.Text;
using TriLibCore.SFB;
using UnityEngine;

/// <summary>
/// Opens the current supplier spreadsheet (Quote Request V6) and merges prices.
/// </summary>
public static class PricingSheetAdmin
{
    public static string StatusSummary => PricingBridgeStore.SupplierStatusSummary;

    public static void ShowStatusDialog()
    {
        var state = PricingBridgeStore.State;
        var body = new StringBuilder();
        body.AppendLine("Price sheet:");
        body.AppendLine(UI_PricingBridge.DisplayFileName(string.IsNullOrEmpty(state.supplierFileName)
            ? PricingBridgeStore.BundledV6FileName
            : state.supplierFileName));
        if (!string.IsNullOrEmpty(state.lastImportSummary))
        {
            body.AppendLine();
            body.AppendLine(state.lastImportSummary);
        }

        UI_DialogPrompt.Open(
            body.ToString(),
            new ButtonAction("Change spreadsheet…", () =>
            {
                UI_DialogPrompt.Close();
                BeginReplaceWithFilePicker();
            }),
            new ButtonAction("Close"));
    }

    public static void BeginReplaceWithFilePicker()
    {
        var extensions = new[]
        {
            new ExtensionFilter("Excel", "xls", "xlsx"),
        };

        string startDir = Directory.Exists(PricingBridgeStore.QuotesDirectory)
            ? PricingBridgeStore.QuotesDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var items = StandaloneFileBrowser.OpenFilePanel(
            "Change spreadsheet",
            startDir,
            extensions,
            false);

        if (items == null || items.Count == 0 || string.IsNullOrWhiteSpace(items[0]?.Name))
            return;

        string sourcePath = items[0].Name;
        if (!File.Exists(sourcePath))
        {
            UI_DialogPrompt.Open($"File not found:\n{sourcePath}");
            return;
        }

        RunImport(sourcePath);
    }

    static void RunImport(string sourcePath)
    {
        var result = PricingBridgeImporter.ImportFromFile(sourcePath);
        if (!result.Ok)
        {
            string detail = result.Problems != null && result.Problems.Count > 0
                ? string.Join("\n\n", result.Problems)
                : result.Message;
            UI_DialogPrompt.Open(
                "Couldn’t read that spreadsheet:\n\n" + detail,
                new ButtonAction("OK"));
            return;
        }

        UI_DialogPrompt.Open(
            "Now using:\n" + UI_PricingBridge.DisplayFileName(Path.GetFileName(sourcePath)),
            new ButtonAction("OK"));
    }
}
