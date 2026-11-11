using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Shared sales-proposal pricing: light combo sheet lookups + boom bundled config sheet.
/// Used by ProposalPDFGenerator and ProposalPreviewModel so totals stay aligned.
/// </summary>
public static class ProposalPricingResolver
{
    public sealed class Line
    {
        public string PartNumber;
        public string Description;
        public int Qty = 1;
        public double UnitPrice;
        public double ExtPrice;
        public bool IsLight;
        public bool IsBoom;
    }

    public static List<Line> ResolveEquipmentLines(IEnumerable<SelectablePrice> allPrices)
    {
        var lines = new List<Line>();
        if (allPrices == null)
            return lines;

        var priced = allPrices
            .Where(sp => sp != null && sp.objectPricingData != null && sp.gameObject.activeInHierarchy)
            .ToList();
        foreach (var group in priced.GroupBy(sp => sp.transform.root))
        {
            var lights = group.Where(sp => !sp.isBoomObject).ToList();
            var booms = group.Where(sp => sp.isBoomObject).ToList();
            var boomRoot = group.Key != null ? group.Key.gameObject : null;

            var lightLines = ResolveLightLines(lights);
            lines.AddRange(lightLines);
            lines.AddRange(ResolveBoomLines(boomRoot, booms));
            // Extras (shelves, duplexes, gas, covers, …) sit on top of the bundled boom package —
            // same stack Carlyn's Estimating Form puts in Configuration List Price.
            lines.AddRange(ResolveBoomExtraLines(boomRoot, booms));
        }

        return lines;
    }

    public static double SumEquipment(IEnumerable<SelectablePrice> allPrices)
        => ResolveEquipmentLines(allPrices).Sum(l => l.ExtPrice);

    public static double SumPricedOptions()
    {
        return DropdownPopulator.GetAllCurrentStates()
            .Where(s => s.Item2 != null
                        && !IsNone(s.Item2.ObjectName)
                        && s.Item2.ListPrice > 0)
            .Sum(s => s.Item2.ListPrice);
    }

    /// <summary>
    /// Quote price from the sheet's cost, margin, and tariff. Nothing is assumed
    /// when the margin cell was not on the sheet.
    /// </summary>
    public static double SellPrice(double cost, double margin, double tariff)
    {
        if (cost <= 0 || margin <= 0 || margin >= 1)
            return 0;
        if (tariff < 0)
            tariff = 0;
        return cost * (1 + tariff) / (1 - margin);
    }

    public static bool TryShippingCharge(int configurations, out double sell)
    {
        sell = 0;
        var state = PricingBridgeStore.State;
        if (state == null)
            return false;
        if (state.shippingPerConfig <= 0 && state.installationPerConfig <= 0)
            return false;
        if (state.equipmentMargin <= 0 || state.equipmentMargin >= 1)
            return false;

        int count = configurations < 1 ? 1 : configurations;
        double cost = (state.shippingPerConfig + state.installationPerConfig) * count;
        sell = cost / (1 - state.equipmentMargin);
        return sell > 0;
    }

    public static double SumInstallAndShip(int configurations = 1)
    {
        if (TryShippingCharge(configurations, out double sell))
            return sell;

        var reader = UnityEngine.Object.FindAnyObjectByType<ExcelReader>();
        if (reader == null)
            return 0;
        double install = reader.GetInstallationLightsCharges()?.ListPrice ?? 0;
        double ship = reader.GetShippingLightsCharges()?.ListPrice ?? 0;
        return install + ship;
    }

    /// <summary>
    /// Config list total matching Estimating Form style: equipment + priced quote options
    /// (no install/shipping).
    /// </summary>
    public static double SumConfigListPrice(IEnumerable<SelectablePrice> allPrices)
        => SumEquipment(allPrices) + SumPricedOptions();

    public static double CalculateGrandEquipmentTotal(IEnumerable<SelectablePrice> allPrices)
        => SumConfigListPrice(allPrices) + SumInstallAndShip(ConfigurationCount(allPrices));

    /// <summary>
    /// One boom is one configuration. The light mounted with it is part of that
    /// same configuration, so it does not add another shipping charge.
    /// </summary>
    public static int ConfigurationCount(IEnumerable<SelectablePrice> allPrices)
    {
        if (allPrices == null)
            return 1;
        var active = allPrices
            .Where(sp => sp != null && sp.gameObject != null && sp.gameObject.activeInHierarchy)
            .ToList();
        int configs = active
            .Where(sp => sp.isBoomObject)
            .Select(sp => sp.transform.root)
            .Distinct()
            .Count();
        if (configs < 1)
            configs = active.Select(sp => sp.transform.root).Distinct().Count();
        return configs < 1 ? 1 : configs;
    }

    static bool IsNone(string name)
        => string.IsNullOrWhiteSpace(name)
           || name.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One line per sheet row the placed light actually matches.
    /// A part with no row is left off the total.
    /// </summary>
    public static List<Line> ResolveLightLines(List<SelectablePrice> lights)
    {
        if (lights == null || lights.Count == 0)
            return new List<Line>();
        return LinesFromLightSheet(lights);
    }

    /// <summary>
    /// One bundled boom package per arm stack. A tandem with mixed arms
    /// (Spring + Powered XL) must emit two lines, not one combined fingerprint.
    /// </summary>
    public static List<Line> ResolveBoomLines(GameObject boomRoot, List<SelectablePrice> boomParts)
    {
        var lines = new List<Line>();
        foreach (var (root, parts) in SplitBoomAssemblies(boomRoot, boomParts))
        {
            var line = ResolveBoomLine(root, parts);
            if (line != null)
                lines.Add(line);
        }
        return lines;
    }

    public static Line ResolveBoomLine(GameObject boomRoot, List<SelectablePrice> boomParts)
    {
        if (boomParts == null || boomParts.Count == 0)
            return null;

        var names = PartNames(boomParts);
        string model = BoomModelName(names);
        var sheet = FindSheetRow("Boom", model);
        if (sheet == null || sheet.ListPrice <= 0)
            return null;

        return new Line
        {
            PartNumber = string.IsNullOrEmpty(sheet.PartNumber) ? "N/A" : sheet.PartNumber,
            Description = string.IsNullOrEmpty(sheet.ObjectName) ? model : sheet.ObjectName,
            Qty = 1,
            UnitPrice = sheet.ListPrice,
            ExtPrice = sheet.ListPrice,
            IsBoom = true
        };
    }

    public static string BoomModelName(IEnumerable<string> partNames)
    {
        switch (DetectBoomArmFamily(partNames))
        {
            case BoomArmFamily.PoweredXl: return "Powered XL";
            case BoomArmFamily.Powered: return "Powered";
            case BoomArmFamily.SpringXl: return "Spring XL";
            case BoomArmFamily.Spring: return "Spring";
            case BoomArmFamily.FixedXxl: return "Fixed XXL";
            case BoomArmFamily.FixedXl: return "Fixed XL";
            case BoomArmFamily.Fixed: return "Fixed";
            default: return null;
        }
    }

