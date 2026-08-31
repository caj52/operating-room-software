using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persistent bridge between the app catalog (Boom_GroupPricing) and an optional
/// supplier Excel used only to refresh prices. Manual fills live here too.
/// Bridge + supplier copies live under persistentDataPath so builds can write them;
/// the catalog Excel stays in StreamingAssets.
/// </summary>
[Serializable]
public class PricingBridgeState
{
    public string supplierFileName;
    public string lastImportUtc;
    public string lastImportSummary;
    public List<PricingBridgeItem> items = new();
}

[Serializable]
public class PricingBridgeItem
{
    public string sheetName;
    public string partNumber;
    /// <summary>Catalog configuration column when present (e.g. "7000 - LED / LED").</summary>
    public string configurationName;
    public string objectName;
    public string size;
    public double catalogPrice;
    public double effectivePrice;
    /// <summary>Catalog | Supplier | Manual</summary>
    public string source;
    public string supplierMatch;
    public bool missingFromSupplier;
}

public static class PricingBridgeStore
{
    const string FileName = "pricing_bridge.json";

    public static event Action Changed;

    public static string QuotesDirectory =>
        Path.Combine(Application.streamingAssetsPath, "Data", "quotes");

    /// <summary>Writable pricing data root (bridge JSON + supplier Excel copies).</summary>
    public static string DataDirectory =>
        Path.Combine(Application.persistentDataPath, "Pricing");

    public static string SupplierDirectory =>
        Path.Combine(DataDirectory, "supplier");

    public static string StatePath => Path.Combine(DataDirectory, FileName);

    public static string CatalogFileName => DataFilePaths.ExcelFileNameForLightAndBoomPricing;

    public static string CatalogPath => Path.Combine(QuotesDirectory, CatalogFileName);

    static PricingBridgeState _state;
    static Dictionary<string, PricingBridgeItem> _byKey;

    public static PricingBridgeState State
    {
        get
        {
            if (_state == null)
                Load();
            return _state;
        }
    }

    public static void NotifyChanged() => Changed?.Invoke();

    public static void Load()
    {
        _state = new PricingBridgeState();
        _byKey = new Dictionary<string, PricingBridgeItem>(StringComparer.OrdinalIgnoreCase);
        MigrateLegacyStreamingAssetsIfNeeded();

        if (!File.Exists(StatePath))
            return;

        try
        {
            string json = File.ReadAllText(StatePath);
            var loaded = JsonUtility.FromJson<PricingBridgeState>(json);
            if (loaded != null)
                _state = loaded;
            RebuildIndex();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to load pricing bridge state: {ex.Message}");
            UI_DialogPrompt.Open(
                "Could not load saved pricing adjustments. They may need to be imported again.\n\n" + ex.Message);
        }
    }

    /// <summary>
    /// Older builds wrote bridge files next to the catalog in StreamingAssets.
    /// Copy once into persistentDataPath.
    /// </summary>
    static void MigrateLegacyStreamingAssetsIfNeeded()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(SupplierDirectory);

            string legacyState = Path.Combine(QuotesDirectory, FileName);
            if (!File.Exists(StatePath) && File.Exists(legacyState))
                File.Copy(legacyState, StatePath, overwrite: false);

            string legacySupplier = Path.Combine(QuotesDirectory, "supplier");
            if (!Directory.Exists(legacySupplier))
                return;

