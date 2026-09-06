using System.Collections.Generic;
using System.Linq;
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
            if (mt == null)
                continue;
            mt.HideElevOverlays();
            mt.gameObject.SetActive(false);
        }

        // Orphan plates (sibling under canvas, not destroyed with a disabled label).
        var canvasGo = GameObject.Find("UI_WorldspaceText");
        if (canvasGo != null)
        {
            for (int i = canvasGo.transform.childCount - 1; i >= 0; i--)
            {
                var child = canvasGo.transform.GetChild(i);
                if (child != null && child.name == "ElevTextBacking")
                    child.gameObject.SetActive(false);
            }
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
        ElevationDimPlacement.BeginCutsheetPass();
        ElevationLengthGeometry.ClearClaimEndpointCache();
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
        }

        // --- Curate: catalog table rows + floors ---
        var lengthByOwner = ElevationDimCurator.BuildLengthOwners(assemblySelectables);
        var floorBySource = new Dictionary<Selectable, Measurable>();

        foreach (var sel in assemblySelectables)
        {
            if (sel == null || !ElevationDimPolicy.IsFloorClearanceHost(sel))
                continue;
            if (floorBySource.ContainsKey(sel))
                continue;

            Measurable floorM = FindFloorMeasurableUnder(sel)
                ?? EnsureFloorMeasurableForProductHead(sel);
            if (floorM == null)
            {
                Debug.LogWarning(
                    $"[ElevDim] floor product={sel.name} could not get a Floor Measurable",
                    sel);
                continue;
            }

            floorBySource[sel] = floorM;
        }

        DedupeLengthOwnersByStem(lengthByOwner);
        DedupeLengthOwnersBySharedMeasurable(lengthByOwner);
        DedupeFloorOwnersByHead(floorBySource);

        int drewLength = 0;
        int drewFloor = 0;

        // Per-owner layout (not on Measurable alone — dual-select / linked measurables
        // can share one component and overwrite V→H, shoving tube dims into the elbow).
        var layoutByOwner = AssignCutsheetLanes(lengthByOwner);
        AssignHorizontalLabelStack(lengthByOwner, layoutByOwner);

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

        // LightHead / Simeon before logos or catalog shells — Player.log: Arm/GUID placed
        // first, stole Sim.LED.LightHead id, real light floor never stuck.
        foreach (var kv in floorBySource.OrderByDescending(k => FloorOwnerPriority(k.Key)))
        {
            var measurable = kv.Value;
            if (measurable == null)
            {
                Debug.LogWarning(
                    $"[ElevDim] FLOOR APPLY null measurable owner={kv.Key?.name}",
                    kv.Key);
                continue;
            }

            measurable.CutsheetLengthOwner = kv.Key;
            measurable.EnsureConfiguredAsElevationFloor();
            measurable.EnsureInitializedForElevation();

            bool hasFloorMeas = measurable.Measurements != null
                && measurable.Measurements.Any(m =>
                    m != null && m.MeasurementType == MeasurementType.Floor);
            bool hasFloorMeasurer = hasFloorMeas
                && measurable.Measurements.Any(m =>
                    m != null && m.MeasurementType == MeasurementType.Floor && m.Measurer != null);

            if (!hasFloorMeas)
            {
                Debug.LogWarning(
                    $"[ElevDim] FLOOR APPLY missing Floor Measurement owner={kv.Key.name} " +
                    $"measurable={measurable.name}",
                    measurable);
            }

            measurable.ApplyCutsheetElevation(ref heightMod, camera, kv.Key,
                drawLength: false, drawFloor: true);

            int active = CountActive(measurable, MeasurementType.Floor);
            if (active > 0)
                drewFloor++;
        }

        // Page-plane overlap: push dim LINES outboard only; label gap stays fixed.
        ElevationDimLayoutResolve.Apply(camera);

        // Upstream contract: a dim line without an mm label must not exist.
        EnforceLabeledDimsOnly();
        SuppressNonMetricTexts();

        ElevationChecklistDiagnostics.StashForScore(lengthByOwner, floorBySource);

        if (drewLength == 0)
        {
            Debug.LogWarning(
                "[ElevDim] Cutsheet drew ZERO catalog length dims — expect missing arm/tube mm. " +
                "Check curated LEN list and ScaleLevel bind warnings.");
        }

        if (drewFloor == 0 && floorBySource.Count > 0)
        {
            Debug.LogWarning(
                "[ElevDim] Cutsheet curated floors but drewFloor=0 — check FLOOR SKIP lines.");
        }

        return heightMod;
    }

    /// <summary>
    /// Initial place: every length dim starts at the same near offset.
    /// <see cref="ElevationDimLayoutResolve"/> stacks outward only when page-rects collide.
    /// </summary>
    static Dictionary<Selectable, (bool vertical, float side, float lift)> AssignCutsheetLanes(
        Dictionary<Selectable, (Measurable measurable, float sizeM)> lengthByOwner)
    {
        var layoutByOwner = new Dictionary<Selectable, (bool vertical, float side, float lift)>();
        if (lengthByOwner == null || lengthByOwner.Count == 0)
            return layoutByOwner;

        float near = ElevationDimPlacement.CutsheetLaneBaseMeters;

        foreach (var kv in lengthByOwner.OrderByDescending(k => k.Value.sizeM))
        {
            var sel = kv.Key;
            if (sel == null || kv.Value.measurable == null)
                continue;
            float horiz = kv.Value.measurable.EstimateProximalHorizontalSpan(sel);
            bool vertical = Measurable.ClassifyCutsheetLengthVertical(sel, kv.Value.sizeM, horiz);
            if (vertical)
                layoutByOwner[sel] = (true, near, 0f);
            else
                layoutByOwner[sel] = (false, 0f, near);
        }

        return layoutByOwner;
    }

    /// <summary>
    /// Stacked horizontal arms: highest labels above, lowest below.
    /// Prevents both numbers floating into the gap (reads as 600/1000 swapped on the PDF).
    /// </summary>
    static void AssignHorizontalLabelStack(
        Dictionary<Selectable, (Measurable measurable, float sizeM)> lengthByOwner,
        Dictionary<Selectable, (bool vertical, float side, float lift)> layoutByOwner)
    {
        if (lengthByOwner == null || layoutByOwner == null)
            return;

        var horiz = new List<(Selectable sel, Measurable m, float y, float sizeM)>();
        foreach (var kv in lengthByOwner)
        {
            if (kv.Key == null || kv.Value.measurable == null)
                continue;
            if (!layoutByOwner.TryGetValue(kv.Key, out var layout) || layout.vertical)
                continue;

            float y = kv.Key.transform.position.y;
            if (ElevationLengthGeometry.TryGetLengthMeshBounds(
                    kv.Key, kv.Value.sizeM, out Bounds b, out _))
                y = b.center.y;
            horiz.Add((kv.Key, kv.Value.measurable, y, kv.Value.sizeM));
        }

        horiz.Sort((a, b) => b.y.CompareTo(a.y));

        float ceil = ElevationDimPlacement.CeilingUndersideY();
        float hardware = ElevationDimPlacement.CeilingHardwareClearanceY();
        float topClear = Mathf.Min(ceil, hardware)
            - ElevationDimPlacement.CutsheetLaneBaseMeters
            - ElevationDimPlacement.CutsheetInFrameMarginMeters;
        bool topHasRoomAbove = horiz.Count > 0 && horiz[0].y < topClear;

        for (int i = 0; i < horiz.Count; i++)
        {
            bool? preferBelow = null;
            if (horiz.Count >= 2)
            {
                if (i == 0)
                    preferBelow = !topHasRoomAbove;
                else if (i == horiz.Count - 1)
                    preferBelow = true;
                else
                    preferBelow = i >= horiz.Count / 2;
            }

            horiz[i].m.CutsheetPreferLabelBelow = preferBelow;
        }
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
        {
            measurer.MeasurementText.HideElevOverlays();
            measurer.MeasurementText.gameObject.SetActive(false);
        }
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
    /// One catalog length dim per dual-select twin pair (Clone/.001 or Related).
    /// Separate arm instances that share a catalog name (two Sim.FLEX 800mm arms)
    /// must each keep their own dim — do not merge by stem alone.
    /// </summary>
    /// <summary>
    /// Prefer component-backed product heads when several selectables share one floor key.
    /// </summary>
    static int FloorOwnerPriority(Selectable sel) =>
        ElevationDimPolicy.FloorHostRank(sel);

    /// <summary>
    /// One floor dim per hanging product head.
    /// </summary>
    static void DedupeFloorOwnersByHead(Dictionary<Selectable, Measurable> floorBySource)
    {
        if (floorBySource == null || floorBySource.Count < 2)
            return;

        var byHead = new Dictionary<int, List<Selectable>>();
        foreach (var sel in floorBySource.Keys.ToList())
        {
            if (sel == null)
                continue;
            int headId = ElevationDimPolicy.FloorHeadKey(sel);
            if (!byHead.TryGetValue(headId, out var list))
            {
                list = new List<Selectable>();
                byHead[headId] = list;
            }
            list.Add(sel);
        }

        foreach (var group in byHead.Values)
        {
            if (group.Count < 2)
                continue;
            Selectable keep = group
                .OrderByDescending(FloorOwnerPriority)
                .ThenBy(s => s.name)
                .First();
            foreach (var s in group)
            {
                if (s == keep)
                    continue;
                floorBySource.Remove(s);
            }
        }
    }

    static void DedupeLengthOwnersByStem(
        Dictionary<Selectable, (Measurable measurable, float sizeM)> lengthByOwner)
    {
        if (lengthByOwner == null || lengthByOwner.Count < 2)
            return;

        var owners = lengthByOwner.Keys.Where(s => s != null).ToList();
        var removed = new HashSet<Selectable>();

        foreach (var a in owners)
        {
            if (a == null || removed.Contains(a) || !lengthByOwner.ContainsKey(a))
                continue;

            var group = new List<Selectable> { a };
            foreach (var b in owners)
            {
                if (b == null || b == a || removed.Contains(b) || !lengthByOwner.ContainsKey(b))
                    continue;
                if (!AreDualSelectTwins(a, b))
                    continue;
                group.Add(b);
            }

            if (group.Count < 2)
                continue;

            Selectable keep = group
                .OrderByDescending(s => Measurable.TryGetOwnRendererBounds(s, out _) ? 1 : 0)
                .ThenByDescending(s => lengthByOwner[s].sizeM)
                .ThenBy(s => s.name)
                .First();

            foreach (var s in group)
            {
                if (s == keep) continue;
                lengthByOwner.Remove(s);
                removed.Add(s);
            }
        }
    }

    /// <summary>
    /// Two Size owners pointing at the same Measurable component must not both draw
    /// (log: two LEN Sim.FLEX …001 → same name, two lanes, overlapping 1100 mm).
    /// </summary>
    static void DedupeLengthOwnersBySharedMeasurable(
        Dictionary<Selectable, (Measurable measurable, float sizeM)> lengthByOwner)
    {
        if (lengthByOwner == null || lengthByOwner.Count < 2)
            return;

        var byMeasurable = new Dictionary<Measurable, List<Selectable>>();
        foreach (var kv in lengthByOwner)
        {
            if (kv.Key == null || kv.Value.measurable == null)
                continue;
            if (!byMeasurable.TryGetValue(kv.Value.measurable, out var list))
            {
                list = new List<Selectable>();
                byMeasurable[kv.Value.measurable] = list;
            }
            list.Add(kv.Key);
        }

        foreach (var group in byMeasurable.Values)
        {
            if (group.Count < 2)
                continue;
            Selectable keep = group
                .OrderByDescending(s => lengthByOwner[s].sizeM)
                .ThenByDescending(s => Measurable.TryGetOwnRendererBounds(s, out _) ? 1 : 0)
                .ThenBy(s => s.name)
                .First();
            foreach (var s in group)
            {
                if (s == keep)
                    continue;
                lengthByOwner.Remove(s);
            }
        }
    }

    /// <summary>
    /// True Clone/.001 or RelatedSelectables pair — not merely the same catalog stem
    /// under a shared mount (two independent Sim.FLEX arms).
    /// </summary>
    static bool AreDualSelectTwins(Selectable a, Selectable b)
    {
        if (a == null || b == null || a == b)
            return false;
        if (DualSelectStem(a.name) != DualSelectStem(b.name))
            return false;
        if (a.RelatedSelectables != null && a.RelatedSelectables.Contains(b))
            return true;
        if (b.RelatedSelectables != null && b.RelatedSelectables.Contains(a))
            return true;
        if (a.transform.IsChildOf(b.transform) || b.transform.IsChildOf(a.transform))
            return true;
        // Same parent: only when that parent hosts exactly two of this stem
        // (real Clone/.001 pair). Three+ means independent arms sharing a mount.
        Transform parent = a.transform.parent;
        if (parent == null || parent != b.transform.parent)
            return false;
        string stem = DualSelectStem(a.name);
        int stemPeers = 0;
        foreach (var s in parent.GetComponentsInChildren<Selectable>(true))
        {
            if (s != null && DualSelectStem(s.name) == stem)
                stemPeers++;
        }
        return stemPeers <= 2;
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
    /// Selectable that owns a catalog length (own Size or PdfData). Not geometric joints.
    /// </summary>
    static bool IsCutsheetLengthHost(Selectable sel) =>
        ElevationDimPolicy.IsCatalogLengthOwner(sel);

    /// <summary>
    /// Link / find / force-install a ToOrigin for a catalog length owner (Size or PdfData).
    /// Prefer <see cref="Selectable.Measurables"/> — EnsureMeasurablesLinked may claim a
    /// child ToOrigin that FindToOrigin's stem/Size guard then rejects.
    /// </summary>
    public static Measurable EnsureToOriginForLengthOwner(
        Selectable sel,
        Dictionary<Selectable, (Measurable measurable, float sizeM)> lengthByOwner,
        IList<Selectable> assemblySelectables)
    {
        if (sel == null)
            return null;

        sel.EnsureMeasurablesLinked();

        if (sel.Measurables != null)
        {
            foreach (var m in sel.Measurables)
            {
                if (m == null || m.Disabled || !HasToOriginType(m))
                    continue;
                bool claimed = false;
                if (lengthByOwner != null)
                {
                    foreach (var existing in lengthByOwner.Values)
                    {
                        if (existing.measurable == m)
                        {
                            claimed = true;
                            break;
                        }
                    }
                }
                if (!claimed)
                {
                    m.EnsureConfiguredAsCatalogLength();
                    return m;
                }
            }
        }

        Measurable found = FindToOriginForSizeOwner(sel, lengthByOwner, assemblySelectables);
        if (found != null)
        {
            found.EnsureConfiguredAsCatalogLength();
            return found;
        }

        // Last resort: install on the length owner (PoweredXL PdfData / Cardanic).
        // Measurables has a private setter — link via EnsureMeasurablesLinked.
        var installed = sel.GetComponent<Measurable>();
        if (installed == null)
            installed = sel.gameObject.AddComponent<Measurable>();
        installed.EnsureConfiguredAsCatalogLength();
        sel.EnsureMeasurablesLinked();
        return installed;
    }

    /// <summary>
    /// Find a ToOrigin Measurable for a Size owner whose Measurables list is empty.
    /// Searches parents (Clone hosts Measurable), same-stem assembly twins, then Related.
    /// </summary>
    public static Measurable FindToOriginForSizeOwner(
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
            // Never steal another length owner's ToOrigin via GetComponentsInChildren
            // (Segment_1 was claiming PoweredXL's force-installed measurable → dedupe
            // dropped 600mm; DropTube claimed Cardanic's → lost 136mm joint).
            var nearest = m.GetComponentInParent<Selectable>(true);
            if (nearest != null && nearest != sel
                && DualSelectStem(nearest.name) != stem
                && IsCutsheetLengthHost(nearest))
                return false;
            taken = m;
            return true;
        }

        // Linked list first (EnsureMeasurablesLinked may already have claimed ToOrigin).
        if (sel.Measurables != null)
        {
            foreach (var m in sel.Measurables)
            {
                if (TryTake(m, out var taken))
                    return taken;
            }
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

    /// <summary>
    /// Find or synthesize a Floor measurable for a product head.
    /// Log: NewBoomHead → "has no Floor Measurable under it" / floors=0 — nested
    /// Measurable_ToFloor was missing or untyped at runtime; install as fallback.
    /// </summary>
    static Measurable FindFloorMeasurableUnder(Selectable sel)
    {
        if (sel == null)
            return null;

        bool PreferName(Measurable m) =>
            m != null
            && (m.name.IndexOf("ToFloor", System.StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(m.name, "floor", System.StringComparison.OrdinalIgnoreCase));

        bool IsFloorType(Measurable m)
        {
            if (m == null || m.Disabled)
                return false;
            if (m.MeasurementTypes != null
                && m.MeasurementTypes.Contains(MeasurementType.Floor))
                return true;
            return m.Measurements != null
                && m.Measurements.Any(x => x != null && x.MeasurementType == MeasurementType.Floor);
        }

        Measurable Search(Selectable root)
        {
            if (root == null)
                return null;

            // Name wins — prefab renames the Floor instance to Measurable_ToFloor.
            foreach (var m in root.GetComponentsInChildren<Measurable>(true))
            {
                if (PreferName(m))
                    return m;
            }

            if (root.Measurables != null)
            {
                foreach (var m in root.Measurables)
                {
                    if (IsFloorType(m))
                        return m;
                }
            }

            foreach (var m in root.GetComponentsInChildren<Measurable>(true))
            {
                if (!IsFloorType(m))
                    continue;
                var nearest = m.GetComponentInParent<Selectable>(true);
                if (nearest != null && nearest != root
                    && Measurable.IsFloorClearanceProductHead(nearest))
                    continue;
                return m;
            }

            return null;
        }

        Measurable found = Search(sel);
        if (found == null && sel.RelatedSelectables != null)
        {
            foreach (var rel in sel.RelatedSelectables)
            {
                if (rel == null || rel == sel)
                    continue;
                found = Search(rel);
                if (found != null)
                    break;
            }
        }

        return found;
    }

    /// <summary>
    /// Ensure a product head has a Floor measurable ready for cutsheet draw.
    /// </summary>
    static Measurable EnsureFloorMeasurableForProductHead(Selectable sel)
    {
        if (sel == null)
            return null;

        Measurable floorM = FindFloorMeasurableUnder(sel);
        if (floorM == null)
        {
            var go = new GameObject("Measurable_ToFloor");
            go.transform.SetParent(sel.transform, false);
            floorM = go.AddComponent<Measurable>();
        }

        floorM.EnsureConfiguredAsElevationFloor();
        sel.EnsureMeasurablesLinked();
        if (sel.Measurables != null && !sel.Measurables.Contains(floorM))
            sel.Measurables.Add(floorM);
        return floorM;
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
