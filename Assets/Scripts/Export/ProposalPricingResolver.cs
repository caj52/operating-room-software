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

        var priced = allPrices.Where(sp => sp != null && sp.objectPricingData != null).ToList();
        foreach (var group in priced.GroupBy(sp => sp.transform.root))
        {
            var lights = group.Where(sp => !sp.isBoomObject).ToList();
            var booms = group.Where(sp => sp.isBoomObject).ToList();

            lines.AddRange(ResolveLightLines(lights));
            var boomLine = ResolveBoomLine(group.Key != null ? group.Key.gameObject : null, booms);
            if (boomLine != null)
                lines.Add(boomLine);
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

    public static double CalculateGrandEquipmentTotal(IEnumerable<SelectablePrice> allPrices)
        => SumEquipment(allPrices) + SumPricedOptions() + SumInstallAndShip();

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
                data = pm.GetCachedPricingData(sheet, string.Join(", ", names));
                if (data != null) take = 3;
            }
            if (data == null && names.Count >= 2)
            {
                data = pm.GetCachedPricingData(sheet, string.Join(", ", names.Take(2)));
                if (data != null) take = 2;
            }
            if (data == null)
            {
                data = pm.GetCachedPricingData(sheet, names[0]);
                take = 1;
            }

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
            // Bundled sheet quotes a full assembly — do not multiply by tandem head count.
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

        // Fallback: sum individual boom parts (legacy) — log so we can spot missed fingerprints.
        double sum = 0;
        bool simFlexOnce = false;
        foreach (var sp in boomParts)
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
            $"Falling back to sum of individual parts ({sum:C}).");

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

        // Live scale levels beat serialized BoomConfigurationManager arm lengths
        // (stale 1000mm defaults caused Spring/Anes lookups to miss the sheet).
        InferSizesFromSelectables(boomRoot, ref topMm, ref bottomMm, ref shMm);
        InferBoomFamilyFromParts(boomParts, ref family, ref xlTop);
        InferBoomFamilyFromHierarchy(boomRoot, ref family, ref xlTop, ref xlBottom);

        if (bcm != null)
        {
            switch (bcm.CurrentBoomType)
            {
                case BoomConfigurationManager.BoomType.PoweredBoom:
                    if (!family.StartsWith("Spring", StringComparison.OrdinalIgnoreCase)
                        && !family.StartsWith("Fixed", StringComparison.OrdinalIgnoreCase)
                        && !family.StartsWith("Large Monitor", StringComparison.OrdinalIgnoreCase))
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
                    if (family == "Powered Boom")
                        family = "Powered Boom";
                    break;
            }

            // Only fill gaps — never overwrite live scale lengths with prefab defaults.
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

    static void InferBoomFamilyFromHierarchy(
        GameObject boomRoot, ref string family, ref bool xlTop, ref bool xlBottom)
    {
        if (boomRoot == null)
            return;

        string blob = boomRoot.name ?? "";
        foreach (var sel in boomRoot.GetComponentsInChildren<Selectable>(true))
        {
            if (sel == null) continue;
            blob += " " + (sel.UIButtonName ?? "") + " " + (sel.name ?? "") + " " + (sel.gameObject.name ?? "");
        }

        blob = blob.ToLowerInvariant();
        bool xl = blob.Contains("xl");
        if (blob.Contains("spring"))
        {
            family = "Spring Boom";
            xlTop = xl || xlTop;
            return;
        }
        if (blob.Contains("large monitor") || blob.Contains("cemor"))
        {
            family = "Large Monitor Boom";
            xlTop = xl || xlTop;
            return;
        }
        if (blob.Contains("fixed") && !blob.Contains("powered"))
        {
            family = "Fixed Boom";
            xlTop = xl || xlTop;
            xlBottom = blob.Contains("xxl") || xlBottom;
            return;
        }
        if (blob.Contains("powered"))
        {
            family = "Powered Boom";
            xlTop = xl || xlTop;
        }
    }

    static void InferBoomFamilyFromParts(List<SelectablePrice> parts, ref string family, ref bool xlTop)
    {
        foreach (var sp in parts)
        {
            string n = (sp.UIObjectName ?? sp.pricingObjectName ?? "").ToLowerInvariant();
            if (n.Contains("spring"))
            {
                family = "Spring Boom";
                xlTop = n.Contains("xl");
                return;
            }
            if (n.Contains("fixed"))
            {
                family = "Fixed Boom";
                xlTop = n.Contains("xl");
                return;
            }
            if (n.Contains("large monitor"))
            {
                family = "Large Monitor Boom";
                xlTop = n.Contains("xl");
                return;
            }
            if (n.Contains("powered"))
            {
                family = "Powered Boom";
                xlTop = n.Contains("xl");
                return;
            }
        }
    }

    static void InferSizesFromSelectables(GameObject root, ref int topMm, ref int bottomMm, ref int shMm)
    {
        if (root == null)
            return;
        foreach (var sel in root.GetComponentsInChildren<Selectable>(true))
        {
            if (sel == null)
                continue;

            string name = ((sel.UIButtonName ?? "") + " " + (sel.name ?? "") + " " + (sel.gameObject.name ?? ""))
                .ToLowerInvariant();
            float size = sel.CurrentScaleLevel?.Size
                         ?? sel.CurrentPreviewScaleLevel?.Size
                         ?? 0f;
            int mm = size >= 10f ? Mathf.RoundToInt(size) : Mathf.RoundToInt(size * 1000f);
            if (mm <= 0)
                continue;

            bool isTop = name.Contains("toparm") || name.Contains("top_arm") || name.Contains("top arm")
                         || name.Contains("boomsegment_1") || name.Contains("segment_1");
            bool isBottom = name.Contains("bottomarm") || name.Contains("bottom_arm") || name.Contains("bottom arm")
                            || name.Contains("boomsegment_2") || name.Contains("segment_2");

            if (isTop)
                topMm = mm;
            else if (isBottom)
                bottomMm = mm;
            else if (sel.GetComponent<BoomHeadScaleHandler>() != null
                     || name.Contains("service head") || name.Contains("servicehead") || name.Contains("boomhead"))
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

    static (string Part, string Name, double Unit) IndividualLightKey(SelectablePrice sp)
    {
        var data = sp.objectPricingData;
        string name = string.IsNullOrEmpty(data.ObjectSize)
            ? data.ObjectName
            : $"{data.ObjectSize} {data.ObjectName}";
        return (data.PartNumber, name, data.ListPrice);
    }
}
