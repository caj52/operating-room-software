using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using TriLibCore.SFB;
using UnityEngine;

/// <summary>
/// Zip pack of the full pricing setup: app price list, supplier Excel, and bridge adjustments.
/// </summary>
public static class PricingConfigPack
{
    const string ManifestName = "manifest.json";
    const string BridgeName = "pricing_bridge.json";
    const string CatalogFolder = "catalog/";
    const string SupplierFolder = "supplier/";
    const int FormatVersion = 1;

    [Serializable]
    class Manifest
    {
        public int formatVersion = FormatVersion;
        public string exportedUtc;
        public string catalogFileName;
        public string supplierFileName;
        public string note;
    }

    public static void BeginExport()
    {
        if (!File.Exists(PricingBridgeStore.CatalogPath))
        {
            UI_DialogPrompt.Open(
                $"Cannot export — app price list missing:\n{PricingBridgeStore.CatalogFileName}");
            return;
        }

        PricingBridgeStore.Load();
        PricingBridgeStore.Save();

        string defaultName = $"PricingConfig_{DateTime.Now:yyyyMMdd_HHmm}.zip";
        var dest = StandaloneFileBrowser.SaveFilePanel(
            "Export pricing configuration",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            defaultName,
            new[] { new ExtensionFilter("Pricing config pack", "zip") });

        if (dest == null || string.IsNullOrWhiteSpace(dest.Name))
            return;

        string path = dest.Name;
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            path += ".zip";

        try
        {
            ExportTo(path);
            UI_DialogPrompt.Open(
                $"Prices exported.\n\n{path}\n\nGive this zip to a coworker. They open Pricing and choose Load coworker file.",
                new ButtonAction("Show file", () =>
                {
                    UI_DialogPrompt.Close();
                    ExportFolderUtility.RevealInFileManager(path);
                }),
                new ButtonAction("OK"));
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            UI_DialogPrompt.Open($"Export failed:\n{ex.Message}");
        }
    }

    public static void BeginImport()
    {
        var items = StandaloneFileBrowser.OpenFilePanel(
            "Import pricing configuration",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            new[] { new ExtensionFilter("Pricing config pack", "zip") },
            false);

        if (items == null || items.Count == 0 || string.IsNullOrWhiteSpace(items[0]?.Name))
            return;

        string path = items[0].Name;
        if (!File.Exists(path))
        {
            UI_DialogPrompt.Open($"File not found:\n{path}");
            return;
        }

        UI_DialogPrompt.Open(
            "Load this coworker file?\n\n" +
            "This replaces prices on this computer with theirs.",
            new ButtonAction("Load", () =>
            {
                UI_DialogPrompt.Close();
                try
                {
                    string summary = ImportFrom(path);
                    UI_DialogPrompt.Open(
                        "Loaded.\n\n" + summary,
                        new ButtonAction("OK"));
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                    UI_DialogPrompt.Open($"Could not load that file:\n{ex.Message}");
                }
            }),
            new ButtonAction("Cancel", UI_DialogPrompt.Close));
    }

    static void ExportTo(string zipPath)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        PricingBridgeStore.Load();
        var state = PricingBridgeStore.State;

        var manifest = new Manifest
        {
            formatVersion = FormatVersion,
            exportedUtc = DateTime.UtcNow.ToString("o"),
            catalogFileName = PricingBridgeStore.CatalogFileName,
            supplierFileName = state.supplierFileName,
            note = "Operating Room Software pricing configuration pack"
        };

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        WriteEntry(zip, ManifestName, JsonUtility.ToJson(manifest, true));

        if (File.Exists(PricingBridgeStore.StatePath))
            zip.CreateEntryFromFile(PricingBridgeStore.StatePath, BridgeName);
        else
            WriteEntry(zip, BridgeName, JsonUtility.ToJson(new PricingBridgeState(), true));

        zip.CreateEntryFromFile(
            PricingBridgeStore.CatalogPath,
            CatalogFolder + PricingBridgeStore.CatalogFileName);

