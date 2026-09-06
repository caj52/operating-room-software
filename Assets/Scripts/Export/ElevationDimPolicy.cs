using UnityEngine;

/// <summary>
/// Cutsheet ownership policy: what length/floor dims to draw.
/// Endpoint math is ElevationDimSolver.
/// Catalog: every OrderedSelectables LengthTube with own Size or primary PdfData
/// (includes Ceiling Tube / DropTube rows — e.g. lights 150 mm, boom flange 100 mm).
/// Floor: host of a Floor measurable (not accessory / mid-arm / drop tube).
/// Geometric joints (Cardanic): not cutsheet length dims.
/// </summary>
public static class ElevationDimPolicy
{
    public enum Role
    {
        Ignore,
        CatalogLength,
        FloorClearance
    }

    public readonly struct Claim
    {
        public readonly Selectable Owner;
        public readonly Role Role;
        public readonly float LengthM;
        public readonly string Source;

        public Claim(Selectable owner, Role role, float lengthM, string source)
        {
            Owner = owner;
            Role = role;
            LengthM = lengthM;
            Source = source ?? "?";
        }
    }

    /// <summary>
    /// Own catalog meters only — Size on this selectable, else primary PdfData.
    /// Does not walk RelatedSelectables (display/PDF helpers may still do that).
    /// </summary>
    public static bool TryGetOwnCatalogLengthMeters(
        Selectable sel, out float meters, out string source)
    {
        meters = 0f;
        source = null;
        if (sel == null)
            return false;
        if (sel.GetLengthScaleKind() != LengthScaleKind.LengthTube)
            return false;
        if (Measurable.IsServiceHeadAccessoryDimOwner(sel))
            return false;

        float sizeM = ElevationLengthFormat.ResolveOwnSizeMeters(sel);
        if (sizeM > 0f)
        {
            meters = sizeM;
            source = "Size";
            return true;
        }

        float pdfM = ElevationLengthFormat.ResolvePdfDataLengthMeters(sel);
        if (pdfM > 0f)
        {
            meters = pdfM;
            source = "PdfData";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Dual-select: claim the twin that actually carries Size/PdfData.
    /// </summary>
    public static bool TryResolveCatalogLengthClaim(Selectable row, out Claim claim)
    {
        claim = default;
        if (row == null)
            return false;

        Selectable owner = ResolveLengthOwningTwin(row);
        if (!TryGetOwnCatalogLengthMeters(owner, out float meters, out string source))
            return false;

        claim = new Claim(owner, Role.CatalogLength, meters, source);
        return true;
    }

    public static bool IsCatalogLengthOwner(Selectable sel) =>
        TryGetOwnCatalogLengthMeters(sel, out _, out _);

    /// <summary>
    /// Hosts a Floor measurable and is an allowed clearance product (not tube/accessory).
    /// </summary>
    public static bool IsFloorClearanceHost(Selectable sel)
    {
        if (sel == null)
            return false;
        if (Measurable.LooksLikeGuidSelectable(sel))
            return false;
        if (Measurable.IsServiceHeadAccessoryDimOwner(sel))
            return false;
        if (Measurable.IsSkippedMidArmFloorName(sel))
            return false;
        if (Measurable.IsDropTubeName(sel.name))
            return false;

        // Structural product heads — components / authored Floor measurable.
        if (sel.GetComponentInChildren<BoomHeadScaleHandler>(true) != null)
            return true;
        if (sel.GetComponent<LightFactory>() != null)
            return true;
        if (Measurable.IsLightProductFloorOwner(sel))
            return true;
        // Service-head cabinet prefab — always a floor clearance product.
        string n = sel.name ?? "";
        if (n.IndexOf("NewBoomHead", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Prefab-authored Floor measurable (e.g. ArmMonitorMount Measurable_ToFloor).
        return HasFloorMeasurable(sel);
    }

    public static bool HasFloorMeasurable(Selectable sel)
    {
        if (sel == null)
            return false;

        if (sel.Measurables != null)
        {
            foreach (var m in sel.Measurables)
            {
                if (IsFloorMeasurable(m))
                    return true;
            }
        }

        foreach (var m in sel.GetComponentsInChildren<Measurable>(true))
        {
            if (!IsFloorMeasurable(m))
                continue;
            var nearest = m.GetComponentInParent<Selectable>(true);
            // Nested product head owns that Floor — outer shell must not claim it.
            if (nearest != null && nearest != sel
                && (nearest.GetComponent<BoomHeadScaleHandler>() != null
                    || nearest.GetComponent<LightFactory>() != null
                    || Measurable.IsLightProductFloorOwner(nearest)))
                continue;
            return true;
        }

        return false;
    }

    static bool IsFloorMeasurable(Measurable m)
    {
        if (m == null || m.Disabled)
            return false;
        if (m.name != null
            && (m.name.IndexOf("ToFloor", System.StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(m.name, "floor", System.StringComparison.OrdinalIgnoreCase)))
            return true;
        if (m.MeasurementTypes != null
            && m.MeasurementTypes.Contains(MeasurementType.Floor))
            return true;
        return m.Measurements != null
            && m.Measurements.Exists(x =>
                x != null && x.MeasurementType == MeasurementType.Floor);
    }

    /// <summary>
    /// Stable floor-head key for dedupe — one dim per hanging product.
    /// </summary>
    public static int FloorHeadKey(Selectable sel)
    {
        if (sel == null)
            return 0;
        if (Measurable.TryGetLightProductFloorRoot(sel, out Transform light)
            && light != null)
            return light.GetInstanceID();
        if (sel.GetComponent<BoomHeadScaleHandler>() != null)
            return sel.GetInstanceID();
        if (Measurable.TryGetServiceHeadFloorRoot(sel, out Transform sh) && sh != null)
            return sh.GetInstanceID();

        // Dual-select monitor / carrier twins share one floor dim.
        if (sel.RelatedSelectables != null && sel.RelatedSelectables.Count > 0
            && sel.RelatedSelectables[0] != null)
            return sel.RelatedSelectables[0].GetInstanceID();

        return sel.GetInstanceID();
    }

    /// <summary>
    /// Prefer component-backed heads when several selectables share one FloorHeadKey.
    /// </summary>
    public static int FloorHostRank(Selectable sel)
    {
        if (sel == null)
            return 0;
        if (sel.name != null
            && sel.name.IndexOf("LightHead", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return 100;
        if (sel.GetComponent<BoomHeadScaleHandler>() != null)
            return 90;
        if (sel.GetComponent<LightFactory>() != null)
            return 80;
        if (HasFloorMeasurable(sel))
            return 50;
        return 10;
    }

    static Selectable ResolveLengthOwningTwin(Selectable sel)
    {
        if (sel == null)
            return null;
        if (TryGetOwnCatalogLengthMeters(sel, out _, out _))
            return sel;

        Selectable best = sel;
        float bestM = 0f;
        if (sel.RelatedSelectables == null)
            return sel;

        string stem = Measurable.DualSelectStem(sel.name);
        foreach (var rel in sel.RelatedSelectables)
        {
            if (rel == null)
                continue;
            if (!string.IsNullOrEmpty(stem)
                && Measurable.DualSelectStem(rel.name) != stem)
                continue;
            if (!TryGetOwnCatalogLengthMeters(rel, out float m, out _))
                continue;
            if (m > bestM)
            {
                bestM = m;
                best = rel;
            }
        }
        return best;
    }
}
