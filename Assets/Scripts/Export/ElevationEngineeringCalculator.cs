using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Nice-to-have elevation weights: fill from known sample/config rows in CSV, else leave unset (XXX).
/// Does not invent service-head mass or max-payload tables.
/// </summary>
public static class ElevationEngineeringCalculator
{
    public sealed class PartRow
    {
        public string CatalogName;
        public string SizeKey;
        public string Category;
        public double? WeightKg;
        public double? TorqueNm;
        public double? VerticalForceN;
        public double? PayloadCapacityKg;
    }

    public sealed class Result
    {
        public double? OverallWeightKg;
        public double? TorqueMomentNm;
        public double? VerticalForceN;
        public double? PayloadCapacityKg;
    }

    static readonly Regex MmInName = new(
        @"(\d+)\s*mm",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static List<PartRow> _rows;
    static bool _loadAttempted;

    public static Result TryCompute(ElevationEngineeringResolver.AssemblyKind kind, IList<Selectable> selectables)
    {
        EnsureLoaded();
        var result = new Result();
        if (_rows == null || _rows.Count == 0 || selectables == null || selectables.Count == 0)
            return result;

        if (kind == ElevationEngineeringResolver.AssemblyKind.BoomServiceHead)
        {
            TryBoomAssembly(selectables, result);
            return result;
        }

        if (kind == ElevationEngineeringResolver.AssemblyKind.LightFamily)
        {
            if (!TryLightAssembly(selectables, result))
                SumLightHeads(selectables, result);
            return result;
        }

        return result;
    }

    static bool TryBoomAssembly(IList<Selectable> selectables, Result result)
    {
        var bcm = FindBoomConfig(selectables);
        if (bcm == null)
            return false;

        int? sh = EstimateServiceHeadMm(selectables);
        if (!sh.HasValue)
            return false;

        int top = bcm.TopArmLength;
        int bottom = bcm.BottomArmLength;
        string[] keys =
        {
            $"Spring XL Boom (Top {top} / Bottom {bottom} / SH {sh.Value})",
            $"MediBoom dual (Top {top} / Bottom {bottom} / SH {sh.Value})",
            $"MediBoom tandem (Top {top} / Bottom {bottom} / SH {sh.Value})"
        };

        foreach (string key in keys)
        {
            var row = _rows.FirstOrDefault(r =>
                r.Category == "boom_assembly"
                && r.CatalogName.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (row == null)
                continue;
            Apply(row, result, includePayload: true);
            return true;
        }
        return false;
    }

    static bool TryLightAssembly(IList<Selectable> selectables, Result result)
    {
        bool hasFlatPanel = selectables.Any(s =>
        {
            string n = NameOf(s);
            return n.IndexOf("Flat Panel", StringComparison.OrdinalIgnoreCase) >= 0
                   || n.IndexOf("FP/LED", StringComparison.OrdinalIgnoreCase) >= 0;
        });
        if (hasFlatPanel)
        {
            bool tandem = selectables.Any(s =>
                NameOf(s).IndexOf("Tandem", StringComparison.OrdinalIgnoreCase) >= 0);
            var row = FindByName(tandem ? "Tandem FP/LED" : "FP/LED", "light_assembly");
            if (row != null)
            {
                Apply(row, result, includePayload: false);
                return true;
            }
        }

        foreach (var item in selectables)
        {
            string n = NameOf(item);
            if (string.IsNullOrEmpty(n))
                continue;
            var row = _rows.FirstOrDefault(r =>
                r.Category == "light_assembly"
                && n.IndexOf(r.CatalogName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (row == null)
                continue;
            Apply(row, result, includePayload: false);
            return true;
        }
        return false;
    }

    static void SumLightHeads(IList<Selectable> selectables, Result result)
    {
        double weight = 0;
        bool any = false;
        foreach (var item in selectables)
        {
            string n = NameOf(item);
            if (string.IsNullOrEmpty(n))
                continue;
            var row = _rows.FirstOrDefault(r =>
                r.Category == "light_head"
                && r.WeightKg.HasValue
                && (n.Equals(r.CatalogName, StringComparison.OrdinalIgnoreCase)
                    || n.IndexOf(r.CatalogName, StringComparison.OrdinalIgnoreCase) >= 0
                    || r.CatalogName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));
            if (row == null)
                continue;
            weight += row.WeightKg.Value;
            any = true;
        }
        if (any)
            result.OverallWeightKg = weight;
    }

    static void Apply(PartRow row, Result result, bool includePayload)
    {
        if (row.WeightKg.HasValue)
            result.OverallWeightKg = row.WeightKg;
        if (row.TorqueNm.HasValue)
            result.TorqueMomentNm = row.TorqueNm;
        if (row.VerticalForceN.HasValue)
            result.VerticalForceN = row.VerticalForceN;
        if (includePayload && row.PayloadCapacityKg.HasValue)
            result.PayloadCapacityKg = row.PayloadCapacityKg;
    }

    static PartRow FindByName(string name, string category)
        => _rows.FirstOrDefault(r =>
            r.Category == category
            && r.CatalogName.Equals(name, StringComparison.OrdinalIgnoreCase));

    static BoomConfigurationManager FindBoomConfig(IList<Selectable> selectables)
    {
        foreach (var item in selectables)
        {
            if (item == null)
                continue;
            var bcm = item.GetComponentInParent<BoomConfigurationManager>()
                      ?? item.GetComponentInChildren<BoomConfigurationManager>(true);
            if (bcm != null)
                return bcm;
        }
        return null;
    }

    static int? EstimateServiceHeadMm(IList<Selectable> selectables)
    {
        foreach (var item in selectables)
        {
            string name = NameOf(item);
            if (name.IndexOf("Service Head", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            float sizeM = item.CurrentScaleLevel?.Size
                          ?? item.CurrentPreviewScaleLevel?.Size
                          ?? 0f;
            if (sizeM > 0.05f)
                return (int)Math.Round(sizeM * 1000.0);
            var m = MmInName.Match(name);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int mm))
                return mm;
        }
        return null;
    }

    static string NameOf(Selectable item)
    {
        if (item == null)
            return "";
        return item.GetMetadata()?.Name ?? item.UIButtonName ?? item.name ?? "";
    }

    static void EnsureLoaded()
    {
        if (_loadAttempted)
            return;
        _loadAttempted = true;
        _rows = new List<PartRow>();

        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Data", "elevation-engineering-parts.csv");
            if (!File.Exists(path))
                return;

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("catalog_name", StringComparison.OrdinalIgnoreCase))
                    continue;
                var cols = ParseCsvLine(line);
                if (cols.Count < 7)
                    continue;
                var row = new PartRow
                {
                    CatalogName = cols[0].Trim(),
                    SizeKey = cols[1].Trim(),
                    Category = cols[2].Trim(),
                    WeightKg = ParseDouble(cols[3]),
                    TorqueNm = ParseDouble(cols[4]),
                    VerticalForceN = ParseDouble(cols[5]),
                    PayloadCapacityKg = ParseDouble(cols[6])
                };
                if (!string.IsNullOrEmpty(row.CatalogName))
                    _rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"ElevationEngineeringCalculator: {ex.Message}");
        }
    }

    static double? ParseDouble(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v
            : (double?)null;
    }

    static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var cur = new System.Text.StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (c == ',' && !inQuotes)
            {
                result.Add(cur.ToString());
                cur.Clear();
                continue;
            }
            cur.Append(c);
        }
        result.Add(cur.ToString());
        return result;
    }
}
