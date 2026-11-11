using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Customer-facing configuration name. Prices stay on the resolver rows;
/// this only chooses the words printed on the proposal.
/// </summary>
public static class ProposalListing
{
    public static string Describe(
        IEnumerable<SelectablePrice> lights,
        GameObject boomRoot,
        IEnumerable<SelectablePrice> booms,
        IReadOnlyList<ProposalPricingResolver.Line> boomLines)
    {
        string boom = DescribeBoom(boomRoot, booms, boomLines);
        string light = LightPhrase(lights, article: true);
        if (!string.IsNullOrEmpty(boom) && !string.IsNullOrEmpty(light))
            return $"{boom} with {light}";
        if (!string.IsNullOrEmpty(boom))
            return boom;
        if (!string.IsNullOrEmpty(light))
            return light;
        return "Configuration";
    }

    /// <summary>
    /// Selected size and count win. Catalog ranges and internal tags are dropped.
    /// </summary>
    public static string CleanExtra(string description, string selectedSize, int count)
    {
        string desc = (description ?? "").Trim();
        desc = Regex.Replace(desc, @"\s*\(?\s*1st\s+Position\s*\)?", "", RegexOptions.IgnoreCase);
        desc = desc.Trim(' ', '-', ',');

        if (desc.IndexOf("flange", StringComparison.OrdinalIgnoreCase) >= 0
            && !string.IsNullOrWhiteSpace(selectedSize))
        {
            string size = selectedSize.Trim();
            desc = Regex.Replace(desc, @"\d+(\.\d+)?\s*mm", "", RegexOptions.IgnoreCase);
            desc = Regex.Replace(desc, @"\s{2,}", " ").Trim(' ', '-', ',');
            if (desc.IndexOf(size, StringComparison.OrdinalIgnoreCase) < 0)
                desc = $"{size} {desc}".Trim();
        }

        if (count > 1
            && desc.IndexOf("shelf", StringComparison.OrdinalIgnoreCase) >= 0
            && !Regex.IsMatch(desc, $@"\b{count}\b"))
        {
            desc = desc.Replace("Shelf", "Shelves").Replace("shelf", "shelves");
            desc = $"{count} {desc}";
        }

        desc = Regex.Replace(desc, @"\s{2,}", " ").Trim();
        return desc;
    }

    static string DescribeBoom(
        GameObject boomRoot,
        IEnumerable<SelectablePrice> booms,
        IReadOnlyList<ProposalPricingResolver.Line> boomLines)
    {
        bool anyBoom = boomLines != null && boomLines.Count > 0;
        if (!anyBoom)
            return "";

        string family = FamilyFromLines(boomLines);
        if (string.IsNullOrEmpty(family))
            family = PlacedBoomName(booms);

        bool tandem = BlobHas(booms, "tandem") || BlobHas(boomRoot, "tandem");
        string core;
        if (string.IsNullOrEmpty(family))
            core = "Equipment Boom";
        else if (family.IndexOf("boom", StringComparison.OrdinalIgnoreCase) >= 0)
            core = family;
        else
            core = $"Equipment Boom ({family})";
        return tandem ? "Tandem " + core : core;
    }

