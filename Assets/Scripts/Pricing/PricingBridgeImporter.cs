using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using UnityEngine;

/// <summary>
/// Builds catalog rows from Boom_GroupPricing and merges prices from a supplier Excel.
/// </summary>
public static class PricingBridgeImporter
{
    public struct ImportResult
    {
        public bool Ok;
        public string Message;
        public int CatalogCount;
        public int Matched;
        public int Missing;
        public List<string> Problems;
    }

    /// <summary>
    /// Match the app price list to a Quote Request / supplier Excel without writing files
    /// or touching the scene. Used by editor diagnostics and by <see cref="ImportFromFile"/>.
    /// </summary>
    public sealed class MatchReport
    {
        public bool Ok;
        public string Message;
        public int CatalogCount;
        public int Matched;
        public int Missing;
        public int SupplierRowCount;
        public int LightSupplierRows;
        public int BoomSupplierRows;
        public int InstallSupplierRows;
        public List<string> Problems = new();
        public List<PricingBridgeItem> Items = new();
    }

    public static ImportResult ImportFromFile(string sourcePath)
    {
        var report = Analyze(sourcePath);
        var result = new ImportResult
        {
            Ok = report.Ok,
            Message = report.Message,
            CatalogCount = report.CatalogCount,
            Matched = report.Matched,
            Missing = report.Missing,
            Problems = report.Problems
        };
        if (!report.Ok)
            return result;

        PersistMatch(report, sourcePath);
        return result;
    }

    public static MatchReport Analyze(string sourcePath)
    {
        var report = new MatchReport();

        if (!File.Exists(sourcePath))
        {
            report.Message = "Selected file was not found.";
            report.Problems.Add(report.Message);
            return report;
        }

        if (!File.Exists(PricingBridgeStore.CatalogPath))
        {
            report.Message = $"App price list missing:\n{PricingBridgeStore.CatalogFileName}";
            report.Problems.Add(report.Message);
            return report;
        }

        if (!SupplierExcelReader.TryOpen(sourcePath, out var sheets, out string openError))
        {
            report.Message = openError;
            report.Problems.Add(openError);
            return report;
        }

        var supplierPrices = ExtractSupplierPrices(sheets, report.Problems);
        report.SupplierRowCount = supplierPrices.Count;
        report.LightSupplierRows = supplierPrices.Count(s => s.Family == "Light");
        report.BoomSupplierRows = supplierPrices.Count(s => s.Family == "Boom");
        report.InstallSupplierRows = supplierPrices.Count(s => s.Family == "Install");

        if (supplierPrices.Count == 0)
        {
            report.Message =
                "Opened the Excel file, but could not find usable price rows.\n" +
                "Expected sheets like “Simeon Lights”, “Ondal Booms”, or “Install Pricelist”, " +
                "or the older Boom/Light pricing tabs.";
            report.Problems.Add(report.Message);
            return report;
        }

        List<PricingBridgeItem> catalog;
        try
        {
            catalog = LoadCatalogItems();
        }
        catch (Exception ex)
        {
            report.Message = $"Could not read the app price list: {ex.Message}";
            report.Problems.Add(report.Message);
            return report;
        }

        if (catalog.Count == 0)
        {
            report.Message = "App price list opened but had no priced rows.";
            report.Problems.Add(report.Message);
            return report;
        }

        int matched = 0;
        foreach (var item in catalog)
        {
            if (TryMatch(item, supplierPrices, out double price, out string matchLabel))
            {
                item.effectivePrice = price;
                item.source = "Supplier";
                item.supplierMatch = matchLabel;
                item.missingFromSupplier = false;
                matched++;
            }
            else
            {
                item.effectivePrice = item.catalogPrice;
                item.source = "Catalog";
                item.supplierMatch = null;
                item.missingFromSupplier = true;
            }
        }

        report.Ok = true;
        report.Items = catalog;
        report.CatalogCount = catalog.Count;
        report.Matched = matched;
        report.Missing = catalog.Count - matched;
        report.Message = $"Updated {matched} of {catalog.Count} prices. Still missing: {catalog.Count - matched}.";
        return report;
    }

