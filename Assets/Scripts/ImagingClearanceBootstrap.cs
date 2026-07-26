using System;
using UnityEngine;

/// <summary>
/// Ensures specialty imaging selectables get a clearance ring when the prefab
/// never had <see cref="ClearanceLinesRenderer"/> wired (Allia, Omni/PET-CT, Azurion, etc.).
/// Prefer calling before the GO is renamed to a GUID so prefab/catalog names still match.
/// </summary>
public static class ImagingClearanceBootstrap
{
    public static void EnsureFor(Selectable selectable)
    {
        if (selectable == null) return;

        if (selectable.GetComponentInParent<ClearanceLinesRenderer>() != null
            || selectable.GetComponent<ClearanceLinesRenderer>() != null)
            return;

        // Prefer one ring on the assembly root selectable when possible.
        if (selectable.transform.parent != null)
        {
            var parentSel = selectable.transform.parent.GetComponentInParent<Selectable>();
            if (parentSel != null && parentSel != selectable
                && TryResolveType(parentSel, out _))
                return;
        }

        if (!TryResolveType(selectable, out ClearanceLinesRenderer.RendererType type))
            return;

        var renderer = selectable.gameObject.AddComponent<ClearanceLinesRenderer>();
        renderer.Type = type;
    }

    public static bool TryResolveType(Selectable selectable, out ClearanceLinesRenderer.RendererType type)
    {
        type = default;
        if (selectable == null) return false;
        return TryResolveType(selectable.gameObject, selectable, out type);
    }

    public static bool TryResolveType(GameObject go, out ClearanceLinesRenderer.RendererType type)
    {
        type = default;
        if (go == null) return false;
        go.TryGetComponent(out Selectable sel);
        return TryResolveType(go, sel, out type);
    }

    private static bool TryResolveType(GameObject go, Selectable sel, out ClearanceLinesRenderer.RendererType type)
    {
        type = default;
        if (go == null) return false;

        // Blob: live name + UI label + catalog metadata name (survives GUID rename if MetaData set).
        string blob = go.name ?? "";
        if (sel != null)
        {
            if (!string.IsNullOrEmpty(sel.UIButtonName))
                blob += " " + sel.UIButtonName;
            if (sel.MetaData != null && !string.IsNullOrEmpty(sel.MetaData.Name))
                blob += " " + sel.MetaData.Name;
            if (!string.IsNullOrEmpty(sel.GUID))
                blob += " " + sel.GUID;
        }

        if (Contains(blob, "Allia"))
        {
            type = ClearanceLinesRenderer.RendererType.Allia;
            return true;
        }

        if (Contains(blob, "omnictscan") || Contains(blob, "Omni CT")
            || Contains(blob, "PET-CT") || Contains(blob, "PET CT") || Contains(blob, "PET/CT"))
        {
            type = ClearanceLinesRenderer.RendererType.PetCTscan;
            return true;
        }

        if (Contains(blob, "Azurion"))
        {
            type = ClearanceLinesRenderer.RendererType.Azurion;
            return true;
        }

        // C-Arm / C90 / Dura: Azurion-style circular footprint as a reasonable default.
        if (Contains(blob, "CArm") || Contains(blob, "C-Arm") || Contains(blob, "OEC")
            || Contains(blob, "Diagnost") || Contains(blob, "Diagnostic"))
        {
            type = ClearanceLinesRenderer.RendererType.Azurion;
            return true;
        }

        return false;
    }

    private static bool Contains(string haystack, string needle)
    {
        return !string.IsNullOrEmpty(haystack)
            && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