    static List<string> PartNames(IEnumerable<SelectablePrice> parts)
    {
        var names = new List<string>();
        if (parts == null)
            return names;
        foreach (var sp in parts)
        {
            if (sp == null) continue;
            if (!string.IsNullOrWhiteSpace(sp.pricingObjectName)) names.Add(sp.pricingObjectName);
            if (!string.IsNullOrWhiteSpace(sp.UIObjectName)) names.Add(sp.UIObjectName);
            var sel = sp.GetComponent<Selectable>();
            if (sel != null && !string.IsNullOrWhiteSpace(sel.UIButtonName))
                names.Add(sel.UIButtonName);
        }
        return names;
    }

    public static PriceExcelData FindSheetRow(string family, string objectName)
    {
        if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(objectName))
            return null;

        PricingBridgeStore.EnsureBundledPriceList();
        var items = PricingBridgeStore.State?.items;
        if (items == null)
            return null;

        foreach (var item in items)
        {
            if (item == null || item.effectivePrice <= 0)
                continue;
            if (!string.Equals(item.sheetName, family, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals((item.objectName ?? "").Trim(), objectName.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            return new PriceExcelData
            {
                PartNumber = item.partNumber ?? "",
                ObjectName = item.objectName ?? "",
                ObjectSize = item.size,
                ListPrice = item.effectivePrice
            };
        }
        return null;
    }

    static List<Line> LinesFromLightSheet(List<SelectablePrice> lights)
    {
        var lines = new List<Line>();
        var blobs = new List<string>();
        var headNames = new List<string>();
        foreach (var sp in lights)
        {
            if (sp == null) continue;
            headNames.Add(HeadLabel(sp));
            var sel = sp.GetComponent<Selectable>();
            var blob = new StringBuilder();
            blob.Append(sp.pricingObjectName).Append(' ');
            blob.Append(sp.UIObjectName).Append(' ');
            blob.Append(sp.gameObject != null ? sp.gameObject.name : "").Append(' ');
            if (sel != null)
                blob.Append(sel.UIButtonName);
            if (sp.transform != null)
            {
                foreach (var child in sp.GetComponentsInChildren<Transform>(true))
                {
                    if (child != null && child != sp.transform)
                        blob.Append(' ').Append(child.name);
                }
            }
            blobs.Add(blob.ToString());
        }

        string model = HeadModelName(headNames);
        var modelLine = SheetLine("Light", model, true);
        if (modelLine != null)
            lines.Add(modelLine);

        var arms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var blob in blobs)
        {
            string arm = ArmSelectionName(blob);
            if (!string.IsNullOrEmpty(arm))
                arms.Add(arm);
        }
        foreach (var arm in arms)
        {
            var armLine = SheetLine("Light", arm, true);
            if (armLine != null)
                lines.Add(armLine);
        }
        return lines;
    }

    static Line SheetLine(string family, string name, bool isLight)
    {
        var row = FindSheetRow(family, name);
        if (row == null || row.ListPrice <= 0)
            return null;
        return new Line
        {
            PartNumber = string.IsNullOrEmpty(row.PartNumber) ? "N/A" : row.PartNumber,
            Description = string.IsNullOrEmpty(row.ObjectName) ? name : row.ObjectName,
            Qty = 1,
            UnitPrice = row.ListPrice,
            ExtPrice = row.ListPrice,
            IsLight = isLight,
            IsBoom = !isLight
        };
    }

    /// <summary>
    /// The catalog button is what was placed. The saved price name is often just
    /// "Horizontal Arm", which is not a row on the light sheet.
    /// </summary>
    static string HeadLabel(SelectablePrice sp)
    {
        if (sp == null)
            return null;
        var sel = sp.GetComponent<Selectable>();
        string fallback = null;
        foreach (var raw in new[]
        {
            sp.UIObjectName,
            sel != null ? sel.UIButtonName : null,
            sp.pricingObjectName,
            sp.gameObject != null ? sp.gameObject.name : null
        })
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (fallback == null)
                fallback = raw;
            if (HeadModelName(new[] { raw }) != null)
                return raw;
        }
        return fallback;
    }

    public static string HeadModelName(IEnumerable<string> partNames)
    {
        if (partNames == null)
            return null;
        int led = 0, fp = 0, ls = 0;
        foreach (var raw in partNames)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            string n = raw.ToLowerInvariant();
            if (n.Contains("flat panel") || n.Contains("flatpanel"))
                fp++;
            else if (n.Contains("lead shield") || n.Contains("leadshield"))
                ls++;
            else if (n.Contains("8000") || n.Contains("7000") || n.Contains("simeon")
                     || n.Contains("u|") || n.Contains("u |") || n.Contains("led"))
                led++;
        }