    static void PersistMatch(MatchReport report, string sourcePath)
    {
        Directory.CreateDirectory(PricingBridgeStore.SupplierDirectory);
        foreach (var old in Directory.GetFiles(PricingBridgeStore.SupplierDirectory))
        {
            try { File.Delete(old); } catch { /* ignore */ }
        }

        string destName = Path.GetFileName(sourcePath);
        string destPath = Path.Combine(PricingBridgeStore.SupplierDirectory, destName);
        File.Copy(sourcePath, destPath, overwrite: true);

        PricingBridgeStore.ReplaceItems(report.Items, destName, report.Message);

        PricingManager.Instance?.ClearCache();
        var reader = UnityEngine.Object.FindAnyObjectByType<ExcelReader>();
        reader?.InvalidateWorkbookCache();
        PricingManager.RebuildPricingFromTrackedObjects(forceReload: true);
        PricingBridgeStore.NotifyChanged();
    }

    static List<PricingBridgeItem> LoadCatalogItems()
    {
        using var stream = new FileStream(PricingBridgeStore.CatalogPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        IWorkbook wb = new HSSFWorkbook(stream);
        var items = new List<PricingBridgeItem>();

        foreach (var mapping in DataFilePaths.SheetColumnMappings)
        {
            string sheetName = mapping.Key;
            var cols = mapping.Value;
            ISheet sheet = wb.GetSheet(sheetName);
            if (sheet == null) continue;

            bool isLightSheet = sheetName.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0;

            for (int r = 0; r <= sheet.LastRowNum; r++)
            {
                IRow row = sheet.GetRow(r);
                if (row == null) continue;

                string name = GetCell(row, cols.ObjectName);
                string priceRaw = GetCell(row, cols.ListPrice);
                if (string.IsNullOrWhiteSpace(name) || !SupplierExcelReader.TryParsePrice(priceRaw, out double price))
                    continue;

                if (name.Equals("Configuration", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Base Model", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Object Name", StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf("3D Tool Config", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                // Light sheets: col 1 is usually "7000 - LED / LED" style configuration.
                string configuration = isLightSheet ? (GetCell(row, 1) ?? "") : "";

                items.Add(new PricingBridgeItem
                {
                    sheetName = sheetName,
                    partNumber = GetCell(row, cols.PartNumber) ?? "",
                    configurationName = configuration.Trim(),
                    objectName = name.Trim(),
                    size = cols.ObjectSize >= 0 ? (GetCell(row, cols.ObjectSize) ?? "") : "",
                    catalogPrice = price,
                    effectivePrice = price,
                    source = "Catalog",
                    missingFromSupplier = true
                });
            }
        }

        return items;
    }

    static string GetCell(IRow row, int col)
    {
        if (col < 0) return null;
        ICell cell = row.GetCell(col);
        if (cell == null) return null;
        if (cell.CellType == CellType.Numeric)
            return cell.NumericCellValue.ToString("G", CultureInfo.InvariantCulture);
        return cell.ToString()?.Trim();
    }

    class SupplierPriceRow
    {
        public string PartNumber;
        public string Name;
        public double Price;
        public string Label;
        public string Family; // Light | Boom | Install | Legacy
    }

    static List<SupplierPriceRow> ExtractSupplierPrices(
        List<SupplierExcelReader.SheetTable> sheets,
        List<string> problems)
    {
        var prices = new List<SupplierPriceRow>();
        var names = sheets.Select(s => s.Name).ToList();

        bool looksLikeQuoteRequest =
            names.Any(n => n.IndexOf("Simeon", StringComparison.OrdinalIgnoreCase) >= 0)
            || names.Any(n => n.IndexOf("Ondal", StringComparison.OrdinalIgnoreCase) >= 0)
            || names.Any(n => n.IndexOf("Install Pricelist", StringComparison.OrdinalIgnoreCase) >= 0);

        bool looksLikeLegacyCatalog =
            names.Any(n => n.IndexOf("3D Light Pricing", StringComparison.OrdinalIgnoreCase) >= 0)
            || names.Any(n => n.IndexOf("3D Boom Pricing", StringComparison.OrdinalIgnoreCase) >= 0);

        if (!looksLikeQuoteRequest && !looksLikeLegacyCatalog)
        {
            problems.Add(
                "Unrecognized spreadsheet layout. Sheet tabs found:\n- " +
                string.Join("\n- ", names.Take(12)) +
                (names.Count > 12 ? "\n- …" : ""));
        }

        foreach (var sheet in sheets)
        {
            if (sheet.Name.IndexOf("Simeon Lights", StringComparison.OrdinalIgnoreCase) >= 0)
                ExtractSimeon(sheet, prices);
            else if (sheet.Name.IndexOf("Ondal Booms", StringComparison.OrdinalIgnoreCase) >= 0)
                ExtractOndal(sheet, prices);
            else if (sheet.Name.IndexOf("Install Pricelist", StringComparison.OrdinalIgnoreCase) >= 0)
                ExtractInstall(sheet, prices);
            else if (looksLikeLegacyCatalog
                     && (sheet.Name.IndexOf("3D Light Pricing", StringComparison.OrdinalIgnoreCase) >= 0
                         || sheet.Name.IndexOf("3D Boom Pricing", StringComparison.OrdinalIgnoreCase) >= 0
                         || sheet.Name.Equals("2025_03_28 Boom Pricing", StringComparison.OrdinalIgnoreCase)
                         || sheet.Name.Equals("2025_03_28 Light Pricing", StringComparison.OrdinalIgnoreCase)))
                ExtractLegacyCatalogSheet(sheet, prices);
        }

        return prices;
    }

    static void ExtractSimeon(SupplierExcelReader.SheetTable sheet, List<SupplierPriceRow> prices)
    {
        foreach (var row in sheet.Rows)
        {
            if (row.Length < 8) continue;
            string part = Cell(row, 1);
            string name = Cell(row, 2);
            if (string.IsNullOrWhiteSpace(part) && string.IsNullOrWhiteSpace(name)) continue;
            if (part != null && part.IndexOf("Part", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (!SupplierExcelReader.TryParsePrice(Cell(row, 7), out double price)) continue;
            prices.Add(new SupplierPriceRow
            {
                PartNumber = part,
                Name = name,
                Price = price,
                Family = "Light",
                Label = $"Simeon Lights / {part} / {name}"
            });
        }
    }

    static void ExtractOndal(SupplierExcelReader.SheetTable sheet, List<SupplierPriceRow> prices)
    {
        // Prefer the first priced occurrence of each base-model name (header table).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in sheet.Rows)
        {
            if (row.Length < 5) continue;
            string name = Cell(row, 1);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (name.IndexOf("Base Model", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (LooksLikeArmLengthAddon(name)) continue;
            if (LooksLikeSapPart(Cell(row, 0))) continue;
            if (!SupplierExcelReader.TryParsePrice(Cell(row, 4), out double price) || price <= 0.5) continue;
            string key = NormalizeKey(name);
            if (string.IsNullOrEmpty(key) || !seen.Add(key)) continue;
            prices.Add(new SupplierPriceRow
            {
                Name = name.Trim(),
                Price = price,
                Family = "Boom",
                Label = $"Ondal Booms / {name.Trim()}"
            });
        }
    }

    static void ExtractInstall(SupplierExcelReader.SheetTable sheet, List<SupplierPriceRow> prices)
    {
        foreach (var row in sheet.Rows)
        {
            if (row.Length < 2) continue;
            string name = Cell(row, 0);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (name.IndexOf("Ondal SAP", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            string priceRaw = row.Length > 3 ? Cell(row, 3) : Cell(row, 1);
            if (!SupplierExcelReader.TryParsePrice(priceRaw, out double price)) continue;
            prices.Add(new SupplierPriceRow
            {
                Name = name,
                Price = price,
                Family = "Install",
                Label = $"Install Pricelist / {name}"
            });
        }
    }

    static void ExtractLegacyCatalogSheet(SupplierExcelReader.SheetTable sheet, List<SupplierPriceRow> prices)
    {
        // Same shape as Boom_GroupPricing mapping sheets: part / config-or-name / list price.
        bool light = sheet.Name.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0;
        foreach (var row in sheet.Rows)
        {
            if (row.Length < 4) continue;
            string part = Cell(row, 0);
            string configOrName = Cell(row, light ? 1 : 1);
            string objectName = light && row.Length > 2 ? Cell(row, 2) : configOrName;
            string priceRaw = light ? Cell(row, 3) : (row.Length > 4 ? Cell(row, 4) : Cell(row, 3));
            if (string.IsNullOrWhiteSpace(objectName)) continue;
            if (!SupplierExcelReader.TryParsePrice(priceRaw, out double price)) continue;
            if (objectName.IndexOf("Configuration", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            prices.Add(new SupplierPriceRow
            {
                PartNumber = part,
                Name = string.IsNullOrWhiteSpace(configOrName) ? objectName : configOrName,
                Price = price,
                Family = light ? "Light" : "Boom",
                Label = $"{sheet.Name} / {objectName}"
            });
        }
    }

    static string Cell(string[] row, int i) =>
        i >= 0 && i < row.Length ? row[i]?.Trim() : null;

    static bool TryMatch(
        PricingBridgeItem item,
        List<SupplierPriceRow> supplier,
        out double price,
        out string label)
    {
        price = 0;
        label = null;

        bool itemIsLight = (item.sheetName ?? "").IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0;
        bool itemIsBoom = (item.sheetName ?? "").IndexOf("Boom", StringComparison.OrdinalIgnoreCase) >= 0;

        IEnumerable<SupplierPriceRow> pool = supplier;
        if (itemIsLight)
            pool = supplier.Where(s => s.Family == "Light");
        else if (itemIsBoom)
            pool = supplier.Where(s => s.Family == "Boom");

        var list = pool.ToList();
        string part = NormalizeKey(item.partNumber);
        string objectKey = NormalizeKey(item.objectName);
        string configKey = NormalizeKey(item.configurationName);

        if (!string.IsNullOrEmpty(part))
        {
            var byPart = list.FirstOrDefault(s => NormalizeKey(s.PartNumber) == part);
            if (byPart != null)
            {
                price = byPart.Price;
                label = byPart.Label;
                return true;
            }
        }

        // Exact match on configuration ("7000 - LED / LED") or object name.
        // Priced light packages: object name only — config strings collide with Simeon combos.
        var exactCandidates = itemIsLight && item.catalogPrice > 1.0
            ? new[] { objectKey }
            : new[] { configKey, objectKey };
        foreach (var candidate in exactCandidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            var exact = list.FirstOrDefault(s => NormalizeKey(s.Name) == candidate);
            if (exact != null)
            {
                price = exact.Price;
                label = exact.Label;
                return true;
            }
        }

        // Configuration often looks like "7000 - LED / LED" while Simeon is "LED / LED".
        // Do not apply this to priced 3D light packages (U|ONE / U|002 combos) — those
        // list prices are whole assemblies, not Simeon head-only combos.
        bool allowLightConfigSuffix = !itemIsLight || item.catalogPrice <= 1.0;
        if (!string.IsNullOrEmpty(configKey) && allowLightConfigSuffix)
        {
            var byConfigSuffix = list
                .Where(s =>
                {
                    string sn = NormalizeKey(s.Name);
                    return !string.IsNullOrEmpty(sn)
                           && sn.Length >= 3
                           && configKey.EndsWith(sn, StringComparison.Ordinal);
                })
                .OrderByDescending(s => NormalizeKey(s.Name).Length)
                .FirstOrDefault();
            if (byConfigSuffix != null)
            {
                price = byConfigSuffix.Price;
                label = byConfigSuffix.Label + " (config match)";
                return true;
            }
        }

        // Full boom configs (3D Boom Pricing) only — not individual arms/heads.
        bool isBoomPackage = string.Equals(
            item.sheetName, DataFilePaths.sheetNameBoomCombined, StringComparison.OrdinalIgnoreCase);
        if (isBoomPackage && !string.IsNullOrEmpty(objectKey))
        {
            var tokenHit = list
                .Where(s => s.Price > 0.5)
                .Where(s => IsOndalBaseModel(s.Name))
                .Where(s => BoomBaseModelFits(item.objectName, s.Name))
                .OrderByDescending(s => Tokenize(s.Name).Count)
                .ThenByDescending(s => s.Price)
                .FirstOrDefault();
            if (tokenHit != null)
            {
                price = tokenHit.Price;
                label = tokenHit.Label + " (base model)";
                return true;
            }
        }

        // Install lines: token match against catalog object names when present.
        if (!itemIsLight && !itemIsBoom)
        {
            var installHit = supplier
                .Where(s => s.Family == "Install" && TokensMatch(objectKey, s.Name))
                .OrderByDescending(s => Tokenize(s.Name).Count)
                .FirstOrDefault();
            if (installHit != null)
            {
                price = installHit.Price;
                label = installHit.Label;
                return true;
            }
        }

        return false;
    }

    static bool LooksLikeSapPart(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.StartsWith("11-", StringComparison.OrdinalIgnoreCase)) return false;
        return double.TryParse(id, System.Globalization.NumberStyles.Any,
                   System.Globalization.CultureInfo.InvariantCulture, out double n)
               && n >= 100000;
    }

    static bool LooksLikeArmLengthAddon(string name)
    {
        string n = name.Trim();
        if (n.Length == 0) return false;
        int i = 0;
        while (i < n.Length && char.IsDigit(n[i])) i++;
        if (i == 0) return false;
        string rest = n.Substring(i).TrimStart();
        return rest.StartsWith("mm", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsOndalBaseModel(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string n = name.Trim();
        return n.Equals("Spring", StringComparison.OrdinalIgnoreCase)
               || n.Equals("Spring XL", StringComparison.OrdinalIgnoreCase)
               || n.Equals("Powered", StringComparison.OrdinalIgnoreCase)
               || n.Equals("Powered XL", StringComparison.OrdinalIgnoreCase)
               || n.Equals("Fixed", StringComparison.OrdinalIgnoreCase)
               || n.Equals("Fixed XL", StringComparison.OrdinalIgnoreCase)
               || n.Equals("Fixed XXL", StringComparison.OrdinalIgnoreCase);
    }

    static bool BoomBaseModelFits(string catalogName, string supplierName)
    {
        var catalogTokens = new HashSet<string>(Tokenize(catalogName));
        var supplierTokens = Tokenize(supplierName);
        if (supplierTokens.Count == 0) return false;

        foreach (var t in supplierTokens)
        {
            if (!catalogTokens.Contains(t))
                return false;
        }

        bool catXl = catalogTokens.Contains("xl") || catalogTokens.Contains("xxl");
        bool supXl = supplierTokens.Contains("xl") || supplierTokens.Contains("xxl");
        bool catXxl = catalogTokens.Contains("xxl");
        bool supXxl = supplierTokens.Contains("xxl");
        if (catXxl != supXxl) return false;
        if (catXl != supXl) return false;
        return true;
    }

    static bool TokensMatch(string catalogNormalized, string supplierName)
    {
        var supplierTokens = Tokenize(supplierName);
        if (supplierTokens.Count == 0) return false;
        // Single short tokens like "powered" / "fixed" match too many boom SKUs.
        if (supplierTokens.Count == 1 && supplierTokens[0].Length < 8) return false;
        if (supplierTokens.Sum(t => t.Length) < 4) return false;
        var catalogTokens = new HashSet<string>(Tokenize(catalogNormalized));
        // catalogNormalized may already be alnum-only; also tokenize original-style by splitting digits/words
        if (catalogTokens.Count == 0 && !string.IsNullOrEmpty(catalogNormalized))
            catalogTokens.Add(catalogNormalized);
        foreach (var t in supplierTokens)
        {
            if (!catalogTokens.Contains(t) && !catalogNormalized.Contains(t))
                return false;
        }
        return true;
    }

    static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(s)) return tokens;
        var sb = new StringBuilder();
        foreach (char ch in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    static string NormalizeKey(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }
}
