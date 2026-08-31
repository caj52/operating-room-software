using System;
using System.IO;
using System.Text;
using TriLibCore.SFB;
using UnityEngine;

/// <summary>
/// Supplier Excel picker: copies into StreamingAssets/supplier and merges prices into the bridge.
/// The app catalog (Boom_GroupPricing) stays the live lookup list.
/// </summary>
public static class PricingSheetAdmin
{
    public static string StatusSummary => PricingBridgeStore.SupplierStatusSummary;

    public static void ShowStatusDialog()
    {
        var state = PricingBridgeStore.State;
        var body = new StringBuilder();
        body.AppendLine("App price list (used for lookups):");
        body.AppendLine(PricingBridgeStore.CatalogFileName);
        body.AppendLine();
        if (string.IsNullOrEmpty(state.supplierFileName))
        {
            body.AppendLine("No supplier Excel loaded yet.");
            body.AppendLine("Choose Update to import prices from a Quote Request / supplier sheet.");
        }
        else
        {
            body.AppendLine("Supplier Excel (price updates):");
            body.AppendLine(state.supplierFileName);
            if (!string.IsNullOrEmpty(state.lastImportSummary))
            {
                body.AppendLine();
                body.AppendLine(state.lastImportSummary);
            }
        }

        UI_DialogPrompt.Open(
            body.ToString(),
            new ButtonAction("Review gaps…", () =>
            {
                UI_DialogPrompt.Close();
                UI_PricingBridge.Open();
            }),
            new ButtonAction("Export pack…", () =>
            {
                UI_DialogPrompt.Close();
                PricingConfigPack.BeginExport();
            }),
            new ButtonAction("Update from Excel…", () =>
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

        string samples = Path.Combine(PricingBridgeStore.QuotesDirectory, "supplier_samples");
        string startDir = Directory.Exists(samples)
            ? samples
            : (Directory.Exists(PricingBridgeStore.QuotesDirectory)
                ? PricingBridgeStore.QuotesDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        var items = StandaloneFileBrowser.OpenFilePanel(
            "Choose Imagine’s price spreadsheet",
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

        string sourceName = Path.GetFileName(sourcePath);
        UI_DialogPrompt.Open(
            $"Load prices from:\n\n{sourceName}\n\n" +
            "Matching items get new prices. Anything we can’t match shows up as missing so you can type it in.\n\nContinue?",
            new ButtonAction("Load", () =>
            {
                UI_DialogPrompt.Close();
                RunImport(sourcePath);
            }),
            new ButtonAction("Cancel", UI_DialogPrompt.Close));
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
                "Problem with the spreadsheet:\n\n" + detail,
                new ButtonAction("OK"));
            return;
        }

        UI_DialogPrompt.Open(
            "Done.\n\n" + result.Message +
            (result.Missing > 0
                ? "\n\nFill in the missing prices on the list."
                : ""),
            new ButtonAction("OK"));
    }
}
