using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    public static double SumInstallAndShip()
    {
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
        => SumConfigListPrice(allPrices) + SumInstallAndShip();

    static bool IsNone(string name)
        => string.IsNullOrWhiteSpace(name)
           || name.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Same 3→2→1 combo walk as PopulateUIWithPricingItems.ProcessLightGroup.
    /// </summary>
    public static List<Line> ResolveLightLines(List<SelectablePrice> lights)
    {
        var lines = new List<Line>();
        if (lights == null || lights.Count == 0)
            return lines;

        lights = lights
            .Where(sp => sp != null && IsLightComboHead(sp))
            .OrderBy(sp => LightHierarchySortKey(sp.transform), StringComparer.Ordinal)
            .ToList();
        if (lights.Count == 0)
            return lines;

        var pm = PricingManager.Instance;
        if (pm == null)
        {
            // Fallback: individual Excel rows (no combo).
            foreach (var g in lights.GroupBy(IndividualLightKey))
            {
                int qty = g.Count();
                double unit = g.Key.Unit;
                lines.Add(new Line
                {
                    PartNumber = g.Key.Part ?? "N/A",
                    Description = g.Key.Name,
                    Qty = qty,
                    UnitPrice = unit,
                    ExtPrice = unit * qty,
                    IsLight = true
                });
            }
            return lines;
        }

        string sheet = DataFilePaths.sheetNameLight;
        bool simFlex = lights.Any(HasSimFlexInRoot);
        int i = 0;
        while (i < lights.Count)
        {
            var names = GetValidLightNames(lights, i, 3);
            if (names.Count == 0)
            {
                i++;
                continue;
            }

            PriceExcelData data = null;
            int take = 1;
            if (names.Count == 3)
            {
                data = LookupLightCombo(pm, sheet, names);
                if (data != null) take = 3;
            }
            if (data == null && names.Count >= 2)
            {
                data = LookupLightCombo(pm, sheet, names.Take(2).ToList());
                if (data != null) take = 2;
            }
            if (data == null)
            {
                data = LookupLightCombo(pm, sheet, names.Take(1).ToList());
                take = 1;
            }

            if (data != null && (LooksLikeMultiHeadLightPackage(data.ObjectName) && take == 1
                                 || !ComboRowFitsAskedHeads(data, names.Take(take).ToList())))
                data = null;

            if (data != null)
            {
                double unit = data.ListPrice + (simFlex ? data.SimFlexPrice : 0);
                // SimFlex only once for the whole root — clear after first light line.
                if (simFlex)
                    simFlex = false;

                lines.Add(new Line
                {
                    PartNumber = string.IsNullOrEmpty(data.PartNumber) ? "N/A" : data.PartNumber,
                    Description = string.IsNullOrEmpty(data.ObjectName) ? names[0] : data.ObjectName,
                    Qty = 1,
                    UnitPrice = unit,
                    ExtPrice = unit,
                    IsLight = true
                });
            }
            else
            {
                // Last-resort individual from SelectablePrice data.
                var sp = lights[i];
                double unit = sp.objectPricingData.ListPrice;
                lines.Add(new Line
                {
                    PartNumber = sp.objectPricingData.PartNumber ?? "N/A",
                    Description = sp.pricingObjectName ?? sp.objectPricingData.ObjectName ?? "LIGHT",
                    Qty = 1,
                    UnitPrice = unit,
                    ExtPrice = unit,
                    IsLight = true
                });
            }

            i += take;
        }

        if (RoomHasTandemCeilingCover(lights)
            || RoomHasTandemCeilingCover(PricingManager.CollectActiveSelectablePrices()))
            ApplyTandemSecondPositionCoverCredit(lines, lights);

        return lines;
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

        int qty = ResolveTandemQty(boomRoot != null ? boomRoot.transform : boomParts[0].transform.root);

        var bundled = TryLookupBundledBoom(boomRoot, boomParts);
        if (bundled != null)
        {
            // Bundled sheet quotes the structural assembly (arms / SH fingerprint).
            // Covers, duplexes, gas, shelves, etc. are added via ResolveBoomExtraLines.
            return new Line
            {
                PartNumber = string.IsNullOrEmpty(bundled.PartNumber) ? "N/A" : bundled.PartNumber,
                Description = string.IsNullOrEmpty(bundled.ObjectName)
                    ? ProposalPDFGenerator.BuildBoomModelDescription(boomRoot)
                    : bundled.ObjectName,
                Qty = 1,
                UnitPrice = bundled.ListPrice,
                ExtPrice = bundled.ListPrice,
                IsBoom = true
            };
        }

        // Fallback: structural / base parts only — extras still come from ResolveBoomExtraLines.
        double sum = 0;
        bool simFlexOnce = false;
        foreach (var sp in boomParts.Where(sp => sp != null && !IsBoomExtraPart(sp)))
        {
            var data = sp.objectPricingData;
            if (data == null) continue;

            double part = sp.UIRefPricingRowDataFill != null
                ? sp.UIRefPricingRowDataFill.Price
                : data.ListPrice;

            if (!simFlexOnce && data.isSimFlexArmAvailable && sp.UIRefPricingRowDataFill == null)
            {
                part += data.SimFlexPrice;
                simFlexOnce = true;
            }

            sum += part;
        }

        Debug.LogWarning(
            $"[ProposalPricing] No bundled boom match for '{BuildBoomConfigKey(boomRoot, boomParts)}'. " +
            $"Falling back to sum of base parts ({sum:C}); extras priced separately.");

        if (sum <= 0)
            return null;

        double list = qty == 2 ? sum / 2.0 : sum;
        return new Line
        {
            PartNumber = boomParts[0].objectPricingData?.PartNumber ?? "N/A",
            Description = ProposalPDFGenerator.BuildBoomModelDescription(boomRoot)
                          ?? "ARTICULATING BOOM",
            Qty = qty,
            UnitPrice = list,
            ExtPrice = list * qty,
            IsBoom = true
        };
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

        var remaining = extras
            .Where(sp => !IsElectricalExtra(sp) && !IsMedGasExtra(sp) && !IsShelfExtra(sp)
                         && !IsRailExtra(sp) && !IsUnpricedEstimatingSkip(sp))
            .GroupBy(BoomExtraGroupKey)
            .ToList();

        foreach (var g in remaining)
        {
            var sample = g.First();
            double unit = sample.UIRefPricingRowDataFill != null
                ? sample.UIRefPricingRowDataFill.Price
                : sample.objectPricingData.ListPrice;
            if (unit <= 0)
                continue;

            int qty = g.Count();
            string desc = sample.objectPricingData.ObjectName
                          ?? sample.pricingObjectName
                          ?? sample.UIObjectName
                          ?? "Boom option";
            if (!string.IsNullOrEmpty(sample.objectPricingData.ObjectSize)
                && desc.IndexOf(sample.objectPricingData.ObjectSize, StringComparison.OrdinalIgnoreCase) < 0)
                desc = $"{sample.objectPricingData.ObjectSize} {desc}".Trim();

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

        string packageName = electrical
            ? $"Electrical ({count} Duplex)"
            : $"Medical Gases ({count}x)";

        var pm = PricingManager.Instance;
        PriceExcelData packaged = pm?.GetCachedPricingData(DataFilePaths.sheetNameBoomIndividual, packageName);
        if (packaged == null && electrical)
            packaged = pm?.GetCachedPricingData(
                DataFilePaths.sheetNameBoomIndividual, $"Electrical ({count} Duplexes)");
        if (packaged == null && !electrical)
            packaged = pm?.GetCachedPricingData(
                DataFilePaths.sheetNameBoomIndividual, $"{count} Outlets");

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

    static Line LookupShelfPackage(int mm, int count)
    {
        var pm = PricingManager.Instance;
        // Sheet uses both "Shelf" and "Shelfs"
        string[] keys =
        {
            $"({count}) {mm}mm Shelfs",
            $"({count}) {mm}mm Shelves",
            $"({count}) {mm}mm Shelf",
        };

        PriceExcelData packaged = null;
        foreach (var key in keys)
        {
            packaged = pm?.GetCachedPricingData(DataFilePaths.sheetNameBoomIndividual, key);
            if (packaged != null && packaged.ListPrice > 0)
                break;
        }

        double unitFallback = mm >= 750 ? 1942.5417 : 1285.2667;
        double ext = packaged != null && packaged.ListPrice > 0
            ? packaged.ListPrice
            : unitFallback * count;
        if (ext <= 0)
            return null;

        return new Line
        {
            PartNumber = packaged != null && !string.IsNullOrEmpty(packaged.PartNumber)
                ? packaged.PartNumber
                : "N/A",
            Description = packaged != null && !string.IsNullOrEmpty(packaged.ObjectName)
                ? packaged.ObjectName
                : $"({count}) {mm}mm Shelves",
            Qty = 1,
            UnitPrice = ext,
            ExtPrice = ext,
            IsBoom = true
        };
    }

    static double LookupUnitOutletRate(bool electrical)
    {
        var pm = PricingManager.Instance;
        if (pm == null)
            return electrical ? 234.70 : 814.675;

        if (electrical)
        {
            var one = pm.GetCachedPricingData(DataFilePaths.sheetNameBoomIndividual, "Electrical (1 Duplex)");
            if (one != null && one.ListPrice > 0)
                return one.ListPrice;
            return 234.70;
        }

        var gas = pm.GetCachedPricingData(DataFilePaths.sheetNameBoomIndividual, "Medical Gases (1x)")
                  ?? pm.GetCachedPricingData(DataFilePaths.sheetNameBoomIndividual, "Medical Gas")
                  ?? pm.GetCachedPricingData(DataFilePaths.sheetNameBoomIndividual, "1 Outlets");
        if (gas != null && gas.ListPrice > 0)
            return gas.ListPrice;
        return 814.675;
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

    static bool IsShelf500(SelectablePrice sp)
    {
        string blob = ExtraBlob(sp);
        string size = sp.objectPricingData?.ObjectSize ?? "";
        if (blob.Contains("750") || size.Contains("750"))
            return false;
        return blob.Contains("500") || size.Contains("500") || !blob.Contains("750");
    }

    static bool RoomHasTandemCeilingCover(IEnumerable<SelectablePrice> prices)
    {
        if (prices == null)
            return false;
        foreach (var sp in prices)
        {
            if (sp == null)
                continue;
            string blob = ExtraBlob(sp);
            if (blob.Contains("tandem") && blob.Contains("cover"))
                return true;
        }
        return false;
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

        // Catalog Single Ceiling Cover (Standard) if the 3D combo column has no name.
        if (credit <= 0)
            credit = 3398.97;

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

    static bool HasSimFlexInRoot(SelectablePrice sp)
    {
        if (sp == null)
            return false;
        return sp.transform.root
            .GetComponentsInChildren<Selectable>(true)
            .Any(s => s != null && s.name.ToLowerInvariant().Contains("simflexarm"));
    }

    static bool IsLightComboHead(SelectablePrice sp)
    {
        string n = sp.pricingObjectName ?? sp.objectPricingData?.ObjectName;
        return GetAttachedObjects.IsSurgicalLightPricingName(n)
               || GetAttachedObjects.IsFlatPanelPricingName(n);
    }

    static bool LooksLikeMultiHeadLightPackage(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName) || objectName.IndexOf(',') < 0)
            return false;
        int heads = 0;
        foreach (var bit in objectName.Split(','))
        {
            if (GetAttachedObjects.IsSurgicalLightPricingName(bit)
                || GetAttachedObjects.IsFlatPanelPricingName(bit))
                heads++;
        }
        return heads >= 2;
    }

    static bool ComboRowFitsAskedHeads(PriceExcelData data, List<string> usedNames)
    {
        if (data == null || usedNames == null || usedNames.Count == 0)
            return false;
        string row = data.ObjectName ?? "";
        bool askedOne = usedNames.Any(n => n != null && n.IndexOf("ONE", StringComparison.OrdinalIgnoreCase) >= 0);
        bool askedPanel = usedNames.Any(GetAttachedObjects.IsFlatPanelPricingName);
        bool rowHasOne = row.IndexOf("ONE", StringComparison.OrdinalIgnoreCase) >= 0;
        bool rowHasPanel = GetAttachedObjects.IsFlatPanelPricingName(row);
        if (askedOne && !rowHasOne)
            return false;
        if (askedPanel && !rowHasPanel)
            return false;
        if (!askedPanel && rowHasPanel)
            return false;
        return true;
    }

    static string LightHierarchySortKey(Transform t)
    {
        if (t == null)
            return "";
        var parts = new List<int>();
        while (t != null)
        {
            parts.Add(t.GetSiblingIndex());
            t = t.parent;
        }
        parts.Reverse();
        return string.Join(".", parts);
    }

    static List<string> GetValidLightNames(List<SelectablePrice> lights, int startIndex, int maxCount)
    {
        var result = new List<string>();
        int end = Mathf.Min(startIndex + maxCount, lights.Count);
        for (int j = startIndex; j < end; j++)
        {
            string name = lights[j].pricingObjectName?.Trim();
            if (string.IsNullOrEmpty(name))
                break;
            result.Add(name);
        }
        return result;
    }

    /// <summary>
    /// Sheet column-2 strings are inconsistent (spaces, "U|002 LC" vs "U | 002 (Low Ceiling)").
    /// Try a few aliases so combo rows hit instead of summing individuals.
    /// </summary>
    static PriceExcelData LookupLightCombo(PricingManager pm, string sheet, List<string> names)
    {
        if (pm == null || names == null || names.Count == 0)
            return null;

        foreach (var variant in BuildLightComboKeyVariants(names))
        {
            var data = pm.GetCachedPricingData(sheet, variant);
            if (data != null)
                return data;
        }

        return null;
    }

    static IEnumerable<string> BuildLightComboKeyVariants(List<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new List<string>();
        void Add(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                return;
            keys.Add(key);
        }

        var aliasSets = names.Select(LightNameAliases).Select(a => a.ToList()).ToList();

        Add(string.Join(", ", names));
        Add(string.Join(",", names));
        Add(string.Join(", ", names.Select(NormalizeLightToken)));
        Add(string.Join(",", names.Select(NormalizeLightToken)));

        var lcNames = names.Select(ToLowCeilingShortForm).ToList();
        Add(string.Join(", ", lcNames));
        Add(string.Join(",", lcNames));

        if (names.Count == 2)
        {
            Add(string.Join(", ", names[1], names[0]));
            Add(string.Join(",", names[1], names[0]));
            Add(string.Join(", ", lcNames[1], lcNames[0]));
            Add(string.Join(",", lcNames[1], lcNames[0]));
        }

        if (aliasSets.Count >= 1)
        {
            foreach (var a in aliasSets[0])
            {
                if (names.Count == 1)
                {
                    Add(a);
                    continue;
                }

                if (aliasSets.Count < 2)
                    continue;

                foreach (var b in aliasSets[1])
                {
                    Add(string.Join(", ", a, b));
                    Add(string.Join(",", a, b));
                    if (names.Count >= 3 && aliasSets.Count >= 3)
                    {
                        foreach (var c in aliasSets[2])
                        {
                            Add(string.Join(", ", a, b, c));
                            Add(string.Join(",", a, b, c));
                        }
                    }
                }
            }
        }

        return keys;
    }

    static IEnumerable<string> LightNameAliases(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            yield break;

        string n = name.Trim();
        yield return n;
        yield return NormalizeLightToken(n);
        yield return ToLowCeilingShortForm(n);

        string compact = NormalizeLightToken(n)
            .Replace("Lights - ", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        if (!string.IsNullOrEmpty(compact))
            yield return compact;

        // Sheet sometimes drops the "Lights - " prefix on the second item only.
        if (n.StartsWith("Lights - ", StringComparison.OrdinalIgnoreCase))
            yield return n.Substring("Lights - ".Length).Trim();
    }

    static string ToLowCeilingShortForm(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;
        string n = NormalizeLightToken(name);
        n = System.Text.RegularExpressions.Regex.Replace(
            n,
            @"U\s*\|\s*002\s*\(\s*Low\s*Ceiling\s*\)",
            "U|002 LC",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        n = System.Text.RegularExpressions.Regex.Replace(
            n,
            @"U\s*\|\s*002\s+Low\s+Ceiling",
            "U|002 LC",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return n;
    }

    static string NormalizeLightToken(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;
        string n = name.Trim();
        n = System.Text.RegularExpressions.Regex.Replace(n, @"\s+", " ");
        n = System.Text.RegularExpressions.Regex.Replace(n, @"U\s*\|\s*", "U|");
        return n;
    }

    static (string Part, string Name, double Unit) IndividualLightKey(SelectablePrice sp)
    {
        var data = sp.objectPricingData;
        string name = string.IsNullOrEmpty(data.ObjectSize)
            ? data.ObjectName
            : $"{data.ObjectSize} {data.ObjectName}";
        return (data.PartNumber, name, data.ListPrice);
    }
}