        var bits = new List<string>();
        for (int i = 0; i < led; i++) bits.Add("LED");
        for (int i = 0; i < fp; i++) bits.Add("FP");
        for (int i = 0; i < ls; i++) bits.Add("LS");
        return bits.Count == 0 ? null : string.Join(" / ", bits);
    }

    public static string ArmSelectionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string n = name.ToLowerInvariant();
        bool low = n.Contains("low ceiling") || n.Contains("lowceiling") || n.Contains("_lc") || n.Contains(" lc");
        bool three = n.Contains("3p") || n.Contains("3-pole");
        string series = null;
        if (n.Contains("8000")) series = "8000";
        else if (n.Contains("7000")) series = "7000";
        else if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\b500\b")) series = "500";
        if (series != null)
        {
            string kind = low ? "Low Ceiling" : "Standard";
            if (three) kind += " (3P)";
            return series + " " + kind;
        }
        if (n.Contains("flat panel") || n.Contains("flatpanel"))
            return "32in Flat Panel";
        if (n.Contains("lead shield") || n.Contains("leadshield"))
            return "Lead Shield";
        return null;
    }

    /// <summary>
    /// Priced boom add-ons that Estimating Form includes in Configuration List Price
    /// but that are NOT inside the bundled boom fingerprint row.
    /// Electrical / med-gas are aggregated by outlet count (Imagine List package),
    /// not multiplied as per-kit SelectablePrice rows. Shelves use Estimating package rows.
    /// </summary>
    public static List<Line> ResolveBoomExtraLines(GameObject boomRoot, List<SelectablePrice> boomParts)
    {
        var lines = new List<Line>();
        if (boomParts == null || boomParts.Count == 0)
            return lines;

        var extras = boomParts
            .Where(sp => sp != null && sp.objectPricingData != null && IsBoomExtraPart(sp))
            .ToList();

        // Estimating: duplexes / gas priced once from total outlet count on the boom.
        var electricalLine = ResolveOutletPackageLine(boomRoot, extras, electrical: true);
        if (electricalLine != null)
            lines.Add(electricalLine);
        var gasLine = ResolveOutletPackageLine(boomRoot, extras, electrical: false);
        if (gasLine != null)
            lines.Add(gasLine);

        // Shelves: Estimating uses (N) 500mm / 750mm package rows, not per-SKU kit spam.
        lines.AddRange(ResolveShelfPackageLines(extras));

        // Flanges follow the boom arm and the size that was placed — not every
        // flange is a Powered boom SKU.
        lines.AddRange(ResolveFlangePackageLines(boomParts, extras));

        var remaining = extras
            .Where(sp => !IsElectricalExtra(sp) && !IsMedGasExtra(sp) && !IsShelfExtra(sp)
                         && !IsFlangeExtra(sp)
                         && !IsRailExtra(sp) && !IsUnpricedEstimatingSkip(sp))
            .GroupBy(BoomExtraGroupKey)
            .ToList();

        foreach (var g in remaining)
        {
            var sample = g.First();
            string alias = PricingBridgeImporter.SheetRowAlias(
                sample.pricingObjectName, sample.objectPricingData.ObjectSize);
            var sheetRow = FindSheetRow("Boom", alias)
                ?? FindSheetRow("Boom", sample.objectPricingData.ObjectName)
                ?? FindSheetRow("Boom", sample.pricingObjectName);
            if (sheetRow == null || sheetRow.ListPrice <= 0)
                continue;
            double unit = sheetRow.ListPrice;

            int qty = g.Count();
            string desc = sample.objectPricingData.ObjectName
                          ?? sample.pricingObjectName
                          ?? sample.UIObjectName
                          ?? "Boom option";
            desc = ProposalListing.CleanExtra(desc, sample.objectPricingData.ObjectSize, qty);

            lines.Add(new Line
            {
                PartNumber = string.IsNullOrEmpty(sample.objectPricingData.PartNumber)
                    ? "N/A"
                    : sample.objectPricingData.PartNumber,
                Description = desc,
                Qty = qty,
                UnitPrice = unit,
                ExtPrice = unit * qty,
                IsBoom = true
            });
        }

        return lines;
    }

    static bool LooksLikeCeilingFlange(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        string n = raw.Trim().ToLowerInvariant();
        return n.Contains("ceiling flange");
    }

    /// <summary>
    /// Tandem cover is one scene root with two flange stacks. Price each stack
    /// on its own subtree so Spring vs Powered XL don't collapse into one package.
    /// </summary>
    static List<(GameObject root, List<SelectablePrice> parts)> SplitBoomAssemblies(
        GameObject boomRoot, List<SelectablePrice> boomParts)
    {
        var single = new List<(GameObject root, List<SelectablePrice> parts)>();
        if (boomParts == null || boomParts.Count == 0)
            return single;

        if (boomRoot == null)
        {
            single.Add((null, boomParts));
            return single;
        }

        var flanges = new List<Selectable>();
        foreach (var sel in boomRoot.GetComponentsInChildren<Selectable>(true))
        {
            if (sel == null)
                continue;
            if (LooksLikeCeilingFlange(sel.UIButtonName))
                flanges.Add(sel);
        }

        if (flanges.Count < 2)
        {
            single.Add((boomRoot, boomParts));
            return single;
        }

        var split = new List<(GameObject root, List<SelectablePrice> parts)>();
        foreach (var flange in flanges)
        {
            var parts = boomParts.Where(sp =>
                sp != null
                && (sp.transform == flange.transform || sp.transform.IsChildOf(flange.transform)))
                .ToList();
            if (parts.Count == 0)
                continue;
            split.Add((flange.gameObject, parts));
        }

        return split.Count > 0 ? split : new List<(GameObject, List<SelectablePrice>)> { (boomRoot, boomParts) };
    }

    static Line ResolveOutletPackageLine(
        GameObject boomRoot,
        List<SelectablePrice> extras,
        bool electrical)
    {
        var matches = extras.Where(sp => electrical ? IsElectricalExtra(sp) : IsMedGasExtra(sp)).ToList();
        int count = electrical
            ? CountDuplexOutletsOnBoom(boomRoot, matches)
            : CountMedGasOutletsOnBoom(boomRoot, matches);

        // Scene may have outlets even when SelectablePrice rows were never attached.
        if (count <= 0 && matches.Count == 0)
            return null;
        if (count <= 0)
            count = matches.Count;
        if (count <= 0)
            return null;

        string packageName = electrical ? $"{count} Duplexes" : $"{count} Outlets";
        PriceExcelData packaged = FindSheetRow("Boom", packageName);

        double unitEach = LookupUnitOutletRate(electrical);
        double ext;
        string part = "N/A";
        string desc;
        if (packaged != null && packaged.ListPrice > 0)
        {
            ext = packaged.ListPrice;
            part = string.IsNullOrEmpty(packaged.PartNumber) ? "N/A" : packaged.PartNumber;
            desc = string.IsNullOrEmpty(packaged.ObjectName) ? packageName : packaged.ObjectName;
        }
        else
        {
            ext = unitEach * count;
            desc = packageName;
        }

        if (ext <= 0)
            return null;

        return new Line
        {
            PartNumber = part,
            Description = desc,
            Qty = 1,
            UnitPrice = ext,
            ExtPrice = ext,
            IsBoom = true
        };
    }

    static IEnumerable<Line> ResolveShelfPackageLines(List<SelectablePrice> extras)
    {
        var shelves = extras.Where(IsShelfExtra).ToList();
        if (shelves.Count == 0)
            yield break;

        int count500 = shelves.Count(IsShelf500);
        int count750 = shelves.Count(sp => !IsShelf500(sp));

        if (count500 > 0)
        {
            var line = LookupShelfPackage(500, count500);
            if (line != null)
                yield return line;
        }

        if (count750 > 0)
        {
            var line = LookupShelfPackage(750, count750);
            if (line != null)
                yield return line;
        }
    }

    static string ShelfLabel(string objectName, int mm, int count)
    {
        string label = string.IsNullOrWhiteSpace(objectName)
            ? $"{mm}mm Shelves"
            : objectName.Trim();
        return ProposalListing.CleanExtra(label, null, count);
    }

    static Line LookupShelfPackage(int mm, int count)
    {
        // Exact package names only — a loose match would grab "Shelf (500mm)"
        // and charge one shelf while the label says two.
        string[] keys =
        {
            $"({count}) {mm}mm Shelfs",
            $"({count}) {mm}mm Shelves",
            $"({count}) {mm}mm Shelf",
        };

        PriceExcelData packaged = null;
        foreach (var key in keys)
        {
            packaged = FindExactBoomPrice(key);
            if (packaged != null && packaged.ListPrice > 0)
                break;
        }

        if (packaged == null || packaged.ListPrice <= 0)
            return null;
        double ext = packaged.ListPrice;

        string label = packaged != null && !string.IsNullOrEmpty(packaged.ObjectName)
            ? packaged.ObjectName
            : $"({count}) {mm}mm Shelves";

        return new Line
        {
            PartNumber = packaged != null && !string.IsNullOrEmpty(packaged.PartNumber)
                ? packaged.PartNumber
                : "N/A",
            Description = ShelfLabel(label, mm, count),
            Qty = 1,
            UnitPrice = ext,
            ExtPrice = ext,
            IsBoom = true
        };
    }

    /// <summary>
    /// Exact name only. Do not use the fuzzy sheet match — package rows must
    /// not collapse onto a single-piece price.
    /// </summary>
    public static PriceExcelData FindExactBoomPrice(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        PricingBridgeStore.EnsureBundledPriceList();
        var items = PricingBridgeStore.State?.items;
        if (items == null)
            return null;

        foreach (var item in items)
        {
            if (item == null)
                continue;
            if (!string.Equals(item.sheetName, "Boom", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals((item.objectName ?? "").Trim(), objectName.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (item.effectivePrice <= 0)
                continue;
            return new PriceExcelData
            {
                PartNumber = item.partNumber ?? "",
                ObjectName = item.objectName ?? "",
                ObjectSize = item.size,
                ListPrice = item.effectivePrice
            };
        }

        return null;
    }

    /// <summary>How much N shelves of this size should cost.</summary>
    public static double ShelfPackagePrice(int mm, int count)
    {
        if (count <= 0)
            return 0;
        var row = FindExactBoomPrice($"({count}) {mm}mm Shelves")
                  ?? FindExactBoomPrice($"({count}) {mm}mm Shelfs")
                  ?? FindExactBoomPrice($"({count}) {mm}mm Shelf");
        if (row != null && row.ListPrice > 0)
            return row.ListPrice;
        return 0;
    }

    static double LookupUnitOutletRate(bool electrical)
    {
        return 0;
    }

    static bool IsElectricalExtra(SelectablePrice sp)
    {
        string blob = ExtraBlob(sp);
        return blob.Contains("duplex") || blob.Contains("electrical");
    }

    static bool IsMedGasExtra(SelectablePrice sp)
    {
        string blob = ExtraBlob(sp);
        return blob.Contains("medical gas") || blob.Contains("gas outlet") || blob.Contains("med-gas")
               || blob.Contains("medical gases");
    }

    static bool IsShelfExtra(SelectablePrice sp)
    {
        string blob = ExtraBlob(sp);
        return blob.Contains("shelf") && !blob.Contains("drawer") && !blob.Contains("handle");
    }

    static bool IsFlangeExtra(SelectablePrice sp)
    {
        return sp != null && LooksLikeCeilingFlange(ExtraBlob(sp));
    }

    public enum BoomArmFamily
    {
        Unknown,
        Spring,
        Fixed,
        Powered,
        SpringXl,
        FixedXl,
        PoweredXl,
        FixedXxl
    }

    /// <summary>
    /// Which arm family this boom uses. Powered flanges only apply on powered arms.
    /// </summary>
    public static BoomArmFamily DetectBoomArmFamily(IEnumerable<string> partNames)
    {
        if (partNames == null)
            return BoomArmFamily.Unknown;

        bool powered = false, spring = false, fixedArm = false, xl = false, xxl = false;
        foreach (var raw in partNames)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            string n = raw.ToLowerInvariant();
            if (n.IndexOf("powered", StringComparison.Ordinal) >= 0)
                powered = true;
            if (n.IndexOf("spring", StringComparison.Ordinal) >= 0)
                spring = true;
            if (n.IndexOf("fixed", StringComparison.Ordinal) >= 0)
                fixedArm = true;
            if (n.IndexOf("xxl", StringComparison.Ordinal) >= 0)
                xxl = true;
            else if (n.IndexOf("xl", StringComparison.Ordinal) >= 0)
                xl = true;
        }

        if (powered)
            return xl || xxl ? BoomArmFamily.PoweredXl : BoomArmFamily.Powered;
        if (spring)
            return xl ? BoomArmFamily.SpringXl : BoomArmFamily.Spring;
        if (fixedArm)
        {
            if (xxl) return BoomArmFamily.FixedXxl;
            return xl ? BoomArmFamily.FixedXl : BoomArmFamily.Fixed;
        }
        return BoomArmFamily.Unknown;
    }

    public static int FlangeSizeMm(string size, string name)
    {
        foreach (var raw in new[] { size, name })
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var match = System.Text.RegularExpressions.Regex.Match(
                raw, @"(\d+)\s*mm", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int mm))
                return mm;
        }
        return 100;
    }

    public sealed class FlangeQuote
    {
        public string Description;
        public string LookupName;
        public double FallbackPrice;
    }

    /// <summary>
    /// 100mm flanges are free. Only powered arms use the Powered flange SKUs
    /// for 300mm / 500mm. Spring and fixed use the small flange adder.
    /// </summary>
    public static FlangeQuote QuoteFlange(BoomArmFamily family, int sizeMm, bool flangeIsXl)
    {
        int mm = sizeMm <= 0 ? 100 : sizeMm;
        bool powered = family == BoomArmFamily.Powered || family == BoomArmFamily.PoweredXl;
        bool useXlSku = flangeIsXl || family == BoomArmFamily.PoweredXl;

        if (mm <= 100)
        {
            string label = useXlSku
                ? $"{mm}mm XL Ceiling Flange"
                : $"{mm}mm Ceiling Flange";
            return new FlangeQuote { Description = label, LookupName = null, FallbackPrice = 0 };
        }

        if (powered)
        {
            string lookup = useXlSku
                ? $"Powered XL - {mm}mm XL Ceiling Flange"
                : $"Powered - {mm}mm Ceiling Flange";
            return new FlangeQuote
            {
                Description = lookup,
                LookupName = lookup,
                FallbackPrice = 0
            };
        }

        string plain = useXlSku
            ? $"{mm}mm XL Ceiling Flange"
            : $"{mm}mm Ceiling Flange";
        return new FlangeQuote { Description = plain, LookupName = plain, FallbackPrice = 0 };
    }

    static IEnumerable<Line> ResolveFlangePackageLines(
        List<SelectablePrice> boomParts,
        List<SelectablePrice> extras)
    {
        var flanges = extras.Where(IsFlangeExtra).ToList();
        if (flanges.Count == 0)
            yield break;

        var names = new List<string>();
        if (boomParts != null)
        {
            foreach (var sp in boomParts)
            {
                if (sp == null) continue;
                if (!string.IsNullOrWhiteSpace(sp.pricingObjectName)) names.Add(sp.pricingObjectName);
                if (!string.IsNullOrWhiteSpace(sp.UIObjectName)) names.Add(sp.UIObjectName);
                var sel = sp.GetComponent<Selectable>();
                if (sel != null && !string.IsNullOrWhiteSpace(sel.UIButtonName))
                    names.Add(sel.UIButtonName);
            }
        }

        var family = DetectBoomArmFamily(names);
        foreach (var g in flanges.GroupBy(BoomExtraGroupKey))
        {
            var sample = g.First();
            string size = sample.objectPricingData?.ObjectSize ?? "";
            string blob = ExtraBlob(sample);
            bool xl = blob.Contains("xl");
            int mm = FlangeSizeMm(size, blob);
            var quote = QuoteFlange(family, mm, xl);

            double unit = 0;
            string part = "N/A";
            string desc = quote.Description;
            if (!string.IsNullOrEmpty(quote.LookupName))
            {
                var row = FindExactBoomPrice(quote.LookupName);
                if (row != null && row.ListPrice > 0)
                {
                    unit = row.ListPrice;
                    if (!string.IsNullOrEmpty(row.PartNumber))
                        part = row.PartNumber;
                    if (!string.IsNullOrEmpty(row.ObjectName))
                        desc = row.ObjectName;
                }
            }

            // Free 100mm flanges stay off the money lines; the size still
            // prints through CleanExtra when a charged flange is listed.
            if (unit <= 0)
                continue;

            desc = ProposalListing.CleanExtra(desc, size, 1);
            yield return new Line
            {
                PartNumber = part,
                Description = desc,
                Qty = 1,
                UnitPrice = unit,
                ExtPrice = unit,
                IsBoom = true
            };
        }
    }

    static bool IsShelf500(SelectablePrice sp)
    {
        string blob = ExtraBlob(sp);
        string size = sp.objectPricingData?.ObjectSize ?? "";
        if (blob.Contains("750") || size.Contains("750"))
            return false;
        return blob.Contains("500") || size.Contains("500") || !blob.Contains("750");
    }

    /// <summary>
    /// How far a light can sit from the boom cover it hangs on.
    /// Setups placed across a room are farther apart than this.
    /// </summary>
    public const float TandemMountReachMeters = 4f;

    /// <summary>
    /// True only when the nearest boom cover within reach is a tandem.
    /// A tandem on another setup does not discount this light.
    /// </summary>
    public static bool LightMountedOnTandem(
        Vector3 lightPosition,
        IReadOnlyList<(Vector3 position, bool tandem)> boomCovers,
        float reachMeters = TandemMountReachMeters)
    {
        if (boomCovers == null || boomCovers.Count == 0)
            return false;

        float best = float.MaxValue;
        bool bestIsTandem = false;
        for (int i = 0; i < boomCovers.Count; i++)
        {
            float distance = Vector3.Distance(lightPosition, boomCovers[i].position);
            if (distance < best)
            {
                best = distance;
                bestIsTandem = boomCovers[i].tandem;
            }
        }

        return bestIsTandem && best <= reachMeters;
    }

    public static bool LightMountedOnTandem(
        IReadOnlyList<SelectablePrice> lights,
        IEnumerable<SelectablePrice> roomPrices)
    {
        if (lights == null || roomPrices == null)
            return false;

        Vector3 lightPosition = default;
        Transform lightRoot = null;
        bool hasLight = false;
        for (int i = 0; i < lights.Count; i++)
        {
            var sp = lights[i];
            if (sp == null)
                continue;
            lightPosition = sp.transform.position;
            lightRoot = sp.transform.root;
            hasLight = true;
            break;
        }
        if (!hasLight)
            return false;

        var covers = new List<(Vector3 position, bool tandem)>();
        foreach (var sp in roomPrices)
        {
            if (sp == null || !sp.isBoomObject)
                continue;
            if (lightRoot != null && sp.transform.root == lightRoot)
                continue;
            string blob = ExtraBlob(sp);
            if (blob.IndexOf("cover", StringComparison.Ordinal) < 0)
                continue;
            covers.Add((sp.transform.position, blob.IndexOf("tandem", StringComparison.Ordinal) >= 0));
        }

        return LightMountedOnTandem(lightPosition, covers);
    }

    /// <summary>
    /// Estimating: light on tandem 2nd position does not pay a second ceiling cover.
    /// The 3D combo row still includes a single-mount cover — credit it off.
    /// </summary>
    static void ApplyTandemSecondPositionCoverCredit(List<Line> lightLines, List<SelectablePrice> lightParts)
    {
        if (lightLines == null || lightLines.Count == 0)
            return;

        double credit = 0;
        if (lightParts != null)
        {
            foreach (var sp in lightParts)
            {
                if (sp?.objectPricingData == null)
                    continue;
                string blob = ExtraBlob(sp);
                if (!blob.Contains("ceiling cover"))
                    continue;
                if (sp.objectPricingData.ListPrice > 0)
                    credit += sp.objectPricingData.ListPrice;
            }
        }

        if (credit <= 0)
        {
            var pm = PricingManager.Instance;
            var row = pm?.GetCachedPricingData(
                          DataFilePaths.sheetNameLight, "Lights - Single Ceiling Cover (Standard)")
                      ?? pm?.GetCachedPricingData(
                          DataFilePaths.sheetNameLight, "Lights - Single Ceiling Cover (Slim)")
                      ?? pm?.GetCachedPricingData(
                          DataFilePaths.sheetNameLight, "115-001112");
            if (row != null && row.ListPrice > 0)
                credit = row.ListPrice;
        }

        var main = lightLines
            .Where(l => l != null && l.IsLight)
            .OrderByDescending(l => l.ExtPrice)
            .FirstOrDefault();
        if (main == null || main.ExtPrice <= credit)
            return;

        main.ExtPrice -= credit;
        main.UnitPrice = main.Qty > 0 ? main.ExtPrice / main.Qty : main.ExtPrice;
    }

    static bool IsUnpricedEstimatingSkip(SelectablePrice sp)
    {
        string blob = ExtraBlob(sp);
        return blob.Contains("blank plate") || blob.Contains("blank preparation")
               || blob.Contains("data plate") || blob.Contains("data pass");
    }

    static bool IsRailExtra(SelectablePrice sp)
    {
        // Estimating Config sheets do not line-item SH rails; keep them off the money total.
        string blob = ExtraBlob(sp);
        return blob.Contains("rail") && !blob.Contains("shelf");
    }

    static string ExtraBlob(SelectablePrice sp)
    {
        return string.Join(" ",
            sp.pricingObjectName ?? "",
            sp.UIObjectName ?? "",
            sp.objectPricingData?.ObjectName ?? "",
            sp.gameObject != null ? sp.gameObject.name : "").ToLowerInvariant();
    }

    /// <summary>
    /// Estimating quotes electrical from HV plate capacity (3 duplex positions per
    /// high-voltage module), not from however many red-duplex meshes happen to be
    /// snapped in. Empty positions on a purchased plate are still in the package.
    /// </summary>
    static int CountDuplexOutletsOnBoom(GameObject boomRoot, List<SelectablePrice> electricalPrices)
    {
        Transform root = boomRoot != null
            ? boomRoot.transform
            : (electricalPrices != null && electricalPrices.Count > 0
                ? electricalPrices[0].transform.root
                : null);
        if (root == null)
            return electricalPrices?.Count ?? 0;

        int fromPlates = 0;
        foreach (var plate in FindBoomHeadPlates(root, highVoltage: true))
            fromPlates += CountNativeAttachSlots(plate);
        if (fromPlates > 0)
            return fromPlates;

        int fromScene = CountSelectablesOnBoom(root, IsHvPowerOutletSelectable);
        if (fromScene > 0)
            return fromScene;

        return electricalPrices?.Count ?? 0;
    }

    /// <summary>
    /// LV plates are the med-gas manifold (2 positions each). Slots filled with
    /// blank/data/AV are not gas. Empty slots count as gas only when the head
    /// already has at least one gas outlet (anes / mixed heads), so a data-only
    /// boom does not invent gas.
    /// </summary>
    static int CountMedGasOutletsOnBoom(GameObject boomRoot, List<SelectablePrice> gasPrices)
    {
        Transform root = boomRoot != null
            ? boomRoot.transform
            : (gasPrices != null && gasPrices.Count > 0
                ? gasPrices[0].transform.root
                : null);
        if (root == null)
            return gasPrices?.Count ?? 0;

        int placedGas = CountSelectablesOnBoom(root, IsGasOutletSelectable);
        int fromPlates = 0;
        foreach (var plate in FindBoomHeadPlates(root, highVoltage: false))
        {
            foreach (var ap in NativeAttachPoints(plate))
            {
                string occ = SlotOccupantBlob(ap);
                if (IsNonGasLvOccupant(occ))
                    continue;
                if (IsGasOccupant(occ) || (string.IsNullOrEmpty(occ) && placedGas > 0))
                    fromPlates++;
            }
        }

        if (fromPlates > 0)
            return fromPlates;
        if (placedGas > 0)
            return placedGas;
        return gasPrices?.Count ?? 0;
    }

    static IEnumerable<Transform> FindBoomHeadPlates(Transform root, bool highVoltage)
    {
        string namePrefix = highVoltage
            ? "BoomHeadAttachment_HighVoltage"
            : "BoomHeadAttachment_LowVoltage";
        string uiNeedle = highVoltage ? "high voltage" : "low voltage";
        var seen = new HashSet<int>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || !seen.Add(t.GetInstanceID()))
                continue;
            if (t.name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return t;
                continue;
            }
            if (!t.TryGetComponent<Selectable>(out var sel))
                continue;
            string ui = sel.UIButtonName ?? "";
            if (ui.IndexOf(uiNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                && ui.IndexOf("module", StringComparison.OrdinalIgnoreCase) >= 0)
                yield return t;
        }
    }

    static int CountNativeAttachSlots(Transform plate)
        => NativeAttachPoints(plate).Count();

    static IEnumerable<AttachmentPoint> NativeAttachPoints(Transform plate)
    {
        if (plate == null)
            yield break;
        var plateSel = plate.GetComponent<Selectable>();
        foreach (var ap in plate.GetComponentsInChildren<AttachmentPoint>(true))
        {
            if (ap == null)
                continue;
            var enclosing = ap.GetComponentInParent<Selectable>();
            if (plateSel != null && enclosing != null && enclosing != plateSel)
                continue;
            yield return ap;
        }
    }

    static string SlotOccupantBlob(AttachmentPoint ap)
    {
        if (ap == null)
            return "";
        var parts = new List<string>();
        if (ap.AttachedSelectable != null)
        {
            foreach (var sel in ap.AttachedSelectable)
            {
                if (sel == null)
                    continue;
                parts.Add(sel.UIButtonName ?? "");
                parts.Add(sel.MetaData.Name ?? "");
                parts.Add(sel.name ?? "");
            }
        }
        foreach (var sel in ap.GetComponentsInChildren<Selectable>(true))
        {
            if (sel == null || sel.transform == ap.transform)
                continue;
            parts.Add(sel.UIButtonName ?? "");
            parts.Add(sel.MetaData.Name ?? "");
            parts.Add(sel.name ?? "");
        }
        return string.Join(" ", parts).ToLowerInvariant();
    }

    static bool IsNonGasLvOccupant(string blob)
    {
        if (string.IsNullOrEmpty(blob))
            return false;
        return blob.Contains("blank")
               || blob.Contains("ethernet")
               || blob.Contains("data plate")
               || blob.Contains("storz")
               || blob.Contains("duplex")
               || blob.Contains("hv power");
    }

    static bool IsGasOccupant(string blob)
    {
        if (string.IsNullOrEmpty(blob))
            return false;
        return blob.Contains("gas outlet") || blob.Contains("medical gas");
    }

    static bool IsHvPowerOutletSelectable(Selectable sel)
    {
        if (sel == null)
            return false;
        string meta = sel.MetaData.Name ?? "";
        return meta.IndexOf("HV Power Outlet", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsGasOutletSelectable(Selectable sel)
    {
        if (sel == null)
            return false;
        string meta = sel.MetaData.Name ?? "";
        string ui = sel.UIButtonName ?? "";
        string blob = $"{meta} {ui} {sel.name}".ToLowerInvariant();
        return meta.StartsWith("Gas Outlet", StringComparison.OrdinalIgnoreCase)
               || ui.IndexOf("Gas Outlet", StringComparison.OrdinalIgnoreCase) >= 0
               || blob.Contains("gas outlet");
    }

    static int CountSelectablesOnBoom(Transform root, Func<Selectable, bool> match)
    {
        var seen = new HashSet<int>();
        int n = 0;
        foreach (var sel in root.GetComponentsInChildren<Selectable>(true))
        {
            if (!match(sel) || !seen.Add(sel.GetInstanceID()))
                continue;
            n++;
        }
        return n;
    }

    /// <summary>
    /// True for add-ons Estimating stacks on top of the boom model (covers, power, gas,
    /// shelves, rails, nitrogen, …). False for structural parts inside the bundled SKU.
    /// </summary>
    public static bool IsBoomExtraPart(SelectablePrice sp)
    {
        if (sp == null)
            return false;

        string blob = string.Join(" ",
            sp.pricingObjectName ?? "",
            sp.UIObjectName ?? "",
            sp.objectPricingData?.ObjectName ?? "",
            sp.gameObject != null ? sp.gameObject.name : "").ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(blob))
            return false;

        // Explicit add-ons (check before "arm" / "service head" base rules).
        if (blob.Contains("shelf") || blob.Contains("drawer") || blob.Contains("rail")
            || blob.Contains("duplex") || blob.Contains("electrical")
            || blob.Contains("medical gas") || blob.Contains("gas outlet") || blob.Contains("med-gas")
            || blob.Contains("nitrogen")
            || blob.Contains("ceiling cover") || blob.Contains("tandem ceiling")
            || blob.Contains("single square") || blob.Contains("mounting plate")
            || blob.Contains("ceiling flange") // Estimating line-items non-zero flanges (e.g. 300mm)
            || blob.Contains("blank plate") || blob.Contains("blank preparation")
            || blob.Contains("data pass") || blob.Contains("accessory"))
            return true;

        // Structural / bundled-base parts.
        if (blob.Contains("top arm") || blob.Contains("toparm")
            || blob.Contains("bottom arm") || blob.Contains("bottomarm")
            || blob.Contains("spring bottom") || blob.Contains("powered xl")
            || blob.Contains("arm (powered") || blob.Contains("fixed bottom")
            || blob.Contains("service head") || blob.Contains("servicehead") || blob.Contains("boomhead")
            || blob.Contains("drop tube") || blob.Contains("column tube")
            || blob.Contains("boomsegment") || blob.Contains("segment_1") || blob.Contains("segment_2"))
            return false;

        if (UINameToExcelKey.IsBoomBaseModelFromExcel(sp.pricingObjectName)
            || UINameToExcelKey.IsBoomBaseModelFromExcel(sp.objectPricingData?.ObjectName))
            return false;

        // Unknown boom-priced part with money → treat as extra so we don't drop dollars.
        return sp.objectPricingData.ListPrice > 0;
    }

    static string BoomExtraGroupKey(SelectablePrice sp)
    {
        string name = sp.objectPricingData?.ObjectName ?? sp.pricingObjectName ?? sp.UIObjectName ?? sp.name;
        string size = sp.objectPricingData?.ObjectSize ?? "";
        string part = sp.objectPricingData?.PartNumber ?? "";
        return $"{name}\u001f{size}\u001f{part}".ToLowerInvariant();
    }

    static PriceExcelData TryLookupBundledBoom(GameObject boomRoot, List<SelectablePrice> boomParts)
    {
        var pm = PricingManager.Instance;
        if (pm == null)
            return null;

        string key = BuildBoomConfigKey(boomRoot, boomParts);
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return pm.GetCachedPricingData(DataFilePaths.sheetNameBoomCombined, key);
    }

    /// <summary>
    /// Fingerprint matching "2025_03_28 3D Boom Pricing" Configuration column, e.g.
    /// "Powered Boom, Top Arm 1000mm, SH 200mm".
    /// </summary>
    public static string BuildBoomConfigKey(GameObject boomRoot, List<SelectablePrice> boomParts)
    {
        var bcm = boomRoot != null
            ? boomRoot.GetComponentInChildren<BoomConfigurationManager>(true)
            : null;

        int topMm = 0;
        int bottomMm = 0;
        int shMm = 0;
        bool xlTop = false;
        bool xlBottom = false;
        string family = "Powered Boom";

        // Live scale levels beat serialized BoomConfigurationManager arm lengths.
        InferSizesFromSelectables(boomRoot, ref topMm, ref bottomMm, ref shMm);

        // Family from catalog UI / pricing identity only — never mesh GameObject names.
        // (Mesh parts like "MCP - Large Monitor Boom" under Spring arms caused false families.)
        bool sceneFamilyKnown = InferBoomFamilyFromCatalog(boomRoot, boomParts, ref family, ref xlTop, ref xlBottom);

        if (bcm != null)
        {
            if (!sceneFamilyKnown)
                ApplyBoomConfigurationManager(bcm, ref family, ref xlTop, ref xlBottom);
            else if (TryMapFamilyToBoomType(family, xlTop, xlBottom, out var mappedType))
                bcm.SyncBoomTypeIdentity(mappedType);

            if (topMm <= 0 && bcm.TopArmLength > 0)
                topMm = bcm.TopArmLength;
            if (bottomMm <= 0 && bcm.BottomArmLength > 0)
                bottomMm = bcm.BottomArmLength;
        }

        if (topMm <= 0) topMm = 1000;
        if (bottomMm <= 0) bottomMm = 1000;
        if (shMm <= 0) shMm = 200;

        // Sheet keys: "Large Monitor Boom, Top Arm 600 mm" (space before mm, no SH).
        if (family.StartsWith("Large Monitor", StringComparison.OrdinalIgnoreCase))
        {
            string lmArm = xlTop ? "XL Top Arm" : "Top Arm";
            return $"{family}, {lmArm} {topMm} mm";
        }

        if (family.StartsWith("Fixed", StringComparison.OrdinalIgnoreCase))
        {
            string topLabel = xlTop ? "XL Top" : "Top Arm";
            string bottomLabel = xlBottom ? "XL Bottom Arm" : "Bottom Arm";
            return $"{family}, {topLabel} {topMm}mm, {bottomLabel} {bottomMm}mm, SH {shMm}mm";
        }

        string armLabel = xlTop ? "XL Top Arm" : "Top Arm";
        return $"{family}, {armLabel} {topMm}mm, SH {shMm}mm";
    }

    static void ApplyBoomConfigurationManager(
        BoomConfigurationManager bcm, ref string family, ref bool xlTop, ref bool xlBottom)
    {
        // BCM is authoritative for family/XL when the prefab declares a real boom type.
        switch (bcm.CurrentBoomType)
        {
            case BoomConfigurationManager.BoomType.PoweredBoom:
                family = "Powered Boom";
                break;
            case BoomConfigurationManager.BoomType.PoweredXLBoom:
                family = "Powered Boom";
                xlTop = true;
                break;
            case BoomConfigurationManager.BoomType.SpringBoom:
                family = "Spring Boom";
                break;
            case BoomConfigurationManager.BoomType.SpringXLBoom:
                family = "Spring Boom";
                xlTop = true;
                break;
            case BoomConfigurationManager.BoomType.FixedBoom:
                family = "Fixed Boom";
                break;
            case BoomConfigurationManager.BoomType.FixedXLBoom:
                family = "Fixed Boom";
                xlTop = true;
                break;
            case BoomConfigurationManager.BoomType.FixedXXLBoom:
                family = "Fixed Boom";
                xlTop = true;
                xlBottom = true;
                break;
            case BoomConfigurationManager.BoomType.ServiceHead:
                // Leave family from bottom-arm inference (SH-only prefabs).
                break;
        }
    }

    /// <summary>
    /// Map catalog-resolved family to BCM enum. Large Monitor has no BCM type — skip sync.
    /// </summary>
    static bool TryMapFamilyToBoomType(
        string family, bool xlTop, bool xlBottom, out BoomConfigurationManager.BoomType boomType)
    {
        boomType = BoomConfigurationManager.BoomType.PoweredBoom;
        if (string.IsNullOrEmpty(family)
            || family.StartsWith("Large Monitor", StringComparison.OrdinalIgnoreCase))
            return false;

        if (family.StartsWith("Spring", StringComparison.OrdinalIgnoreCase))
        {
            boomType = xlTop
                ? BoomConfigurationManager.BoomType.SpringXLBoom
                : BoomConfigurationManager.BoomType.SpringBoom;
            return true;
        }

        if (family.StartsWith("Fixed", StringComparison.OrdinalIgnoreCase))
        {
            if (xlBottom)
                boomType = BoomConfigurationManager.BoomType.FixedXXLBoom;
            else if (xlTop)
                boomType = BoomConfigurationManager.BoomType.FixedXLBoom;
            else
                boomType = BoomConfigurationManager.BoomType.FixedBoom;
            return true;
        }

        if (family.StartsWith("Powered", StringComparison.OrdinalIgnoreCase))
        {
            boomType = xlTop
                ? BoomConfigurationManager.BoomType.PoweredXLBoom
                : BoomConfigurationManager.BoomType.PoweredBoom;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolve boom family from catalog labels only (UIButtonName / pricingObjectName / UIObjectName).
    /// Never reads mesh GameObject names — those are geometry labels and caused false
    /// Large-Monitor / Spring / Powered classifications.
    /// </summary>
    static bool InferBoomFamilyFromCatalog(
        GameObject boomRoot,
        List<SelectablePrice> boomParts,
        ref string family,
        ref bool xlTop,
        ref bool xlBottom)
    {
        int powered = 0, spring = 0, fixedBottom = 0, largeMonitor = 0;
        bool topArmXl = false;
        bool poweredXlBottom = false;

        void ConsiderCatalogLabel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            string n = raw.Trim().ToLowerInvariant();

            // Top-arm catalog SKUs: XL flag only.
            if (n.Contains("top arm") || n.Contains("toparm"))
            {
                if (n.Contains("(xl)") || n.EndsWith(" xl") || n.Contains(" xl "))
                    topArmXl = true;
                return;
            }

            // Explicit Large Monitor boom catalog entry (not C-Arm Monitor, not MCP mesh parts).
            if (n.Contains("large monitor mount")
                || n.Equals("large monitor boom", StringComparison.Ordinal)
                || n.StartsWith("large monitor boom,", StringComparison.Ordinal))
            {
                largeMonitor++;
                return;
            }

            // Bottom / articulating arm catalog SKUs — these define family.
            if (n.Contains("spring bottom")
                || n.Contains("bottom arm - spring")
                || n.Contains("bottom arm – spring")
                || (n.Contains("spring") && n.Contains("bottom arm")))
            {
                spring++;
                return;
            }

            if (n.Contains("arm (powered")
                || n.Contains("powered bottom")
                || n.Contains("bottom arm - powered")
                || n.Contains("bottom arm – powered")
                || (n.Contains("powered") && n.Contains("bottom arm")))
            {
                powered++;
                if (n.Contains("xl"))
                    poweredXlBottom = true;
                return;
            }

            if (n.Contains("fixed bottom")
                || n.Contains("bottom arm - fixed")
                || n.Contains("bottom arm – fixed")
                || (n.Contains("fixed") && n.Contains("bottom arm")))
            {
                fixedBottom++;
                return;
            }
        }

        if (boomParts != null)
        {
            foreach (var sp in boomParts)
            {
                if (sp == null) continue;
                ConsiderCatalogLabel(sp.UIObjectName);
                ConsiderCatalogLabel(sp.pricingObjectName);
            }
        }

        if (boomRoot != null)
        {
            foreach (var sel in boomRoot.GetComponentsInChildren<Selectable>(true))
            {
                if (sel == null) continue;
                ConsiderCatalogLabel(sel.UIButtonName);
            }
        }

        if (spring > 0 && spring >= powered && spring >= fixedBottom)
            family = "Spring Boom";
        else if (powered > 0 && powered >= spring && powered >= fixedBottom)
        {
            family = "Powered Boom";
            if (poweredXlBottom)
                xlTop = true; // Powered XL articulating arm implies XL package
        }
        else if (fixedBottom > 0)
            family = "Fixed Boom";
        else if (largeMonitor > 0)
            family = "Large Monitor Boom";
        else
            return false;

        if (topArmXl)
            xlTop = true;

        return true;
    }

    static void InferSizesFromSelectables(GameObject root, ref int topMm, ref int bottomMm, ref int shMm)
    {
        if (root == null)
            return;
        foreach (var sel in root.GetComponentsInChildren<Selectable>(true))
        {
            if (sel == null)
                continue;

            // Role from catalog UIButtonName first; mesh names only as last-resort length hints.
            string ui = (sel.UIButtonName ?? "").ToLowerInvariant();
            string mesh = (sel.gameObject.name ?? "").ToLowerInvariant();
            float size = sel.CurrentScaleLevel?.Size
                         ?? sel.CurrentPreviewScaleLevel?.Size
                         ?? 0f;
            int mm = size >= 10f ? Mathf.RoundToInt(size) : Mathf.RoundToInt(size * 1000f);
            if (mm <= 0)
                continue;

            bool isTop = ui.Contains("top arm") || ui.Contains("toparm")
                         || (!ui.Contains("bottom") && (mesh.Contains("boomsegment_1") || mesh.Contains("segment_1")));
            bool isBottom = ui.Contains("bottom arm") || ui.Contains("bottomarm") || ui.Contains("arm (powered")
                            || ui.Contains("spring bottom")
                            || (!ui.Contains("top") && (mesh.Contains("boomsegment_2") || mesh.Contains("segment_2")));

            if (isTop)
                topMm = mm;
            else if (isBottom)
                bottomMm = mm;
            else if (sel.GetComponent<BoomHeadScaleHandler>() != null
                     || ui.Contains("service head") || ui.Contains("servicehead"))
                shMm = mm;
        }
    }

    static int ResolveTandemQty(Transform root)
    {
        if (root == null)
            return 1;
        var selectables = root.GetComponentsInChildren<Selectable>(true);
        bool isTandem = selectables.Any(s =>
            !string.IsNullOrEmpty(s.UIButtonName) &&
            s.UIButtonName.Contains("Boom - Tandem Ceiling Cover"));
        if (!isTandem)
            return 1;
        int heads = selectables.Count(s => s.GetComponent<BoomHeadScaleHandler>() != null);
        return heads >= 2 ? 2 : 1;
    }

}
