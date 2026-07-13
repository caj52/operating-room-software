using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SimpleJSON;
using UnityEngine;

/// <summary>
/// Fills elevation engineering rows when known; otherwise XXX.
/// Priority: existing Fields → PdfData → known CSV samples → circuits/med-gas from scene → JSON seed → XXX.
/// </summary>
public static class ElevationEngineeringResolver
{
    public const string Placeholder = "XXX";

    public enum AssemblyKind
    {
        Unknown,
        LightFamily,
        BoomServiceHead
    }

    static readonly string[] MedGasTypes =
    {
        "Ohmeda", "DISS", "Chemetron", "Puritan", "Schrader", "DIN", "Amico"
    };

    static readonly Regex CircuitsInName = new(
        @"(\d+)\s*(?:x\s*)?circuits?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static Dictionary<string, Dictionary<string, string>> _jsonSeeds;
    static bool _jsonLoadAttempted;

    public static IReadOnlyList<string> RequiredLabelsForKind(AssemblyKind kind)
    {
        switch (kind)
        {
            case AssemblyKind.LightFamily:
                // Carlyn light tables use "Vertical Force" (not "Vertical Force Nm").
                return new[]
                {
                    "Circuits Required",
                    "Overall Weight",
                    "Torque Moment",
                    "Vertical Force"
                };
            case AssemblyKind.BoomServiceHead:
                return new[]
                {
                    "Circuits Required",
                    "Med-Gas Connection Type",
                    "Overall Weight",
                    "Vertical Force",
                    "Payload Capacity"
                };
            default:
                return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Classify from table title and/or the assembly's selectables.
    /// Titles from export are metadata Names (e.g. product names), not always the legacy literals.
    /// </summary>
    public static AssemblyKind Classify(string tableName, IList<Selectable> selectables)
    {
        if (!string.IsNullOrWhiteSpace(tableName))
        {
            if (LooksLikeBoomServiceHead(tableName))
                return AssemblyKind.BoomServiceHead;
            if (LooksLikeLightFamily(tableName))
                return AssemblyKind.LightFamily;
        }

        if (selectables == null || selectables.Count == 0)
            return AssemblyKind.Unknown;

        bool boomish = false;
        bool lightish = false;
        foreach (var item in selectables)
        {
            if (item == null)
                continue;
            var meta = item.GetMetadata();
            string name = meta?.Name ?? item.UIButtonName ?? item.name ?? "";
            if (LooksLikeBoomServiceHead(name) || HasBoomServiceCategory(meta))
                boomish = true;
            if (LooksLikeLightFamily(name) || HasLightFamilyCategory(meta))
                lightish = true;
        }

        // Service-head assemblies often also include arms; prefer boom when both signal.
        if (boomish)
            return AssemblyKind.BoomServiceHead;
        if (lightish)
            return AssemblyKind.LightFamily;
        return AssemblyKind.Unknown;
    }

    public static bool IsEngineeringKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;
        string k = key.Trim();
        return k.Equals("Circuits Required", StringComparison.OrdinalIgnoreCase)
               || k.Equals("Overall Weight", StringComparison.OrdinalIgnoreCase)
               || k.Equals("Torque Moment", StringComparison.OrdinalIgnoreCase)
               || k.Equals("Vertical Force", StringComparison.OrdinalIgnoreCase)
               || k.Equals("Vertical Force Nm", StringComparison.OrdinalIgnoreCase) // legacy label
               || k.Equals("Med-Gas Connection Type", StringComparison.OrdinalIgnoreCase)
               || k.Equals("Payload Capacity", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Appends any missing required engineering rows onto <paramref name="assembly"/>.
    /// </summary>
    public static void Enrich(PdfExporterLocal.AssemblyJson assembly, IList<Selectable> selectables)
    {
        if (assembly == null)
            return;

        assembly.Fields ??= new List<PdfExporterLocal.PdfField>();
        var kind = Classify(assembly.TableName, selectables);
        var required = RequiredLabelsForKind(kind);
        if (required.Count == 0)
            return;

        var pdfLookup = BuildPdfDataLookup(selectables);
        var calc = ElevationEngineeringCalculator.TryCompute(kind, selectables);

        // Drop legacy light label so Carlyn-style "Vertical Force" is the only VF row.
        if (kind == AssemblyKind.LightFamily)
        {
            assembly.Fields.RemoveAll(f =>
                f != null
                && f.Item != null
                && f.Item.Trim().Equals("Vertical Force Nm", StringComparison.OrdinalIgnoreCase));
        }

        foreach (string label in required)
        {
            var matches = assembly.Fields
                .Where(f => f != null && string.Equals(f.Item, label, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var good = matches.FirstOrDefault(f => HasRealValue(f.Value));
            if (good != null)
            {
                foreach (var dup in matches)
                {
                    if (!ReferenceEquals(dup, good))
                        assembly.Fields.Remove(dup);
                }
                continue;
            }

            string value = ResolveValue(label, kind, assembly.TableName, selectables, pdfLookup, calc);
            if (string.IsNullOrWhiteSpace(value))
            {
                // Boom circuits only appear when we can derive them (Carlyn tandem pages omit them).
                if (label.Equals("Circuits Required", StringComparison.OrdinalIgnoreCase)
                    && kind == AssemblyKind.BoomServiceHead)
                    continue;
                value = Placeholder;
            }

            if (matches.Count > 0)
            {
                matches[0].Value = value;
                for (int i = 1; i < matches.Count; i++)
                    assembly.Fields.Remove(matches[i]);
            }
            else
            {
                assembly.Fields.Add(new PdfExporterLocal.PdfField { Item = label, Value = value });
            }
        }
    }

    static bool HasRealValue(string value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.Trim().Equals(Placeholder, StringComparison.OrdinalIgnoreCase);

    static string ResolveValue(
        string label,
        AssemblyKind kind,
        string tableName,
        IList<Selectable> selectables,
        Dictionary<string, string> pdfLookup,
        ElevationEngineeringCalculator.Result calc)
    {
        if (pdfLookup != null
            && pdfLookup.TryGetValue(NormalizeKey(label), out string fromPdf)
            && !string.IsNullOrWhiteSpace(fromPdf))
            return fromPdf.Trim();

        if (calc != null)
        {
            if (label.Equals("Overall Weight", StringComparison.OrdinalIgnoreCase)
                && calc.OverallWeightKg.HasValue)
                return $"{calc.OverallWeightKg.Value:0.#} kg";

            if (label.Equals("Torque Moment", StringComparison.OrdinalIgnoreCase)
                && calc.TorqueMomentNm.HasValue
                && kind == AssemblyKind.LightFamily)
                return $"{calc.TorqueMomentNm.Value:0.##} Nm";

            // Carlyn samples use "Vertical Force" for lights and booms (not "Vertical Force Nm").
            if (label.Equals("Vertical Force", StringComparison.OrdinalIgnoreCase)
                && calc.VerticalForceN.HasValue
                && (kind == AssemblyKind.LightFamily || kind == AssemblyKind.BoomServiceHead))
                return $"{calc.VerticalForceN.Value:0.#} N";

            if (label.Equals("Vertical Force Nm", StringComparison.OrdinalIgnoreCase)
                && kind == AssemblyKind.LightFamily
                && calc.VerticalForceN.HasValue)
                return $"{calc.VerticalForceN.Value:0.#} N";

            // Boom Payload Capacity = remaining net when known (Carlyn); never print max arm tables here.
            if (label.Equals("Payload Capacity", StringComparison.OrdinalIgnoreCase)
                && calc.PayloadCapacityKg.HasValue
                && kind == AssemblyKind.BoomServiceHead)
                return $"{calc.PayloadCapacityKg.Value:0.#} kg";
        }

        if (label.Equals("Circuits Required", StringComparison.OrdinalIgnoreCase))
        {
            string derived = DeriveCircuits(kind, selectables);
            if (!string.IsNullOrWhiteSpace(derived))
                return derived;
        }

        if (label.Equals("Med-Gas Connection Type", StringComparison.OrdinalIgnoreCase))
        {
            string derived = DeriveMedGas(selectables);
            if (!string.IsNullOrWhiteSpace(derived))
                return derived;
        }

        string fromJson = LookupJsonSeed(tableName, label);
        if (!string.IsNullOrWhiteSpace(fromJson))
            return fromJson.Trim();

        if (label.Equals("Circuits Required", StringComparison.OrdinalIgnoreCase)
            && kind == AssemblyKind.LightFamily)
            return "1";

        return null;
    }

    static bool LooksLikeBoomServiceHead(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return text.IndexOf("Boom - Service Head", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("Boom Service Head", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("MediBoom", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("Anesthesia Boom", StringComparison.OrdinalIgnoreCase) >= 0
               || (text.IndexOf("Service Head", StringComparison.OrdinalIgnoreCase) >= 0
                   && text.IndexOf("Attachment", StringComparison.OrdinalIgnoreCase) < 0
                   && text.IndexOf("Shelf", StringComparison.OrdinalIgnoreCase) < 0
                   && text.IndexOf("Rail", StringComparison.OrdinalIgnoreCase) < 0);
    }

    static bool LooksLikeLightFamily(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return text.Equals("Flat Panel Arm", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Lights - U | ONE", StringComparison.OrdinalIgnoreCase)
               || text.Equals("Spring Arm (Low Ceiling)", StringComparison.OrdinalIgnoreCase)
               || text.IndexOf("Flat Panel", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("Spring Arm", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("U | ONE", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("U|ONE", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("U | 002", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("U|002", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("Sim.LED", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("SimLED", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool HasBoomServiceCategory(SelectableMetaData meta)
    {
        if (meta?.Categories == null)
            return false;
        return meta.Categories.Any(c =>
            !string.IsNullOrEmpty(c) && (
                c.IndexOf("Boom - Service Head", StringComparison.OrdinalIgnoreCase) >= 0
                || c.IndexOf("Boom - SH", StringComparison.OrdinalIgnoreCase) >= 0));
    }

    static bool HasLightFamilyCategory(SelectableMetaData meta)
    {
        if (meta?.Categories == null)
            return false;
        return meta.Categories.Any(c =>
            !string.IsNullOrEmpty(c) && (
                c.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0
                || c.IndexOf("Flat Panel", StringComparison.OrdinalIgnoreCase) >= 0
                || c.IndexOf("Spring Arm", StringComparison.OrdinalIgnoreCase) >= 0));
    }

    static string DeriveCircuits(AssemblyKind kind, IList<Selectable> selectables)
    {
        bool? boomDropdownOnly = kind switch
        {
            AssemblyKind.BoomServiceHead => true,
            AssemblyKind.LightFamily => false,
            _ => (bool?)null
        };

        foreach (var state in DropdownPopulator.GetAllCurrentStates())
        {
            if (state.Item1 == null || state.Item2 == null)
                continue;
            if (boomDropdownOnly.HasValue
                && state.Item1.isBoomExcelFileDropDown != boomDropdownOnly.Value)
                continue;

            string name = state.Item2.ObjectName;
            if (string.IsNullOrWhiteSpace(name) || IsNone(name))
                continue;
            var m = CircuitsInName.Match(name);
            if (m.Success)
                return m.Groups[1].Value;
        }

        int duplexes = CountDuplexOutlets(selectables);
        if (duplexes > 0)
        {
            // Carlyn sample: Red Duplex (8) → Circuits Required 4.
            int circuits = Math.Max(1, (int)Math.Ceiling(duplexes / 2.0));
            return circuits.ToString(CultureInfo.InvariantCulture);
        }

        if (kind == AssemblyKind.LightFamily)
            return "1";

        return null;
    }

    static string DeriveMedGas(IList<Selectable> selectables)
    {
        foreach (var state in DropdownPopulator.GetAllCurrentStates())
        {
            if (state.Item1 == null || !state.Item1.isBoomExcelFileDropDown)
                continue;
            string hit = FindMedGasToken(state.Item2?.ObjectName);
            if (hit != null)
                return hit;
        }

        if (selectables == null)
            return null;

        foreach (var item in selectables)
        {
            if (item == null)
                continue;
            var meta = item.GetMetadata();
            string hit = FindMedGasToken(meta?.Name)
                         ?? FindMedGasToken(item.UIButtonName)
                         ?? FindMedGasToken(item.name);
            if (hit != null)
                return hit;
        }

        return null;
    }

    static string FindMedGasToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        foreach (string type in MedGasTypes)
        {
            if (text.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (type.Equals("Amico", StringComparison.OrdinalIgnoreCase))
                    continue;
                return type;
            }
        }
        return null;
    }

    static int CountDuplexOutlets(IList<Selectable> selectables)
    {
        if (selectables == null)
            return 0;

        int count = 0;
        foreach (var item in selectables)
        {
            if (item == null)
                continue;
            string name = item.GetMetadata()?.Name ?? item.UIButtonName ?? item.name ?? "";
            if (name.IndexOf("Duplex", StringComparison.OrdinalIgnoreCase) >= 0)
                count++;
        }

        return count;
    }

    static Dictionary<string, string> BuildPdfDataLookup(IList<Selectable> selectables)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (selectables == null)
            return map;

        foreach (var item in selectables)
        {
            var meta = item?.GetMetadata();
            if (meta?.PdfData == null)
                continue;

            foreach (var pdf in meta.PdfData)
            {
                if (pdf == null || string.IsNullOrWhiteSpace(pdf.Key) || !IsEngineeringKey(pdf.Key))
                    continue;
                string value = (pdf.Value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(value)
                    || value.Equals("{NAME}", StringComparison.OrdinalIgnoreCase)
                    || value.Equals(Placeholder, StringComparison.OrdinalIgnoreCase))
                    continue;
                string key = NormalizeKey(pdf.Key);
                if (!map.ContainsKey(key))
                    map[key] = value;
            }
        }

        return map;
    }

    static string NormalizeKey(string key) => (key ?? string.Empty).Trim();

    static bool IsNone(string name)
        => string.IsNullOrWhiteSpace(name)
           || name.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);

    static string LookupJsonSeed(string tableName, string label)
    {
        EnsureJsonSeeds();
        if (_jsonSeeds == null || string.IsNullOrEmpty(tableName))
            return null;

        if (_jsonSeeds.TryGetValue(tableName, out var exact)
            && exact != null
            && exact.TryGetValue(label, out string v)
            && !string.IsNullOrWhiteSpace(v))
            return v;

        foreach (var kvp in _jsonSeeds)
        {
            if (string.IsNullOrEmpty(kvp.Key))
                continue;
            if (tableName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) < 0
                && kvp.Key.IndexOf(tableName, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (kvp.Value != null
                && kvp.Value.TryGetValue(label, out string soft)
                && !string.IsNullOrWhiteSpace(soft))
                return soft;
        }

        return null;
    }

    static void EnsureJsonSeeds()
    {
        if (_jsonLoadAttempted)
            return;
        _jsonLoadAttempted = true;
        _jsonSeeds = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Data", "elevation-engineering.json");
            if (!File.Exists(path))
                return;

            string text = File.ReadAllText(path);
            var root = JSON.Parse(text);
            if (root == null || !root.IsObject)
                return;

            foreach (var table in root.Linq)
            {
                if (table.Value == null || !table.Value.IsObject)
                    continue;
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in table.Value.Linq)
                {
                    if (field.Value == null)
                        continue;
                    string val = field.Value.Value;
                    if (!string.IsNullOrWhiteSpace(val))
                        fields[field.Key] = val.Trim();
                }
                if (fields.Count > 0)
                    _jsonSeeds[table.Key] = fields;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"ElevationEngineeringResolver: failed to load elevation-engineering.json: {ex.Message}");
        }
    }
}