            foreach (var file in Directory.GetFiles(legacySupplier))
            {
                string dest = Path.Combine(SupplierDirectory, Path.GetFileName(file));
                if (!File.Exists(dest))
                    File.Copy(file, dest, overwrite: false);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Pricing bridge migrate skipped: {ex.Message}");
        }
    }

    public static void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        if (_state == null) _state = new PricingBridgeState();
        string json = JsonUtility.ToJson(_state, true);
        File.WriteAllText(StatePath, json);
        RebuildIndex();
    }

    public static void ReplaceItems(List<PricingBridgeItem> items, string supplierFileName, string summary)
    {
        _state ??= new PricingBridgeState();
        var previousManual = new Dictionary<string, PricingBridgeItem>(StringComparer.OrdinalIgnoreCase);
        if (_state.items != null)
        {
            foreach (var item in _state.items)
            {
                if (item != null && string.Equals(item.source, "Manual", StringComparison.OrdinalIgnoreCase))
                    previousManual[Key(item.sheetName, item.objectName, item.size)] = item;
            }
        }

        foreach (var item in items)
        {
            string key = Key(item.sheetName, item.objectName, item.size);
            if (previousManual.TryGetValue(key, out var manual)
                && item.missingFromSupplier
                && !string.Equals(item.source, "Supplier", StringComparison.OrdinalIgnoreCase))
            {
                item.effectivePrice = manual.effectivePrice;
                item.source = "Manual";
                item.missingFromSupplier = false;
                item.supplierMatch = string.IsNullOrEmpty(manual.supplierMatch)
                    ? "Manual entry"
                    : manual.supplierMatch;
            }
        }

        _state.items = items;
        _state.supplierFileName = supplierFileName;
        _state.lastImportUtc = DateTime.UtcNow.ToString("o");
        _state.lastImportSummary = summary;
        Save();
    }

    public static void SetManualPrice(PricingBridgeItem item, double price)
    {
        if (item == null) return;
        item.effectivePrice = price;
        item.source = "Manual";
        item.missingFromSupplier = false;
        if (string.IsNullOrEmpty(item.supplierMatch))
            item.supplierMatch = "Manual entry";
        Save();
        PricingManager.Instance?.ClearCache();
        PricingManager.RebuildPricingFromTrackedObjects(forceReload: true);
        NotifyChanged();
    }

    public static bool TryGetOverride(string sheetName, string objectName, string size, out double price)
    {
        price = 0;
        EnsureIndex();
        if (_byKey.TryGetValue(Key(sheetName, objectName, size), out var item)
            && IsOverrideSource(item.source))
        {
            price = item.effectivePrice;
            return true;
        }

        if (!string.IsNullOrEmpty(size)
            && _byKey.TryGetValue(Key(sheetName, objectName, null), out item)
            && IsOverrideSource(item.source))
        {
            price = item.effectivePrice;
            return true;
        }

        return false;
    }

    static bool IsOverrideSource(string source) =>
        string.Equals(source, "Supplier", StringComparison.OrdinalIgnoreCase)
        || string.Equals(source, "Manual", StringComparison.OrdinalIgnoreCase);

    public static string SupplierStatusSummary
    {
        get
        {
            var state = State;
            if (string.IsNullOrEmpty(state.supplierFileName))
                return "No spreadsheet loaded yet";

            int missing = 0;
            if (state.items != null)
            {
                foreach (var i in state.items)
                    if (i != null && i.missingFromSupplier) missing++;
            }

            string when = "";
            if (!string.IsNullOrEmpty(state.lastImportUtc)
                && DateTime.TryParse(state.lastImportUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var utc))
            {
                when = "\nLast loaded: " + utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            return $"{state.supplierFileName}{when}\nStill missing a price: {missing}";
        }
    }

    public static string SupplierFilePath
    {
        get
        {
            var name = State.supplierFileName;
            if (string.IsNullOrEmpty(name)) return null;
            return Path.Combine(SupplierDirectory, name);
        }
    }

    static void RebuildIndex()
    {
        _byKey = new Dictionary<string, PricingBridgeItem>(StringComparer.OrdinalIgnoreCase);
        if (_state?.items == null) return;
        foreach (var item in _state.items)
        {
            if (item == null) continue;
            _byKey[Key(item.sheetName, item.objectName, item.size)] = item;
        }
    }

    static void EnsureIndex()
    {
        if (_state == null) Load();
        if (_byKey == null) RebuildIndex();
    }

    public static string Key(string sheet, string name, string size) =>
        $"{sheet ?? ""}|{name ?? ""}|{NormalizeSize(size)}";

    public static string NormalizeSize(string size)
    {
        if (string.IsNullOrWhiteSpace(size)) return "";
        var chars = new List<char>(size.Length);
        foreach (char ch in size.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) chars.Add(ch);
        }
        return new string(chars.ToArray());
    }

    public static bool HasActiveOverrides()
    {
        var state = State;
        if (state?.items == null) return false;
        foreach (var item in state.items)
        {
            if (item == null) continue;
            if (string.Equals(item.source, "Supplier", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.source, "Manual", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