    /// <summary>
    /// Sizes she selected. Catalog ranges and head-mix codes are left off;
    /// the heads themselves are named by <see cref="LightPhrase"/>.
    /// </summary>
    public static string LightChoices(IEnumerable<SelectablePrice> lights)
    {
        if (lights == null)
            return "";

        var labels = new List<string>();
        foreach (var sp in lights.Where(sp => sp != null)
                     .OrderBy(sp => ProposalPDFGenerator.GetHierarchyPath(sp.transform)))
        {
            if (HeadCode(sp.pricingObjectName) != null || HeadCode(sp.UIObjectName) != null)
                continue;

            string size = sp.objectPricingData?.ObjectSize;
            string name = sp.objectPricingData?.ObjectName ?? sp.pricingObjectName ?? "";
            string label = ChoiceLabel(size, name);
            if (!string.IsNullOrWhiteSpace(label))
                labels.Add(label.Trim());
        }

        return string.Join(", ", labels.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static string LightPhrase(IEnumerable<SelectablePrice> lights, bool article)
    {
        if (lights == null)
            return "";

        var heads = new List<string>();
        string family = "";
        var counted = new List<Transform>();
        foreach (var sp in lights.Where(sp => sp != null)
                     .OrderBy(sp => ProposalPDFGenerator.GetHierarchyPath(sp.transform)))
        {
            string blob = PartBlob(sp);
            if (string.IsNullOrWhiteSpace(blob))
                continue;

            if (string.IsNullOrEmpty(family))
                family = ProductToken(blob);

            bool underCounted = false;
            var walk = sp.transform;
            while (walk != null)
            {
                if (counted.Contains(walk))
                {
                    underCounted = true;
                    break;
                }
                walk = walk.parent;
            }
            if (underCounted)
                continue;

            string head = HeadCode(sp.pricingObjectName);
            if (head == null)
                head = HeadCode(sp.UIObjectName);
            if (head == null && IsStructuralMount(blob))
                continue;
            if (head == null)
                head = PlacedName(sp);
            if (head == null)
                continue;

            heads.Add(head);
            counted.Add(sp.transform);
        }

        if (heads.Count == 0)
            return "";

        bool shortHeads = heads.TrueForAll(h => h.Length <= 6);
        string mix = shortHeads ? string.Join("/", heads) : string.Join(" and ", heads);
        string named = string.IsNullOrEmpty(family) || !shortHeads ? mix : $"{family} {mix}";
        if (heads.Count == 2 && shortHeads)
            return article ? "a Dual " + named : "Dual " + named;
        if (heads.Count == 1 && shortHeads)
            return article ? "a " + named : named;
        return named;
    }

    /// <summary>Product mark as printed, whatever the series is called.</summary>
    static string ProductToken(string blob)
    {
        if (string.IsNullOrWhiteSpace(blob))
            return "";
        var match = Regex.Match(blob, @"U\s*\|\s*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return "";
        return "U|" + match.Groups[1].Value.ToUpperInvariant();
    }

    /// <summary>Name shown for the piece that was placed, skipping spreadsheet ranges and mesh names.</summary>
    static string PlacedName(SelectablePrice sp)
    {
        if (sp == null)
            return null;
        var selectable = sp.GetComponent<Selectable>();
        string raw = FirstFilled(
            selectable != null ? selectable.UIButtonName : null,
            sp.UIObjectName,
            sp.pricingObjectName);
        raw = CleanWords(raw);
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 40 || IsHeadCode(raw) || HasMeasurement(raw))
            return null;
        return raw;
    }

    static string PlacedBoomName(IEnumerable<SelectablePrice> booms)
    {
        if (booms == null)
            return "";
        string best = null;
        foreach (var sp in booms)
        {
            string name = PlacedName(sp);
            if (string.IsNullOrEmpty(name) || IsAccessory(name))
                continue;
            if (name.IndexOf("boom", StringComparison.OrdinalIgnoreCase) >= 0)
                return name;
            if (best == null || name.Length < best.Length)
                best = name;
        }
        return best ?? "";
    }

    /// <summary>
    /// Ceiling cover, tube, arm, and flange are how the light is hung.
    /// They are not the light.
    /// </summary>
    public static bool IsStructuralMount(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        string n = name.ToLowerInvariant();
        if (n.Contains("flat panel") || n.Contains("u|") || n.Contains("u |"))
            return false;
        return n.Contains("cover")
               || n.Contains("ceiling tube")
               || n.Contains("flange")
               || n.Contains("arm");
    }

    static bool IsAccessory(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("shelf") || n.Contains("duplex") || n.Contains("flange")
               || n.Contains("outlet") || n.Contains("rail") || n.Contains("cover")
               || n.Contains("gas") || n.Contains("nitrogen");
    }

    static string FirstFilled(params string[] values)
    {
        if (values == null)
            return "";
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return "";
    }

    static string CleanWords(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        string n = raw.Trim().Replace('_', ' ');
        n = Regex.Replace(n, @"\s*\(Clone\)\s*", "", RegexOptions.IgnoreCase);
        if (n.StartsWith("Boom - ", StringComparison.OrdinalIgnoreCase))
            n = n.Substring("Boom - ".Length);
        if (n.StartsWith("Lights - ", StringComparison.OrdinalIgnoreCase))
            n = n.Substring("Lights - ".Length);
        return Regex.Replace(n, @"\s{2,}", " ").Trim();
    }

    static string ChoiceLabel(string size, string name)
    {
        bool coded = IsHeadCode(name);
        bool otherMeasurement = HasMeasurement(name)
            && (string.IsNullOrWhiteSpace(size)
                || name.IndexOf(size.Trim(), StringComparison.OrdinalIgnoreCase) < 0);
        if (!string.IsNullOrWhiteSpace(size) && (coded || otherMeasurement))
            return size.Trim();
        if (coded)
            return "";
        if (!string.IsNullOrWhiteSpace(size) && !HasMeasurement(name))
            return size.Trim();
        return (name ?? "").Trim();
    }

    static bool IsHeadCode(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (name.IndexOf("pole", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return Regex.IsMatch(name, @"\b(LED|FP|LS)\s*/\s*(LED|FP|LS)\b", RegexOptions.IgnoreCase);
    }

    static bool HasMeasurement(string name)
    {
        return Regex.IsMatch(name ?? "", @"\d+(\.\d+)?\s*mm", RegexOptions.IgnoreCase);
    }

    static string HeadCode(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (GetAttachedObjects.IsFlatPanelPricingName(name))
            return "FP";
        if (name.IndexOf("lead", StringComparison.OrdinalIgnoreCase) >= 0
            && name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0)
            return "LS";
        if (GetAttachedObjects.IsSurgicalLightPricingName(name))
            return "LED";
        return null;
    }

    static string FamilyFromLines(IReadOnlyList<ProposalPricingResolver.Line> boomLines)
    {
        if (boomLines == null)
            return "";
        foreach (var line in boomLines)
        {
            string d = line?.Description;
            if (string.IsNullOrWhiteSpace(d))
                continue;
            int comma = d.IndexOf(',');
            if (comma > 0)
                d = d.Substring(0, comma).Trim();
            if (d.Length > 0 && d.Length <= 40
                && !d.Equals("ARTICULATING BOOM", StringComparison.OrdinalIgnoreCase))
                return d;
        }
        return "";
    }

    static bool BlobHas(IEnumerable<SelectablePrice> parts, string token)
    {
        if (parts == null)
            return false;
        foreach (var sp in parts)
        {
            if (PartBlob(sp).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    static bool BlobHas(GameObject root, string token)
    {
        if (root == null)
            return false;
        var prices = root.GetComponentsInChildren<SelectablePrice>(true);
        return BlobHas(prices, token);
    }

    static string PartBlob(SelectablePrice sp)
    {
        if (sp == null)
            return "";
        return string.Join(" ",
            sp.pricingObjectName ?? "",
            sp.UIObjectName ?? "",
            sp.gameObject != null ? sp.gameObject.name : "");
    }
}
