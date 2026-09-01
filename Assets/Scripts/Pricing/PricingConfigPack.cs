using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using TriLibCore.SFB;
using UnityEngine;

/// <summary>
/// Zip of the live price sheet (Quote Request V6) plus saved adjustments.
/// </summary>
public static class PricingConfigPack
{
    const string ManifestName = "manifest.json";
    const string BridgeName = "pricing_bridge.json";
    const string SupplierFolder = "supplier/";
    const int FormatVersion = 2;

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
        string sheet = PricingBridgeStore.LiveSheetPath;
        if (string.IsNullOrEmpty(sheet) || !File.Exists(sheet))
        {
            UI_DialogPrompt.Open(
                $"Cannot export — price sheet missing:\n{PricingBridgeStore.BundledV6FileName}");
            return;
        }

        PricingBridgeStore.Load();
        PricingBridgeStore.Save();

        string defaultName = "Price List.zip";
        var dest = StandaloneFileBrowser.SaveFilePanel(
            "Export",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            defaultName,
            new[] { new ExtensionFilter("Price list", "zip") });

        if (dest == null || string.IsNullOrWhiteSpace(dest.Name))
            return;

        string path = dest.Name;
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            path += ".zip";

        try
        {
            ExportTo(path);
            UI_DialogPrompt.Open(
                path,
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
            "Import",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            new[] { new ExtensionFilter("Price list", "zip") },
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
            "Replace prices with this file?",
            new ButtonAction("Import", () =>
            {
                UI_DialogPrompt.Close();
                try
                {
                    ImportFrom(path);
                    UI_DialogPrompt.Open("Imported.", new ButtonAction("OK"));
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                    UI_DialogPrompt.Open($"Could not import that file:\n{ex.Message}");
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
        string sheet = PricingBridgeStore.LiveSheetPath;
        if (string.IsNullOrEmpty(sheet) || !File.Exists(sheet))
            throw new InvalidOperationException("Price sheet is missing.");

        string sheetName = Path.GetFileName(sheet);

        var manifest = new Manifest
        {
            formatVersion = FormatVersion,
            exportedUtc = DateTime.UtcNow.ToString("o"),
            catalogFileName = "",
            supplierFileName = sheetName,
            note = "Operating Room Software price list (Quote Request V6)"
        };

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        WriteEntry(zip, ManifestName, JsonUtility.ToJson(manifest, true));

        if (File.Exists(PricingBridgeStore.StatePath))
            zip.CreateEntryFromFile(PricingBridgeStore.StatePath, BridgeName);
        else
            WriteEntry(zip, BridgeName, JsonUtility.ToJson(new PricingBridgeState(), true));

        zip.CreateEntryFromFile(sheet, SupplierFolder + sheetName);
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

            string bridgeSrc = Path.Combine(tempRoot, BridgeName);
            if (!File.Exists(bridgeSrc))
                throw new InvalidOperationException("Pack is missing pricing_bridge.json.");

            string supplierName = Path.GetFileName(manifest.supplierFileName ?? "");
            string supplierSrc = string.IsNullOrEmpty(supplierName)
                ? null
                : Path.Combine(tempRoot, SupplierFolder, supplierName);

            if (string.IsNullOrEmpty(supplierSrc) || !File.Exists(supplierSrc))
            {
                string supplierDir = Path.Combine(tempRoot, "supplier");
                if (Directory.Exists(supplierDir))
                    supplierSrc = FirstXlsx(supplierDir);
                if (string.IsNullOrEmpty(supplierSrc) || !File.Exists(supplierSrc))
                    supplierSrc = FirstXlsx(tempRoot);
                if (!string.IsNullOrEmpty(supplierSrc) && File.Exists(supplierSrc))
                    supplierName = Path.GetFileName(supplierSrc);
            }

            if (LooksLikeLegacyXls(supplierSrc))
                throw new InvalidOperationException(
                    "This pack still uses the old price list. Export a new Price List.zip from this build.");

            if (string.IsNullOrEmpty(supplierSrc) || !File.Exists(supplierSrc))
            {
                if (!BridgeLooksLikeV6(bridgeSrc))
                    throw new InvalidOperationException(
                        "This pack has no Quote Request spreadsheet. Export a new Price List.zip from this build.");
                supplierName = "";
            }

            var reader = UnityEngine.Object.FindAnyObjectByType<ExcelReader>();
            reader?.InvalidateWorkbookCache();

            Directory.CreateDirectory(PricingBridgeStore.DataDirectory);
            Directory.CreateDirectory(PricingBridgeStore.SupplierDirectory);

            foreach (var old in Directory.GetFiles(PricingBridgeStore.SupplierDirectory))
            {
                try { File.Delete(old); } catch { /* ignore locked */ }
            }

            if (!string.IsNullOrEmpty(supplierSrc) && File.Exists(supplierSrc))
            {
                File.Copy(supplierSrc, Path.Combine(PricingBridgeStore.SupplierDirectory, supplierName), overwrite: true);
            }

            File.Copy(bridgeSrc, PricingBridgeStore.StatePath, overwrite: true);

            PricingBridgeStore.Load();
            var state = PricingBridgeStore.State;
            if (!string.IsNullOrEmpty(supplierName))
                state.supplierFileName = supplierName;
            PricingBridgeStore.Save();

            reader?.SetExcelFileName();
            PricingManager.Instance?.ClearCache();
            PricingManager.RebuildPricingFromTrackedObjects(forceReload: true);
            PricingBridgeStore.NotifyChanged();

            var sb = new StringBuilder();
            sb.AppendLine("Prices updated.");
            if (!string.IsNullOrEmpty(supplierName))
                sb.AppendLine("Spreadsheet: " + supplierName);
            if (!string.IsNullOrEmpty(state.lastImportSummary))
                sb.AppendLine(state.lastImportSummary);
            return sb.ToString();
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { /* ignore */ }
        }
    }

    static string FirstXlsx(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        foreach (var file in Directory.GetFiles(dir, "*.xlsx"))
            return file;
        foreach (var file in Directory.GetFiles(dir, "*.xls"))
            return file;
        return null;
    }

    static bool LooksLikeLegacyXls(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        string name = Path.GetFileName(path);
        if (name.IndexOf("Boom_GroupPricing", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return Path.GetExtension(path).Equals(".xls", StringComparison.OrdinalIgnoreCase)
            && !Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
    }

    static bool BridgeLooksLikeV6(string bridgePath)
    {
        try
        {
            var loaded = JsonUtility.FromJson<PricingBridgeState>(File.ReadAllText(bridgePath));
            if (loaded?.items == null || loaded.items.Count == 0) return false;
            int v6 = 0;
            foreach (var i in loaded.items)
            {
                if (i == null) continue;
                if (i.sheetName == "Light" || i.sheetName == "Boom" || i.sheetName == "Install")
                    v6++;
            }
            return v6 > loaded.items.Count / 2;
        }
        catch
        {
            return false;
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