        string supplierPath = PricingBridgeStore.SupplierFilePath;
        if (!string.IsNullOrEmpty(state.supplierFileName)
            && !string.IsNullOrEmpty(supplierPath)
            && File.Exists(supplierPath))
        {
            zip.CreateEntryFromFile(supplierPath, SupplierFolder + state.supplierFileName);
        }
    }

    static string ImportFrom(string zipPath)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "ors_pricing_pack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempRoot);

            string manifestPath = Path.Combine(tempRoot, ManifestName);
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("Not a pricing config pack (missing manifest.json).");

            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if (manifest == null)
                throw new InvalidOperationException("Could not read pack manifest.");
            if (manifest.formatVersion > FormatVersion)
                throw new InvalidOperationException(
                    $"This pack is format v{manifest.formatVersion}; this app only supports up to v{FormatVersion}.");

            string catalogName = string.IsNullOrWhiteSpace(manifest.catalogFileName)
                ? PricingBridgeStore.CatalogFileName
                : Path.GetFileName(manifest.catalogFileName);

            string catalogSrc = Path.Combine(tempRoot, CatalogFolder, catalogName);
            if (!File.Exists(catalogSrc))
            {
                // Allow pack that used a different catalog filename than we expect.
                string catalogDir = Path.Combine(tempRoot, "catalog");
                if (Directory.Exists(catalogDir))
                {
                    var files = Directory.GetFiles(catalogDir);
                    if (files.Length == 1)
                    {
                        catalogSrc = files[0];
                        catalogName = Path.GetFileName(catalogSrc);
                    }
                }
            }

            if (!File.Exists(catalogSrc))
                throw new InvalidOperationException("Pack is missing the app price list Excel.");

            string catalogExt = Path.GetExtension(catalogSrc)?.ToLowerInvariant();
            if (catalogExt != ".xls")
            {
                throw new InvalidOperationException(
                    "Pack catalog must be an .xls app price list (Boom_GroupPricing style). " +
                    $"This pack has '{Path.GetFileName(catalogSrc)}'.");
            }

            // Reject obvious xlsx bytes saved with a wrong extension.
            try
            {
                using var probe = File.OpenRead(catalogSrc);
                if (probe.Length >= 2)
                {
                    int b0 = probe.ReadByte();
                    int b1 = probe.ReadByte();
                    // ZIP/xlsx signature PK
                    if (b0 == 'P' && b1 == 'K')
                        throw new InvalidOperationException(
                            "Pack catalog looks like an .xlsx Quote Request file, not the app .xls price list.");
                }
            }
            catch (InvalidOperationException) { throw; }
            catch { /* probe failed; copy will surface errors later */ }

            string bridgeSrc = Path.Combine(tempRoot, BridgeName);
            if (!File.Exists(bridgeSrc))
                throw new InvalidOperationException("Pack is missing pricing_bridge.json.");

            // Close workbook before replacing catalog on disk.
            var reader = UnityEngine.Object.FindObjectOfType<ExcelReader>();
            reader?.InvalidateWorkbookCache();

            Directory.CreateDirectory(PricingBridgeStore.QuotesDirectory);
            Directory.CreateDirectory(PricingBridgeStore.SupplierDirectory);

            // Always install catalog under the filename this build expects.
            string catalogDest = PricingBridgeStore.CatalogPath;
            File.Copy(catalogSrc, catalogDest, overwrite: true);
            DataFilePaths.ExcelFileNameForLightAndBoomPricing = PricingBridgeStore.CatalogFileName;

            // Clear previous supplier copies, then install pack supplier if present.
            foreach (var old in Directory.GetFiles(PricingBridgeStore.SupplierDirectory))
            {
                try { File.Delete(old); } catch { /* ignore locked */ }
            }

            string supplierName = Path.GetFileName(manifest.supplierFileName ?? "");
            string supplierSrc = string.IsNullOrEmpty(supplierName)
                ? null
                : Path.Combine(tempRoot, SupplierFolder, supplierName);

            if (string.IsNullOrEmpty(supplierSrc) || !File.Exists(supplierSrc))
            {
                string supplierDir = Path.Combine(tempRoot, "supplier");
                if (Directory.Exists(supplierDir))
                {
                    var files = Directory.GetFiles(supplierDir);
                    if (files.Length >= 1)
                    {
                        supplierSrc = files[0];
                        supplierName = Path.GetFileName(supplierSrc);
                    }
                }
            }

            if (!string.IsNullOrEmpty(supplierSrc) && File.Exists(supplierSrc))
            {
                File.Copy(supplierSrc, Path.Combine(PricingBridgeStore.SupplierDirectory, supplierName), overwrite: true);
            }
            else
            {
                supplierName = "";
            }

            File.Copy(bridgeSrc, PricingBridgeStore.StatePath, overwrite: true);

            // Keep bridge supplier filename in sync with what we installed.
            PricingBridgeStore.Load();
            var state = PricingBridgeStore.State;
            state.supplierFileName = supplierName;
            PricingBridgeStore.Save();

            reader?.SetExcelFileName();
            PricingManager.Instance?.ClearCache();
            PricingManager.RebuildPricingFromTrackedObjects(forceReload: true);
            PricingBridgeStore.NotifyChanged();

            var sb = new StringBuilder();
            sb.AppendLine("App price list updated.");
            if (!string.IsNullOrEmpty(supplierName))
                sb.AppendLine("Supplier Excel: " + supplierName);
            else
                sb.AppendLine("No supplier Excel in this pack.");
            if (!string.IsNullOrEmpty(state.lastImportSummary))
                sb.AppendLine(state.lastImportSummary);
            int gaps = 0;
            if (state.items != null)
            {
                foreach (var i in state.items)
                    if (i != null && i.missingFromSupplier) gaps++;
            }
            sb.AppendLine($"Gaps needing review: {gaps}");
            return sb.ToString();
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { /* ignore */ }
        }
    }

    static void WriteEntry(ZipArchive zip, string entryName, string text)
    {
        var entry = zip.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }
}
