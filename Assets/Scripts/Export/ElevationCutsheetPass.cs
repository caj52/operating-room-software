using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Cutsheet elevation overlays — explicit allow-list only.
///
/// Intent (one dim per intent, always labeled):
/// - Catalog lengths: exactly one ToOrigin callout per length-owning Selectable
///   (ScaleLevel.Size → mm).
/// - Floor clearances: exactly one Floor callout per source Selectable that opts in
///   via ShowInElevationPhoto (duplicate mm collapsed; proximal arm floors skipped
///   by Measurable.ShouldSkipElevationFloorDim).
/// - Never activate Walls/Ceiling. Never leave a dim line without an mm label.
/// </summary>
public static class ElevationCutsheetPass
{
    public static void SuppressAllOverlays()
    {
        foreach (var mt in Object.FindObjectsByType<MeasurementText>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mt != null)
                mt.gameObject.SetActive(false);
        }

        foreach (var measurer in Object.FindObjectsByType<Measurer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            DisableMeasurerFully(measurer);
        }
    }

    /// <summary>
    /// Kill imperial leftovers only. Do NOT require MeasurementText to be parented under
    /// Measurer — labels live on a shared canvas, so GetComponentInParent would null
    /// and wipe every valid mm label (blank elevation regression).
    /// </summary>
    public static void SuppressNonMetricTexts()
    {
        foreach (var mt in Object.FindObjectsByType<MeasurementText>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mt == null || !mt.gameObject.activeSelf)
                continue;

            string t = mt.Text != null ? mt.Text.text : null;
            if (string.IsNullOrEmpty(t))
            {
                mt.gameObject.SetActive(false);
                continue;
            }

            bool hasMm = t.IndexOf("mm", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasImperial = t.IndexOf('\'') >= 0;
            if (hasImperial && !hasMm)
                mt.gameObject.SetActive(false);
        }
    }

    public static float Apply(IList<Selectable> assemblySelectables, Camera camera)
    {
        Measurable.BeginElevationMeasurementPass();
        Measurable.ClearCutsheetAssemblyBounds();
        SuppressAllOverlays();

        float heightMod = 0.1f;
        if (assemblySelectables == null)
            return heightMod;

        // Prepare links / scale binding once (also walks nested Size owners so
        // BoomSegment_3.001 under BoomSegment_3(Clone) gets ToOrigin before curation).
        foreach (var root in assemblySelectables)
        {
            if (root == null)
                continue;
            foreach (var sel in root.GetComponentsInChildren<Selectable>(true))
            {
                if (sel == null)
                    continue;
                sel.EnsureCurrentScaleLevelFromCatalog();
                sel.EnsureMeasurablesLinked();
            }
        }

        // Geometry-only AABB for layout — dims must sit outside this box.
        if (TryComputeAssemblyGeometryBounds(assemblySelectables, out Bounds asmBounds))
        {
            Measurable.SetCutsheetAssemblyBounds(asmBounds);
            Debug.Log(
                $"[ElevDim] Assembly geometry bounds center={asmBounds.center} size={asmBounds.size}");
        }

        // --- Curate: one length dim per length owner, one floor dim per source ---
        var lengthByOwner = new Dictionary<Selectable, (Measurable measurable, float sizeM)>();
        var floorBySource = new Dictionary<Selectable, Measurable>();

        foreach (var sel in assemblySelectables)
        {
            float sizeM = ElevationLengthFormat.ResolveOwnSizeMeters(sel);

            // Size owners with empty Measurables lists are recovered in the borrow pass.
            if (sel?.Measurables == null || sel.Measurables.Count == 0)
                continue;

            foreach (var measurable in sel.Measurables)
            {
                // Length curation: ShowInElevationPhoto preferred, but Size + ToOrigin is
                // enough — borrow/draw gates handle hosts that cleared the flag.
                if (measurable == null || measurable.Disabled)
                    continue;
                if (!measurable.ShowInElevationPhoto
                    && !(sizeM > 0f && HasToOriginType(measurable)))
                    continue;

                bool hasToOrigin = measurable.Measurements != null
                    && measurable.Measurements.Any(m =>
                        m != null && m.MeasurementType == MeasurementType.ToArmAssemblyOrigin);
                bool hasFloor = measurable.Measurements != null
                    && measurable.Measurements.Any(m =>
                        m != null && m.MeasurementType == MeasurementType.Floor);

                // Also accept MeasurementTypes when Measurements not yet initialized.
                if (!hasToOrigin && measurable.MeasurementTypes != null
                    && measurable.MeasurementTypes.Contains(MeasurementType.ToArmAssemblyOrigin))
                    hasToOrigin = true;
                if (!hasFloor && measurable.MeasurementTypes != null
                    && measurable.MeasurementTypes.Contains(MeasurementType.Floor))
                    hasFloor = true;

                if (hasToOrigin && sizeM > 0f)
                {
                    // One catalog length per length owner — never stack dual-select copies.
                    // Also skip if this Measurable is already claimed (dual-select shares one).
                    bool measurableClaimed = false;
                    foreach (var existing in lengthByOwner.Values)
                    {
                        if (existing.measurable == measurable)
                        {
                            measurableClaimed = true;
                            break;
                        }
                    }
                    if (!measurableClaimed
                        && (!lengthByOwner.TryGetValue(sel, out var existingOwner)
                            || sizeM > existingOwner.sizeM))
                        lengthByOwner[sel] = (measurable, sizeM);
                }

                if (hasFloor)
                {
                    // One floor ray per source selectable. Skip decision happens at draw time.
                    if (!floorBySource.ContainsKey(sel))
                        floorBySource[sel] = measurable;
                }
            }
        }

        // Dual-select Size owner with empty Measurables: find ToOrigin without relying on
        // RelatedSelectables (often broken when a light hangs under a boom mount).
        foreach (var sel in assemblySelectables)
        {
            if (sel == null || lengthByOwner.ContainsKey(sel))
                continue;
            float sizeM = ElevationLengthFormat.ResolveOwnSizeMeters(sel);
            if (sizeM <= 0f)
                continue;
            // Row-tier Size on service heads is not a tube/arm length dim.
            if (sel.GetComponent<BoomHeadScaleHandler>() != null)
                continue;

            Measurable borrowed = FindToOriginForSizeOwner(sel, lengthByOwner, assemblySelectables);
            if (borrowed == null)
            {
                // Re-link then install if still missing (e.g. neck under a service head used
                // to skip ToOrigin when BoomHeadScaleHandler was searched in children).
                sel.EnsureMeasurablesLinked();
                borrowed = FindToOriginForSizeOwner(sel, lengthByOwner, assemblySelectables);
            }
            if (borrowed == null)
            {
                var installed = sel.GetComponent<Measurable>();
                if (installed == null)
                    installed = sel.gameObject.AddComponent<Measurable>();
                installed.EnsureConfiguredAsCatalogLength();
                sel.EnsureMeasurablesLinked();
                if (sel.Measurables != null && !sel.Measurables.Contains(installed))
                    sel.Measurables.Add(installed);
                borrowed = installed;
                Debug.Log(
                    $"[ElevDim] installed ToOrigin during cutsheet for Size owner={sel.name} " +
                    $"mm={Mathf.RoundToInt(sizeM * 1000f)}",
                    sel);
            }

            if (sel.Measurables != null && !sel.Measurables.Contains(borrowed))
                sel.Measurables.Add(borrowed);

            lengthByOwner[sel] = (borrowed, sizeM);
            Debug.Log(
                $"[ElevDim] borrowed ToOrigin for Size owner={sel.name} mm={Mathf.RoundToInt(sizeM * 1000f)} " +
                $"from measurable={borrowed.name} host={borrowed.transform.name}",
                sel);
        }

        // Nested Size owners (drop tubes, BoomSegment_3 neck) under assembly roots.
        foreach (var root in assemblySelectables)
        {
            if (root == null) continue;
            foreach (var sel in root.GetComponentsInChildren<Selectable>(true))
            {
                if (sel == null || lengthByOwner.ContainsKey(sel))
                    continue;
                sel.EnsureCurrentScaleLevelFromCatalog();
                float sizeM = ElevationLengthFormat.ResolveOwnSizeMeters(sel);
                if (sizeM <= 0f)
                    continue;
                // Skip service-head row tiers (Size is not tube/arm length).
                if (sel.GetComponent<BoomHeadScaleHandler>() != null)
                    continue;

                sel.EnsureMeasurablesLinked();
                Measurable borrowed = FindToOriginForSizeOwner(sel, lengthByOwner, assemblySelectables);
                if (borrowed == null)
                {
                    var installed = sel.GetComponent<Measurable>();
                    if (installed == null)
                        installed = sel.gameObject.AddComponent<Measurable>();
                    installed.EnsureConfiguredAsCatalogLength();
                    sel.EnsureMeasurablesLinked();
                    if (sel.Measurables != null && !sel.Measurables.Contains(installed))
                        sel.Measurables.Add(installed);
                    borrowed = installed;
                }
                if (sel.Measurables != null && !sel.Measurables.Contains(borrowed))
                    sel.Measurables.Add(borrowed);
                lengthByOwner[sel] = (borrowed, sizeM);
                Debug.Log(
                    $"[ElevDim] nested Size curated name={sel.name} mm={Mathf.RoundToInt(sizeM * 1000f)} " +
                    $"via {borrowed.name}",
                    sel);
            }
        }

        // Dual-select twins (Clone / .001) can both carry Size + ToOrigin → two dims for
        // one physical part, or one twin claiming the Measurable and starving the other.
        // Industry: one dimension per part. Keep the best Size owner per stem.
        DedupeLengthOwnersByStem(lengthByOwner);

        var sb = new StringBuilder(256);
        sb.Append("[ElevDim] Cutsheet curated lengths=").Append(lengthByOwner.Count)
          .Append(" floors=").Append(floorBySource.Count);
        foreach (var kv in lengthByOwner.OrderByDescending(k => k.Value.sizeM))
        {
            sb.Append(" | LEN ").Append(kv.Key.name)
              .Append("→").Append(kv.Value.measurable.name)
              .Append(" mm=").Append(Mathf.RoundToInt(kv.Value.sizeM * 1000f));
        }
        foreach (var kv in floorBySource)
        {
            sb.Append(" | FLR ").Append(kv.Key.name)
              .Append("→").Append(kv.Value.name);
        }
        Debug.Log(sb.ToString());

        int drewLength = 0;
        int drewFloor = 0;
        int horizLane = 0;
        int vertLane = 0;

        // Per-owner layout (not on Measurable alone — dual-select / linked measurables
        // can share one component and overwrite V→H, shoving tube dims into the elbow).
        var layoutByOwner = new Dictionary<Selectable, (bool vertical, float side, float lift)>();
        foreach (var kv in lengthByOwner)
        {
            var sel = kv.Key;
            float horiz = kv.Value.measurable.EstimateProximalHorizontalSpan(sel);
            bool vertical = Measurable.ClassifyCutsheetLengthVertical(sel, kv.Value.sizeM, horiz);
            float side;
            float lift;
            if (vertical)
            {
                // Extra clearance beyond AssemblyClearPad — stacked per vertical dim.
                side = 0.28f + vertLane * 0.22f;
                lift = 0f;
                vertLane++;
            }
            else
            {
                // Extra clearance beyond assembly top/bottom — stacked per arm dim.
                side = 0f;
                lift = 0.28f + horizLane * 0.22f;
                horizLane++;
            }
            layoutByOwner[sel] = (vertical, side, lift);
        }

        Debug.Log(
            $"[ElevDim] Cutsheet layout horizLanes={horizLane} vertLanes={vertLane} " +
            string.Join(" ", layoutByOwner.Select(kv =>
            {
                float upDot = 0f;
                var axis = kv.Key.transform.TransformDirection(Vector3.forward);
                if (axis.sqrMagnitude > 1e-8f)
                    upDot = Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up));
                return $"{kv.Key.name}:{(kv.Value.vertical ? "V" : "H")}" +
                       $" upDot={upDot:F2}" +
                       $" side={kv.Value.side:F2}" +
                       $" lift={kv.Value.lift:F2}";
            })));

        foreach (var kv in lengthByOwner.OrderByDescending(k => k.Value.sizeM))
        {
            var measurable = kv.Value.measurable;
            var layout = layoutByOwner[kv.Key];
            // Re-apply immediately before draw so shared Measurable state can't stick as H.
            measurable.CutsheetLengthIsVertical = layout.vertical;
            measurable.CutsheetLayoutSideMeters = layout.side;
            measurable.CutsheetLayoutLiftMeters = layout.lift;
            // Pin curated own Size before apply — Apply must not re-resolve via RelatedSelectables.
            measurable.CutsheetLengthOwner = kv.Key;
            measurable.CutsheetCatalogLengthMeters = kv.Value.sizeM;
            measurable.EnsureInitializedForElevation();
            measurable.ApplyCutsheetElevation(ref heightMod, camera, kv.Key,
                drawLength: true, drawFloor: false);

            if (CountActive(measurable, MeasurementType.ToArmAssemblyOrigin) > 0)
                drewLength++;
        }

        foreach (var kv in floorBySource)
        {
            var measurable = kv.Value;
            measurable.CutsheetLengthOwner = kv.Key;
            measurable.EnsureInitializedForElevation();
            measurable.ApplyCutsheetElevation(ref heightMod, camera, kv.Key,
                drawLength: false, drawFloor: true);

            if (CountActive(measurable, MeasurementType.Floor) > 0)
                drewFloor++;
        }

        // Upstream contract: a dim line without an mm label must not exist.
        int orphansKilled = EnforceLabeledDimsOnly();
        SuppressNonMetricTexts();

        Debug.Log(
            $"[ElevDim] Cutsheet pass done drewLength={drewLength} drewFloor={drewFloor} " +
            $"orphansKilled={orphansKilled} (unlabeled lines stripped)");

        if (drewLength == 0)
        {
            Debug.LogWarning(
                "[ElevDim] Cutsheet drew ZERO catalog length dims — expect missing arm/tube mm. " +
                "Check curated LEN list and ScaleLevel bind warnings.");
        }

        return heightMod;
    }

    public static void SuppressNonCutsheetTexts()
    {
        EnforceLabeledDimsOnly();
        SuppressNonMetricTexts();
    }

    /// <summary>
    /// Any active elev measurer must have a visible mm label. Otherwise kill the whole
    /// dim (mesh + leaders + text) — this is what created "lines with no values".
    /// </summary>
    static int EnforceLabeledDimsOnly()
    {
        int killed = 0;
        foreach (var measurer in Object.FindObjectsByType<Measurer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (measurer == null)
                continue;

            if (!Selectable.IsInElevationPhotoMode)
                continue;

            bool leadersOn = false;
            if (measurer.LineRenderers != null)
            {
                foreach (var lr in measurer.LineRenderers)
                {
                    if (lr != null && lr.enabled)
                    {
                        leadersOn = true;
                        break;
                    }
                }
            }
            bool bodyOn = measurer.ElevationBodyLineEnabled;
            var mt = measurer.MeasurementText;
            bool textGoOn = mt != null && mt.gameObject.activeSelf;

            // Inactive measurers can still leave an enabled body line in the RT.
            if (!measurer.gameObject.activeSelf && !leadersOn && !bodyOn && !textGoOn)
                continue;

            bool allowed = measurer.gameObject.activeSelf && measurer.ShouldDrawInElevationPhoto();
            string dist = measurer.Distance;
            bool hasMm = !string.IsNullOrEmpty(dist)
                && dist.IndexOf("mm", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool textOn = textGoOn && hasMm;

            // Valid cutsheet dim with mm distance but text GO briefly off — revive label,
            // do not strip leaders (that blanked every elevation after SuppressNonMetricTexts).
            if (allowed && hasMm && (leadersOn || bodyOn || textGoOn))
            {
                if (mt != null && !mt.gameObject.activeSelf)
                    mt.gameObject.SetActive(true);
                continue;
            }

            if (allowed && textOn)
                continue;

            if (measurer.gameObject.activeSelf || leadersOn || bodyOn || textGoOn)
            {
                killed++;
                if (killed <= 8)
                {
                    Debug.LogWarning(
                        $"[ElevDim] orphan dim killed type={measurer.Measurement?.MeasurementType} " +
                        $"allowed={allowed} textOn={textOn} hasMm={hasMm} dist='{Truncate(dist, 24)}' " +
                        $"leadersOn={leadersOn} bodyOn={bodyOn} — unlabeled or non-cutsheet");
                }
                DisableMeasurerFully(measurer);
            }
        }
        return killed;
    }

    static void DisableMeasurerFully(Measurer measurer)
    {
        if (measurer == null)
            return;
        measurer.DisableElevationVisuals();
        measurer.gameObject.SetActive(false);
        if (measurer.MeasurementText != null)
            measurer.MeasurementText.gameObject.SetActive(false);
        if (measurer.LineRenderers == null)
            return;
        foreach (var lr in measurer.LineRenderers)
        {
            if (lr != null)
                lr.enabled = false;
        }
    }

    /// <summary>
    /// After PDF capture: kill every overlay left in the live scene so cutsheet
    /// dims do not remain as "gizmos" on the room.
    /// </summary>
    public static void EndCaptureCleanup()
    {
        SuppressAllOverlays();
        Measurable.ClearCutsheetAssemblyBounds();
        foreach (var measurable in Object.FindObjectsByType<Measurable>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (measurable == null)
                continue;
            measurable.ClearCutsheetElevationState();
        }

        // Second pass after IsActive cleared — shared-canvas labels can linger if anything
        // briefly re-enabled a Measurer during teardown.
        SuppressAllOverlays();
    }

    static string DualSelectStem(string name) => Measurable.DualSelectStem(name);

    /// <summary>
    /// One catalog length dim per dual-select stem (Clone/.001 share a stem).
    /// Prefers the twin that has own mesh bounds, then larger Size.
    /// </summary>
    static void DedupeLengthOwnersByStem(
        Dictionary<Selectable, (Measurable measurable, float sizeM)> lengthByOwner)
    {
        if (lengthByOwner == null || lengthByOwner.Count < 2)
            return;

        var groups = lengthByOwner.Keys
            .Where(s => s != null)
            .GroupBy(s => DualSelectStem(s.name))
            .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1)
            .ToList();

        foreach (var g in groups)
        {
            Selectable keep = g
                .OrderByDescending(s => Measurable.TryGetOwnRendererBounds(s, out _) ? 1 : 0)
                .ThenByDescending(s => lengthByOwner[s].sizeM)
                .ThenBy(s => s.name)
                .First();
            foreach (var s in g)
            {
                if (s == keep) continue;
                Debug.Log(
                    $"[ElevDim] Deduped dual-select stem={g.Key}: keep={keep.name} " +
                    $"drop={s.name} mm={Mathf.RoundToInt(lengthByOwner[s].sizeM * 1000f)}");
                lengthByOwner.Remove(s);
            }
        }
    }

    /// <summary>
    /// Union of assembly mesh bounds (each selectable's own + dual-select twins).
    /// Used as the silhouette dims must clear — not for tick span.
    /// </summary>
    static bool TryComputeAssemblyGeometryBounds(IList<Selectable> assembly, out Bounds bounds)
    {
        bounds = default;
        if (assembly == null)
            return false;

        bool any = false;
        var seen = new HashSet<Selectable>();
        foreach (var sel in assembly)
        {
            if (sel == null || !seen.Add(sel))
                continue;
            if (!Measurable.TryGetOwnRendererBounds(sel, out Bounds b))
                continue;
            if (!any)
            {
                bounds = b;
                any = true;
            }
            else
                bounds.Encapsulate(b);
        }
        return any;
    }

    static bool HasToOriginType(Measurable m)
    {
        if (m == null)
            return false;
        if (m.MeasurementTypes != null
            && m.MeasurementTypes.Contains(MeasurementType.ToArmAssemblyOrigin))
            return true;
        return m.Measurements != null
            && m.Measurements.Any(x =>
                x != null && x.MeasurementType == MeasurementType.ToArmAssemblyOrigin);
    }

    /// <summary>
    /// Find a ToOrigin Measurable for a Size owner whose Measurables list is empty.
    /// Searches parents (Clone hosts Measurable), same-stem assembly twins, then Related.
    /// </summary>
    static Measurable FindToOriginForSizeOwner(
        Selectable sel,
        Dictionary<Selectable, (Measurable measurable, float sizeM)> lengthByOwner,
        IList<Selectable> assemblySelectables)
    {
        if (sel == null)
            return null;

        string stem = DualSelectStem(sel.name);

        bool TryTake(Measurable m, out Measurable taken)
        {
            taken = null;
            // Cutsheet borrow: do not require ShowInElevationPhoto — dual-select hosts
            // sometimes clear that flag while MeasurementTypes still has ToOrigin.
            if (m == null || m.Disabled || !HasToOriginType(m))
                return false;
            foreach (var existing in lengthByOwner.Values)
            {
                if (existing.measurable == m)
                    return false;
            }
            // Never steal a descendant Size-owner's ToOrigin (BoomDropTube used to claim
            // an arm Measurable via GetComponentsInChildren → misaligned 100mm ticks).
            var nearest = m.GetComponentInParent<Selectable>(true);
            if (nearest != null && nearest != sel
                && DualSelectStem(nearest.name) != stem
                && ElevationLengthFormat.ResolveOwnSizeMeters(nearest) > 0f)
                return false;
            taken = m;
            return true;
        }

        // Prefer own / same-stem measurables before walking all descendants.
        foreach (var m in sel.GetComponents<Measurable>())
        {
            if (TryTake(m, out var taken))
                return taken;
        }
        foreach (var m in sel.GetComponentsInChildren<Measurable>(true))
        {
            if (TryTake(m, out var taken))
                return taken;
        }

        // Highest same-stem ancestor (Clone) — Measurable often lives there while Size is on .001.
        // Also covers sibling dual-select under a shared mount wrapper.
        Selectable stemRoot = sel;
        for (Transform t = sel.transform.parent; t != null; t = t.parent)
        {
            if (!t.TryGetComponent(out Selectable ancestor))
                continue;
            if (DualSelectStem(ancestor.name) == stem)
            {
                stemRoot = ancestor;
                continue;
            }
            if (ElevationLengthFormat.ResolveOwnSizeMeters(ancestor) > 0f
                && DualSelectStem(ancestor.name) != stem)
                break;
        }

        if (stemRoot != sel)
        {
            foreach (var m in stemRoot.GetComponents<Measurable>())
            {
                if (TryTake(m, out var taken))
                    return taken;
            }
            foreach (var m in stemRoot.GetComponentsInChildren<Measurable>(true))
            {
                if (TryTake(m, out var taken))
                    return taken;
            }
        }

        // Parent chain components (Measurable on intermediate non-Selectable GO).
        for (Transform t = sel.transform.parent; t != null; t = t.parent)
        {
            foreach (var m in t.GetComponents<Measurable>())
            {
                if (TryTake(m, out var taken))
                    return taken;
            }

            if (t.TryGetComponent(out Selectable ancestor)
                && ancestor != sel
                && ElevationLengthFormat.ResolveOwnSizeMeters(ancestor) > 0f
                && DualSelectStem(ancestor.name) != stem)
                break;
        }

        if (assemblySelectables != null && !string.IsNullOrEmpty(stem))
        {
            foreach (var other in assemblySelectables)
            {
                if (other == null || other == sel)
                    continue;
                if (DualSelectStem(other.name) != stem)
                    continue;
                if (lengthByOwner.ContainsKey(other))
                    continue;

                if (other.Measurables != null)
                {
                    foreach (var m in other.Measurables)
                    {
                        if (TryTake(m, out var taken))
                            return taken;
                    }
                }

                foreach (var m in other.GetComponentsInChildren<Measurable>(true))
                {
                    if (TryTake(m, out var taken))
                        return taken;
                }

                foreach (var m in other.GetComponents<Measurable>())
                {
                    if (TryTake(m, out var taken))
                        return taken;
                }
            }
        }

        if (sel.RelatedSelectables != null)
        {
            foreach (var rel in sel.RelatedSelectables)
            {
                if (rel == null || rel == sel || lengthByOwner.ContainsKey(rel))
                    continue;
                if (rel.Measurables != null)
                {
                    foreach (var m in rel.Measurables)
                    {
                        if (TryTake(m, out var taken))
                            return taken;
                    }
                }
                foreach (var m in rel.GetComponentsInChildren<Measurable>(true))
                {
                    if (TryTake(m, out var taken))
                        return taken;
                }
            }
        }

        return null;
    }

    static int CountActive(Measurable measurable, MeasurementType type)
    {
        if (measurable?.Measurements == null)
            return 0;
        int n = 0;
        foreach (var m in measurable.Measurements)
        {
            if (m?.Measurer == null || m.MeasurementType != type)
                continue;
            if (m.Measurer.gameObject.activeSelf)
                n++;
        }
        return n;
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
