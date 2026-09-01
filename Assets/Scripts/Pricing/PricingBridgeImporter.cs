using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Loads prices from Quote Request V6 (Simeon / Ondal / Install).
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
                "Expected sheets like “Simeon Lights”, “Ondal Booms”, or “Install Pricelist”.";
            report.Problems.Add(report.Message);
            return report;
        }

        var items = new List<PricingBridgeItem>();
        foreach (var row in supplierPrices)
        {
            if (row == null || row.Price <= 0) continue;
            items.Add(new PricingBridgeItem
            {
                sheetName = row.Family,
                partNumber = row.PartNumber ?? "",
                configurationName = "",
                objectName = (row.Name ?? "").Trim(),
                size = "",
                catalogPrice = row.Price,
                effectivePrice = row.Price,
                source = "Supplier",
                supplierMatch = row.Label,
                missingFromSupplier = false
            });
        }

        if (items.Count == 0)
        {
            report.Message = "Opened the spreadsheet, but every price was zero.";
            report.Problems.Add(report.Message);
            return report;
        }

        report.Ok = true;
        report.Items = items;
        report.CatalogCount = items.Count;
        report.Matched = items.Count;
        report.Missing = 0;
        report.Message = $"Loaded {items.Count} prices.";
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

        if (looksLikeLegacyCatalog && !looksLikeQuoteRequest)
        {
            problems.Add(
                "That file is the old price list. Open Quote Request V6 instead.");
            return prices;
        }

        if (!looksLikeQuoteRequest)
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
        }

        return prices;
    }

    static void ExtractSimeon(SupplierExcelReader.SheetTable sheet, List<SupplierPriceRow> prices)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in sheet.Rows)
        {
            if (row.Length < 6) continue;
            string part = Cell(row, 1);
            string name = Cell(row, 2);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (part != null && part.IndexOf("Part", StringComparison.OrdinalIgnoreCase) >= 0) continue;

            // Base-model table: Imagine List in col 7 (always above Simeon list in col 5).
            // Accessory / BOM tables leave Imagine blank or put COGS in col 7 — use col 5.
            bool hasImagine = SupplierExcelReader.TryParsePrice(Cell(row, 7), out double imagine) && imagine > 0;
            bool hasSimeon = SupplierExcelReader.TryParsePrice(Cell(row, 5), out double simeon) && simeon > 0;
            double price;
            if (hasImagine && hasSimeon && imagine > simeon + 0.5)
                price = imagine;
            else if (hasSimeon)
                price = simeon;
            else if (hasImagine)
                price = imagine;
            else
                continue;

            string key = NormalizeKey(name);
            if (string.IsNullOrEmpty(key) || !seen.Add(key)) continue;

            prices.Add(new SupplierPriceRow
            {
                PartNumber = part,
                Name = name.Trim(),
                Price = price,
                Family = "Light",
                Label = $"Simeon Lights / {part} / {name.Trim()}"
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

    static string Cell(string[] row, int i) =>
        i >= 0 && i < row.Length ? row[i]?.Trim() : null;

    /// <summary>
    /// Price a 3D object from the live V6 list (Simeon / Ondal / Install names).
    /// </summary>
    public static bool TryPrice3D(
        string sheetName,
        string objectName,
        string size,
        IList<PricingBridgeItem> live,
        out double price)
        => TryPrice3D(sheetName, objectName, size, live, out price, out _);

    public static bool TryPrice3D(
        string sheetName,
        string objectName,
        string size,
        IList<PricingBridgeItem> live,
        out double price,
        out PricingBridgeItem hit)
    {
        price = 0;
        hit = null;
        if (live == null || live.Count == 0 || string.IsNullOrWhiteSpace(objectName))
            return false;

        var supplier = new List<SupplierPriceRow>(live.Count);
        foreach (var i in live)
        {
            if (i == null) continue;
            supplier.Add(new SupplierPriceRow
            {
                PartNumber = i.partNumber,
                Name = i.objectName,
                Price = i.effectivePrice,
                Family = i.sheetName,
                Label = i.objectName
            });
        }

        bool itemIsLight = (sheetName ?? "").IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0;
        var query = new PricingBridgeItem
        {
            sheetName = sheetName,
            objectName = objectName.Trim(),
            configurationName = itemIsLight ? SimeonComboFromObjectName(objectName) : "",
            partNumber = "",
            size = size ?? "",
            catalogPrice = 0.5
        };
        if (!TryMatch(query, supplier, out price, out string label))
            return false;

        string name = label ?? "";
        if (name.EndsWith(" (base model)", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - " (base model)".Length);
        else if (name.EndsWith(" (config match)", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - " (config match)".Length);

        foreach (var i in live)
        {
            if (i == null) continue;
            if (string.Equals(i.objectName, name, StringComparison.OrdinalIgnoreCase))
            {
                hit = i;
                break;
            }
        }
        if (hit == null)
        {
            foreach (var i in live)
            {
                if (i != null && Math.Abs(i.effectivePrice - price) < 0.02)
                {
                    hit = i;
                    break;
                }
            }
        }
        return true;
    }

    static string SimeonComboFromObjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string trimmed = name.Trim();
        if (trimmed.IndexOf("LED", StringComparison.OrdinalIgnoreCase) >= 0
            && trimmed.IndexOf('/') >= 0)
            return trimmed;

        var parts = trimmed.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
        {
            string combo = UHeadCombo(trimmed);
            if (!string.IsNullOrEmpty(combo))
                return combo;
            if (LooksLikeLightHead(trimmed))
                return HeadToSimeon(trimmed);
            return "";
        }

        var heads = new List<string>(parts.Length);
        foreach (var part in parts)
            heads.Add(HeadToSimeon(part));
        return string.Join(" / ", heads);
    }

    static string UHeadCombo(string name)
    {
        string key = NormalizeKey(name);
        if (key.IndexOf("u003", StringComparison.Ordinal) >= 0)
            return "LED / LED / LED";
        if (key.IndexOf("u002", StringComparison.Ordinal) >= 0)
            return "LED / LED";
        if (key.IndexOf("uone", StringComparison.Ordinal) >= 0)
            return "LED";
        return null;
    }

    static bool LooksLikeLightHead(string name)
    {
        string n = name ?? "";
        string key = NormalizeKey(n);
        if (key.IndexOf("springarm", StringComparison.Ordinal) >= 0) return false;
        if (key.IndexOf("horizontalarm", StringComparison.Ordinal) >= 0) return false;
        if (key.IndexOf("ceilingtube", StringComparison.Ordinal) >= 0) return false;
        if (key.IndexOf("ceilingcover", StringComparison.Ordinal) >= 0) return false;
        if (key.IndexOf("ceilingtube", StringComparison.Ordinal) >= 0) return false;
        if (key.IndexOf("mount", StringComparison.Ordinal) >= 0) return false;
        if (n.IndexOf("U |", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("LED", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("flat panel", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("lead shield", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (key.StartsWith("7000") || key.StartsWith("8000") || key.StartsWith("500")) return true;
        if (key == "fp" || key == "ls" || key == "led") return true;
        return false;
    }

    static string HeadToSimeon(string name)
    {
        string n = name ?? "";
        if (n.IndexOf("lead", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0
            || System.Text.RegularExpressions.Regex.IsMatch(n, @"\bLS\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return "LS";
        if (n.IndexOf("FP", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("flat", StringComparison.OrdinalIgnoreCase) >= 0)
            return "FP";
        return "LED";
    }

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
        string strippedKey = NormalizeKey(StripFamilyPrefix(item.objectName));
        string aliasKey = NormalizeKey(BoomLightAlias(item.objectName, item.size));
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
            ? new[] { objectKey, strippedKey, aliasKey }
            : new[] { objectKey, strippedKey, aliasKey, configKey };
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

        if (itemIsBoom && !string.IsNullOrEmpty(objectKey))
        {
            var tokenHit = list
                .Where(s => s.Price > 0.5)
                .Where(s => IsOndalBaseModel(s.Name))
                .Where(s => BoomBaseModelFits(item.objectName, s.Name)
                             || BoomBaseModelFits(StripFamilyPrefix(item.objectName), s.Name))
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

        if (TryLooseNameMatch(list, item.objectName, out price, out label))
            return true;

        // Install / ship catalog rows live on the light sheet, so they would otherwise
        // only search Simeon names. Also match leftover non-light/boom sheets.
        if (LooksLikeInstallOrShipping(item) || (!itemIsLight && !itemIsBoom))
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

    static bool TryLooseNameMatch(
        List<SupplierPriceRow> list, string objectName, out double price, out string label)
    {
        price = 0;
        label = null;
        var catalog = MeaningfulTokens(objectName);
        if (catalog.Count < 2) return false;

        var catalogSet = new HashSet<string>(catalog);
        SupplierPriceRow best = null;
        int bestCount = 0;
        int ties = 0;

        foreach (var row in list)
        {
            if (row == null || row.Price <= 0.5) continue;
            var v6 = MeaningfulTokens(row.Name);
            if (v6.Count < 2) continue;
            bool subset = true;
            foreach (var t in v6)
            {
                if (!catalogSet.Contains(t))
                {
                    subset = false;
                    break;
                }
            }
            if (!subset) continue;
            if (v6.Count > bestCount)
            {
                best = row;
                bestCount = v6.Count;
                ties = 1;
            }
            else if (v6.Count == bestCount)
            {
                ties++;
            }
        }

        if (best == null || ties != 1) return false;
        price = best.Price;
        label = best.Label;
        return true;
    }

    static List<string> MeaningfulTokens(string s)
    {
        var tokens = Tokenize(s);
        var result = new List<string>();
        foreach (var raw in tokens)
        {
            string w = FoldToken(raw);
            if (w.Length < 2) continue;
            if (w == "boom" || w == "light" || w == "lights" || w == "the"
                || w == "for" || w == "with" || w == "and")
                continue;
            result.Add(w);
        }
        return result;
    }

    static string FoldToken(string w)
    {
        if (w == "duplexes") return "duplex";
        if (w == "outlets") return "outlet";
        if (w == "shelves") return "shelf";
        if (w == "racks") return "rack";
        if (w.Length > 3 && w.EndsWith("s") && !w.EndsWith("ss"))
            return w.Substring(0, w.Length - 1);
        return w;
    }

    /// <summary>
    /// Console dump of room parts vs the spreadsheet. Filter the Console with [Prices].
    /// </summary>
    public static void LogRoomMatches(
        IList<PricingBridgeItem> live,
        IEnumerable<(string sheet, string name, string size)> parts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Prices] Room parts vs spreadsheet:");
        int match = 0, miss = 0, skip = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part.name)) continue;
            string key = $"{part.sheet}|{part.name}|{part.size}";
            if (!seen.Add(key)) continue;

            if (IsPackageSkip(part.name, out string skipReason))
            {
                skip++;
                sb.AppendLine($"  skip   {part.name}  ({skipReason})");
                continue;
            }

            if (TryPrice3D(part.sheet, part.name, part.size, live, out double price, out var hit))
            {
                match++;
                string row = hit != null ? hit.objectName : "?";
                sb.AppendLine($"  match  {part.name}  →  {row}  ${price:F2}");
            }
            else
            {
                miss++;
                string hint = ClosestSpreadsheetRows(part.sheet, part.name, live);
                sb.AppendLine($"  miss   {part.name}  closest: {hint}");
            }
        }

        sb.AppendLine($"[Prices] {match} matched, {miss} not on spreadsheet, {skip} skipped.");
        Debug.Log(sb.ToString());
    }

    static string ClosestSpreadsheetRows(string sheetName, string objectName, IList<PricingBridgeItem> live)
    {
        bool lights = (sheetName ?? "").IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0;
        bool booms = (sheetName ?? "").IndexOf("Boom", StringComparison.OrdinalIgnoreCase) >= 0;
        var catalog = new HashSet<string>(MeaningfulTokens(objectName));
        if (catalog.Count == 0 || live == null) return "(none)";

        var scored = new List<(int score, string name)>();
        foreach (var i in live)
        {
            if (i == null) continue;
            if (lights && i.sheetName != "Light") continue;
            if (booms && i.sheetName != "Boom") continue;
            int score = 0;
            foreach (var t in MeaningfulTokens(i.objectName))
                if (catalog.Contains(t)) score++;
            if (score <= 0) continue;
            scored.Add((score, i.objectName));
        }

        scored.Sort((a, b) => b.score.CompareTo(a.score));
        if (scored.Count == 0) return "(no similar row)";
        int take = Math.Min(3, scored.Count);
        var bits = new List<string>(take);
        for (int i = 0; i < take; i++)
            bits.Add($"{scored[i].name} ({scored[i].score} tokens)");
        return string.Join("; ", bits);
    }

    static bool LooksLikeInstallOrShipping(PricingBridgeItem item)
    {
        string part = item?.partNumber ?? "";
        if (part.StartsWith("117-", StringComparison.OrdinalIgnoreCase))
            return true;
        string n = (item?.objectName ?? "") + " " + (item?.configurationName ?? "");
        return n.IndexOf("install", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("shipping", StringComparison.OrdinalIgnoreCase) >= 0;
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

    static string StripFamilyPrefix(string name)
    {
        string n = (name ?? "").Trim();
        if (n.StartsWith("Boom -", StringComparison.OrdinalIgnoreCase))
            n = n.Substring(6).Trim();
        else if (n.StartsWith("Lights -", StringComparison.OrdinalIgnoreCase))
            n = n.Substring(8).Trim();
        return n;
    }

    static string BoomLightAlias(string name, string size = null)
    {
        string n = StripFamilyPrefix(name).ToLowerInvariant();
        string sz = (size ?? "").ToLowerInvariant();
        if (n.IndexOf("tandem", StringComparison.Ordinal) >= 0
            && (n.IndexOf("cover", StringComparison.Ordinal) >= 0
                || n.IndexOf("mount", StringComparison.Ordinal) >= 0))
            return "Tandem (1st Position)";
        if (n.IndexOf("single", StringComparison.Ordinal) >= 0
            && n.IndexOf("square", StringComparison.Ordinal) >= 0)
            return "Single Mount (Square)";
        if (n.IndexOf("single", StringComparison.Ordinal) >= 0
            && (n.IndexOf("round", StringComparison.Ordinal) >= 0
                || n.IndexOf("circle", StringComparison.Ordinal) >= 0))
            return "Single Mount (Round)";
        if (n.IndexOf("single", StringComparison.Ordinal) >= 0
            && n.IndexOf("ceiling cover", StringComparison.Ordinal) >= 0
            && n.IndexOf("standard", StringComparison.Ordinal) >= 0)
            return "Single Mount (Standard)";
        if (n.IndexOf("nitrogen", StringComparison.Ordinal) >= 0
            && n.IndexOf("regulator", StringComparison.Ordinal) >= 0)
            return "Nitrogen Regulator";

        if (n.IndexOf("flange", StringComparison.Ordinal) >= 0)
        {
            bool xl = n.IndexOf("xl", StringComparison.Ordinal) >= 0;
            bool fiveHundred = n.IndexOf("500", StringComparison.Ordinal) >= 0
                               || sz.IndexOf("500", StringComparison.Ordinal) >= 0;
            if (xl)
                return fiveHundred
                    ? "Powered XL - 500mm XL Ceiling Flange"
                    : "Powered XL - 300mm XL Ceiling Flange";
            return fiveHundred
                ? "Powered - 500mm Ceiling Flange"
                : "Powered - 300mm Ceiling Flange";
        }

        var duplex = System.Text.RegularExpressions.Regex.Match(n, @"(\d+)\s*duplex");
        if (duplex.Success)
            return duplex.Groups[1].Value + " Duplexes";

        var gases = System.Text.RegularExpressions.Regex.Match(n, @"medical gases?\s*\((\d+)");
        if (gases.Success)
            return gases.Groups[1].Value + " Outlets";

        if (n.IndexOf("shelf", StringComparison.Ordinal) >= 0)
        {
            if (n.IndexOf("500", StringComparison.Ordinal) >= 0) return "Shelf (500mm)";
            if (n.IndexOf("750", StringComparison.Ordinal) >= 0) return "Shelf (750mm)";
        }

        if (n.IndexOf("rail", StringComparison.Ordinal) >= 0)
        {
            if (n.IndexOf("1000", StringComparison.Ordinal) >= 0) return "Multi-Function Racks (1000mm)";
            if (n.IndexOf("600", StringComparison.Ordinal) >= 0) return "Multi-Function Racks (600mm)";
        }

        if (n.IndexOf("flat panel", StringComparison.Ordinal) >= 0
            || n.IndexOf("flatpanel", StringComparison.Ordinal) >= 0)
            return "32in Flat Panel";

        if (n.IndexOf("spring", StringComparison.Ordinal) >= 0
            && n.IndexOf("arm", StringComparison.Ordinal) >= 0
            && n.IndexOf("low", StringComparison.Ordinal) >= 0)
            return "Spring arm 9-pole low ceiling";

        if (n.IndexOf("spring", StringComparison.Ordinal) >= 0
            && n.IndexOf("arm", StringComparison.Ordinal) >= 0)
            return "Spring arm 9-pole 10-20kg";

        if (n.IndexOf("ceiling tube", StringComparison.Ordinal) >= 0)
            return "300 mm (Ø18)";

        if (n.IndexOf("horizontal arm", StringComparison.Ordinal) >= 0)
            return "Central axis double - 9-pole / 9-pole";

        return null;
    }

    /// <summary>
    /// Parts that are not 1:1 spreadsheet rows (bundled in the boom, or counted as a package).
    /// </summary>
    public static bool IsPackageSkip(string name, out string reason)
    {
        reason = null;
        string n = StripFamilyPrefix(name ?? "").ToLowerInvariant();
        if (n.IndexOf("blank plate", StringComparison.Ordinal) >= 0
            || n.IndexOf("blank preparation", StringComparison.Ordinal) >= 0
            || n.IndexOf("data plate", StringComparison.Ordinal) >= 0
            || n.IndexOf("data pass", StringComparison.Ordinal) >= 0)
        {
            reason = "not quoted";
            return true;
        }
        if (n.IndexOf("gas outlet", StringComparison.Ordinal) >= 0)
        {
            reason = "quoted as outlet count";
            return true;
        }
        if (n.IndexOf("column tube", StringComparison.Ordinal) >= 0
            || n.IndexOf("drop tube", StringComparison.Ordinal) >= 0)
        {
            reason = "part of boom";
            return true;
        }
        if (n.IndexOf("service head", StringComparison.Ordinal) >= 0
            && n.IndexOf("light", StringComparison.Ordinal) < 0
            && n.IndexOf("module", StringComparison.Ordinal) < 0)
        {
            reason = "part of boom";
            return true;
        }
        if (n.IndexOf("voltage module", StringComparison.Ordinal) >= 0
            || n.IndexOf("hv module", StringComparison.Ordinal) >= 0
            || n.IndexOf("lv module", StringComparison.Ordinal) >= 0)
        {
            reason = "quoted as duplex/data plates";
            return true;
        }
        return false;
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
