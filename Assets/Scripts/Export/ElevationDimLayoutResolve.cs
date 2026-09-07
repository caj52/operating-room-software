using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Page-plane elev dim layout.
///
/// Rules:
/// - Label gap to its own dim line is always <see cref="ElevationDimPlacement.LabelGapMeters"/>
///   (never nudge text alone — move the whole dim).
/// - Legal leader directions are the two page-plane perpendiculars to the dim body.
/// - Prefer the curated outboard side and the closest offset that keeps body + label
///   clear of already-settled dims and off the measured part.
/// - If that band is full, walk farther outboard (same side first) until labels clear.
/// - Overlap tests use camera right/up as the page X/Y axes.
/// </summary>
public static class ElevationDimLayoutResolve
{
    const float PagePadMeters = 0.025f;
    const float OutboardStepMeters = 0.04f;
    const float MinOffsetMeters = 0.08f;
    /// <summary>Far enough for a few stacked horizontal arm callouts under one mount.</summary>
    const float MaxOffsetMeters = 0.70f;
    const float BodyHalfWidthMeters = 0.012f;
    const float SepWeight = 6f;
    const float OffsetWeight = 18f;
    const float PreferredSideBonus = 8f;
    const float ThroughPartPenalty = 400f;

    struct SidePick
    {
        public bool Valid;
        public bool Clear;
        public float Score;
        public Vector3 Dir;
        public float Offset;
    }

    public static void Apply(Camera camera)
    {
        if (camera == null || !camera.orthographic)
            return;

        Vector3 pageRight = camera.transform.right;
        Vector3 pageUp = camera.transform.up;
        Vector3 pageFwd = camera.transform.forward;
        if (pageRight.sqrMagnitude < 1e-8f || pageUp.sqrMagnitude < 1e-8f)
            return;
        pageRight.Normalize();
        pageUp.Normalize();
        pageFwd.Normalize();

        var candidates = new List<Measurer>();
        var settled = new List<PageGeom>();

        foreach (var m in Object.FindObjectsByType<Measurer>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (m == null || !m.gameObject.activeSelf)
                continue;
            if (!m.ShouldDrawInElevationPhoto())
                continue;
            if (m.Measurement == null)
                continue;

            // Floor clearance lines span floor→underside (page-tall). Settling the full
            // body as an obstacle shoved ceiling-tube / column dims off the assembly.
            // Only the floor label competes for page space with length callouts.
            if (m.Measurement.MeasurementType == MeasurementType.Floor)
            {
                if (TryBuildPageGeom(m, pageRight, pageUp, out PageGeom floorGeom)
                    && floorGeom.HasLabel)
                {
                    floorGeom.Body = floorGeom.Label;
                    settled.Add(floorGeom);
                }
                continue;
            }

            if (m.Measurement.MeasurementType != MeasurementType.ToArmAssemblyOrigin)
                continue;
            if (!m.ElevationLeadersValid)
                continue;
            candidates.Add(m);
        }

        if (candidates.Count == 0)
            return;

        // Longer dims claim space first so short callouts yield around them.
        candidates.Sort((a, b) =>
        {
            float la = Vector3.Distance(a.Measurement.Origin, a.Measurement.HitPoint);
            float lb = Vector3.Distance(b.Measurement.Origin, b.Measurement.HitPoint);
            return lb.CompareTo(la);
        });

        foreach (var measurer in candidates)
        {
            // Solver ticks are immutable during search — ApplyPlacement used to reseat
            // ElevationLeaderFeature* every trial, so later offsets/sides started from
            // corrupted feet (DropTube/column dims walked into empty space).
            Vector3 solverA = measurer.ElevationLeaderFeatureA;
            Vector3 solverB = measurer.ElevationLeaderFeatureB;

            Vector3 body = solverB - solverA;
            Vector3 bodyPage = Vector3.ProjectOnPlane(body, pageFwd);
            if (bodyPage.sqrMagnitude < 1e-8f)
                bodyPage = Vector3.ProjectOnPlane(
                    measurer.Measurement.HitPoint - measurer.Measurement.Origin, pageFwd);
            if (bodyPage.sqrMagnitude < 1e-8f)
                bodyPage = pageRight;
            bodyPage.Normalize();

            Vector3 perp = Vector3.Cross(pageFwd, bodyPage);
            if (perp.sqrMagnitude < 1e-8f)
                perp = pageUp;
            perp.Normalize();

            Vector3 preferred = measurer.ElevationOutboardDir.sqrMagnitude > 1e-8f
                ? measurer.ElevationOutboardDir.normalized
                : perp;
            Vector3 dirPreferred = Vector3.Dot(preferred, perp) >= 0f ? perp : -perp;
            Vector3 dirOther = -dirPreferred;

            Selectable owner = GetLengthOwner(measurer);
            bool haveOwnerRect = TryOwnerPageRect(owner, pageRight, pageUp, out Rect ownerRect);

            SidePick pick = PickBestSide(
                measurer, solverA, solverB, dirPreferred, dirOther, preferred,
                camera, pageRight, pageUp, settled, owner, haveOwnerRect, ownerRect,
                requireOutside: true);

            if (!pick.Valid)
            {
                pick = PickBestSide(
                    measurer, solverA, solverB, dirPreferred, dirOther, preferred,
                    camera, pageRight, pageUp, settled, owner, haveOwnerRect, ownerRect,
                    requireOutside: false);
            }

            if (!pick.Valid)
            {
                pick.Dir = dirPreferred;
                pick.Offset = MinOffsetMeters;
            }

            ApplyPlacement(
                measurer, solverA, solverB, pick.Dir, pick.Offset, camera, pageRight, pageUp);
            if (!TryBuildPageGeom(measurer, pageRight, pageUp, out PageGeom settledGeom))
                settledGeom = default;
            settled.Add(settledGeom);
        }
    }

    static SidePick PickBestSide(
        Measurer m, Vector3 solverA, Vector3 solverB,
        Vector3 dirPreferred, Vector3 dirOther, Vector3 preferred,
        Camera camera, Vector3 pageRight, Vector3 pageUp, List<PageGeom> settled,
        Selectable owner, bool haveOwnerRect, Rect ownerRect, bool requireOutside)
    {
        SidePick a = FindClosestClearOnSide(
            m, solverA, solverB, dirPreferred, preferred, camera, pageRight, pageUp,
            settled, owner, haveOwnerRect, ownerRect, requireOutside);
        SidePick b = FindClosestClearOnSide(
            m, solverA, solverB, dirOther, preferred, camera, pageRight, pageUp,
            settled, owner, haveOwnerRect, ownerRect, requireOutside);
        return BetterPick(a, b);
    }

    static SidePick BetterPick(SidePick a, SidePick b)
    {
        if (!a.Valid) return b;
        if (!b.Valid) return a;
        if (a.Clear != b.Clear) return a.Clear ? a : b;
        return a.Score >= b.Score ? a : b;
    }

    /// <summary>
    /// Walk outboard from the part until body+label clear settled dims (or max).
    /// First clear slot on this side wins — no style walk beyond that.
    /// </summary>
    static SidePick FindClosestClearOnSide(
        Measurer m, Vector3 solverA, Vector3 solverB, Vector3 dir, Vector3 preferred,
        Camera camera, Vector3 pageRight, Vector3 pageUp, List<PageGeom> settled,
        Selectable owner, bool haveOwnerRect, Rect ownerRect, bool requireOutside)
    {
        SidePick best = default;
        bool preferThisSide = Vector3.Dot(dir, preferred) > 0f;

        for (float offset = MinOffsetMeters; offset <= MaxOffsetMeters + 1e-4f; offset += OutboardStepMeters)
        {
            bool throughPart = false;
            if (haveOwnerRect)
            {
                PredictFeet(solverA, solverB, dir, owner, out Vector3 predFeatA, out Vector3 predFeatB);
                Vector3 predA = predFeatA + dir * offset;
                Vector3 predB = predFeatB + dir * offset;
                if (TrySegmentRect(predA, predB, pageRight, pageUp, BodyHalfWidthMeters, out Rect predBody))
                {
                    Inflate(ref predBody, PagePadMeters);
                    if (predBody.Overlaps(ownerRect))
                        throughPart = true;
                }
            }

            if (throughPart && requireOutside)
                continue;

            ApplyPlacement(m, solverA, solverB, dir, offset, camera, pageRight, pageUp);
            if (!TryBuildPageGeom(m, pageRight, pageUp, out PageGeom geom))
                continue;
            if (!FitsLockedElevationFrame(m, pageRight, pageUp))
                continue;

            if (requireOutside && haveOwnerRect)
            {
                if (geom.Body.Overlaps(ownerRect))
                    continue;
                if (geom.HasLabel && geom.Label.Overlaps(ownerRect))
                    continue;
            }

            bool clear = !throughPart && !OverlapsSettled(geom, settled);
            float sep = SeparationFromSettled(geom, settled, excludeLast: false);
            float score = (clear ? 50f : -200f)
                + SepWeight * sep
                - OffsetWeight * offset
                + (preferThisSide ? PreferredSideBonus : 0f);
            if (throughPart)
                score -= ThroughPartPenalty;

            if (!best.Valid
                || (clear && !best.Clear)
                || (clear == best.Clear && score > best.Score + 1e-4f))
            {
                best = new SidePick
                {
                    Valid = true,
                    Clear = clear,
                    Score = score,
                    Dir = dir,
                    Offset = offset,
                };
            }

            if (clear)
                break;
        }

        return best;
    }

    static Selectable GetLengthOwner(Measurer m)
    {
        var measurable = m?.Measurement?.Measurable;
        if (measurable == null)
            return null;
        return measurable.GetOwningSelectable()
            ?? measurable.GetComponentInParent<Selectable>(true);
    }

    static bool TryOwnerPageRect(
        Selectable owner, Vector3 pageRight, Vector3 pageUp, out Rect rect)
    {
        rect = default;
        if (owner == null)
            return false;
        // Measured part only — never union the service head (that shoved 300mm far out).
        if (!Measurable.TryGetStrictOwnRendererBounds(owner, out Bounds b)
            && !Measurable.TryGetOwnRendererBounds(owner, out b))
            return false;
        if (!TryBoundsRect(b, pageRight, pageUp, out rect))
            return false;
        Inflate(ref rect, PagePadMeters);
        return true;
    }

    /// <summary>
    /// Map solver ticks → feet on the outboard face. Always starts from solverA/B
    /// (never from previously mutated ElevationLeaderFeature*).
    /// Horizontal: keep solver ticks. Vertical: slide xz onto the part's own mesh face
    /// toward dir, capped to tube radius — claim AABB faces shoved dims into empty space.
    /// </summary>
    static void PredictFeet(
        Vector3 solverA, Vector3 solverB, Vector3 dir, Selectable owner,
        out Vector3 featA, out Vector3 featB)
    {
        featA = solverA;
        featB = solverB;
        if (owner == null)
            return;

        float alongLen = Vector3.Distance(
            new Vector3(solverA.x, 0f, solverA.z), new Vector3(solverB.x, 0f, solverB.z));
        bool horizontalLength = alongLen >= 0.05f
            && Mathf.Abs(solverA.y - solverB.y) <= alongLen * 0.25f;
        if (horizontalLength)
            return;

        Vector3 horiz = dir;
        horiz.y = 0f;
        if (horiz.sqrMagnitude < 1e-6f)
            return;
        horiz.Normalize();

        if (!Measurable.TryGetStrictOwnRendererBounds(owner, out Bounds face)
            && !Measurable.TryGetOwnRendererBounds(owner, out face))
            return;

        // Tube / column radius only — never the inflated claim union.
        // Keep solver xz (AP / column axis); only nudge toward the outboard face.
        const float MaxFaceSlideM = 0.06f;
        float faceExtent =
            Mathf.Abs(horiz.x) * face.extents.x + Mathf.Abs(horiz.z) * face.extents.z;
        faceExtent = Mathf.Min(faceExtent, MaxFaceSlideM);
        Vector3 toFace = horiz * faceExtent;
        featA = solverA + toFace;
        featB = solverB + toFace;
    }

    static void ApplyPlacement(
        Measurer m, Vector3 solverA, Vector3 solverB,
        Vector3 dir, float meters, Camera camera,
        Vector3 pageRight, Vector3 pageUp)
    {
        dir.Normalize();
        m.ElevationOutboardDir = dir;

        PredictFeet(solverA, solverB, dir, GetLengthOwner(m), out Vector3 featA, out Vector3 featB);
        m.ElevationLeaderFeatureA = featA;
        m.ElevationLeaderFeatureB = featB;

        m.Measurement.Origin = featA + dir * meters;
        m.Measurement.HitPoint = featB + dir * meters;

        var lengthOwner = GetLengthOwner(m);
        if (lengthOwner != null)
            ElevationLengthGeometry.RememberClaimEndpoints(lengthOwner, featA, featB);

        // Keep mm on the outside of the dim line (fixed LabelGapMeters via PlaceLabel).
        bool mostlyHorizontal =
            Mathf.Abs(Vector3.Dot(dir, pageUp)) >= Mathf.Abs(Vector3.Dot(dir, pageRight));
        if (mostlyHorizontal)
        {
            m.ElevationPreferLabelBelow = Vector3.Dot(dir, pageUp) < 0f;
            m.ElevationLabelSideSign = 0f;
        }
        else
        {
            m.ElevationPreferLabelBelow = false;
            m.ElevationLabelSideSign = Vector3.Dot(dir, pageRight) >= 0f ? 1f : -1f;
        }

        m.UpdateTransform(camera);
        m.RefreshCutsheetLeaders(camera, 0.0025f);
        if (m.MeasurementText != null && m.MeasurementText.gameObject.activeSelf)
            m.MeasurementText.UpdateVisibilityAndPosition(camera, force: true);
    }

    static bool TryPreviewLeaderFeet(
        Measurer m, Vector3 dir, Selectable owner, out Vector3 featA, out Vector3 featB)
    {
        // Kept for call sites that still pass the measurer; prefer PredictFeet(solver…).
        PredictFeet(
            m.ElevationLeaderFeatureA, m.ElevationLeaderFeatureB, dir, owner,
            out featA, out featB);
        return owner != null;
    }

    /// <summary>
    /// Park both extension-line feet on the part face toward the dim (dir).
    /// </summary>
    static void ReseatLeaderFeetOnDimSide(Measurer m, Vector3 dir, Selectable owner)
    {
        if (m == null)
            return;
        PredictFeet(
            m.ElevationLeaderFeatureA, m.ElevationLeaderFeatureB, dir, owner,
            out Vector3 featA, out Vector3 featB);
        m.ElevationLeaderFeatureA = featA;
        m.ElevationLeaderFeatureB = featB;
    }

    static bool TryBoundsRect(
        Bounds b, Vector3 pageRight, Vector3 pageUp, out Rect rect)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;
        Vector3[] corners =
        {
            c + new Vector3(-e.x, -e.y, -e.z),
            c + new Vector3(-e.x, -e.y,  e.z),
            c + new Vector3(-e.x,  e.y, -e.z),
            c + new Vector3(-e.x,  e.y,  e.z),
            c + new Vector3( e.x, -e.y, -e.z),
            c + new Vector3( e.x, -e.y,  e.z),
            c + new Vector3( e.x,  e.y, -e.z),
            c + new Vector3( e.x,  e.y,  e.z),
        };
        float minR = float.MaxValue, maxR = float.MinValue;
        float minU = float.MaxValue, maxU = float.MinValue;
        for (int i = 0; i < corners.Length; i++)
        {
            float r = Vector3.Dot(corners[i], pageRight);
            float u = Vector3.Dot(corners[i], pageUp);
            minR = Mathf.Min(minR, r);
            maxR = Mathf.Max(maxR, r);
            minU = Mathf.Min(minU, u);
            maxU = Mathf.Max(maxU, u);
        }
        if (maxR <= minR || maxU <= minU)
        {
            rect = default;
            return false;
        }
        rect = Rect.MinMaxRect(minR, minU, maxR, maxU);
        return true;
    }

    static float SeparationFromSettled(PageGeom geom, List<PageGeom> settled, bool excludeLast)
    {
        int n = settled.Count - (excludeLast && settled.Count > 0 ? 1 : 0);
        if (n <= 0)
            return 0.5f;

        float minSep = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            float s = GeomSeparation(geom, settled[i]);
            if (s < minSep)
                minSep = s;
        }
        return minSep >= float.MaxValue * 0.5f ? 0.5f : minSep;
    }

    static float GeomSeparation(PageGeom a, PageGeom b)
    {
        float min = RectSeparation(a.Body, b.Body);
        if (a.HasLabel)
            min = Mathf.Min(min, RectSeparation(a.Label, b.Body));
        if (b.HasLabel)
            min = Mathf.Min(min, RectSeparation(a.Body, b.Label));
        if (a.HasLabel && b.HasLabel)
            min = Mathf.Min(min, RectSeparation(a.Label, b.Label));
        return min;
    }

    /// <summary>Positive gap between AABBs; 0 if touching; negative if overlapping.</summary>
    static float RectSeparation(Rect a, Rect b)
    {
        float dx = 0f;
        if (a.xMax < b.xMin)
            dx = b.xMin - a.xMax;
        else if (b.xMax < a.xMin)
            dx = a.xMin - b.xMax;

        float dy = 0f;
        if (a.yMax < b.yMin)
            dy = b.yMin - a.yMax;
        else if (b.yMax < a.yMin)
            dy = a.yMin - b.yMax;

        if (dx < 0f || dy < 0f)
        {
            // Overlap on both axes (or one axis overlap with penetration).
            if (a.Overlaps(b))
            {
                float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                float oy = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                return -Mathf.Min(ox, oy);
            }
            return 0f;
        }

        if (dx > 0f && dy > 0f)
            return Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Max(dx, dy);
    }

    struct PageGeom
    {
        public Rect Body;
        public Rect Label;
        public bool HasLabel;
    }

    static bool TryBuildPageGeom(
        Measurer m, Vector3 pageRight, Vector3 pageUp, out PageGeom geom)
    {
        geom = default;
        Vector3 a = m.Measurement.Origin;
        Vector3 b = m.Measurement.HitPoint;
        if (!TrySegmentRect(a, b, pageRight, pageUp, BodyHalfWidthMeters, out geom.Body))
            return false;

        var mt = m.MeasurementText;
        if (mt != null && mt.gameObject.activeSelf && mt.Text != null
            && TryLabelRect(mt, pageRight, pageUp, out Rect label))
        {
            Inflate(ref label, PagePadMeters);
            geom.Label = label;
            geom.HasLabel = true;
        }

        Inflate(ref geom.Body, PagePadMeters * 0.5f);
        return true;
    }

    static bool OverlapsSettled(PageGeom candidate, List<PageGeom> settled)
    {
        for (int i = 0; i < settled.Count; i++)
        {
            var s = settled[i];
            if (candidate.Body.Overlaps(s.Body))
                return true;
            if (candidate.HasLabel && candidate.Label.Overlaps(s.Body))
                return true;
            if (s.HasLabel && candidate.Body.Overlaps(s.Label))
                return true;
            if (candidate.HasLabel && s.HasLabel && candidate.Label.Overlaps(s.Label))
                return true;
        }
        return false;
    }

    static void Inflate(ref Rect r, float pad)
    {
        r.xMin -= pad;
        r.xMax += pad;
        r.yMin -= pad;
        r.yMax += pad;
    }

    static bool TrySegmentRect(
        Vector3 a, Vector3 b, Vector3 pageRight, Vector3 pageUp, float halfW, out Rect rect)
    {
        float ra = Vector3.Dot(a, pageRight);
        float ua = Vector3.Dot(a, pageUp);
        float rb = Vector3.Dot(b, pageRight);
        float ub = Vector3.Dot(b, pageUp);
        float minR = Mathf.Min(ra, rb) - halfW;
        float maxR = Mathf.Max(ra, rb) + halfW;
        float minU = Mathf.Min(ua, ub) - halfW;
        float maxU = Mathf.Max(ua, ub) + halfW;
        if (maxR <= minR || maxU <= minU)
        {
            rect = default;
            return false;
        }
        rect = Rect.MinMaxRect(minR, minU, maxR, maxU);
        return true;
    }

    static bool TryLabelRect(
        MeasurementText mt, Vector3 pageRight, Vector3 pageUp, out Rect rect)
    {
        rect = default;
        var tmp = mt.Text;
        tmp.ForceMeshUpdate();
        Bounds gb = tmp.textBounds;
        var rt = tmp.rectTransform;
        Vector3 c = gb.center;
        Vector3 e = gb.extents;
        float minR = float.MaxValue, maxR = float.MinValue;
        float minU = float.MaxValue, maxU = float.MinValue;
        Vector3[] corners =
        {
            c + new Vector3(-e.x, -e.y, 0f),
            c + new Vector3(-e.x,  e.y, 0f),
            c + new Vector3( e.x, -e.y, 0f),
            c + new Vector3( e.x,  e.y, 0f),
        };
        for (int i = 0; i < 4; i++)
        {
            Vector3 w = rt.TransformPoint(corners[i]);
            float r = Vector3.Dot(w, pageRight);
            float u = Vector3.Dot(w, pageUp);
            minR = Mathf.Min(minR, r);
            maxR = Mathf.Max(maxR, r);
            minU = Mathf.Min(minU, u);
            maxU = Mathf.Max(maxU, u);
        }

        if (maxR <= minR || maxU <= minU)
            return false;
        rect = Rect.MinMaxRect(minR, minU, maxR, maxU);
        return true;
    }

    /// <summary>
    /// The photo is locked floor→ceiling. Horizontal arm dims that walk above the
    /// underside lose their mm on the crop — reject those slots so the other face wins.
    /// Vertical flange/tube ticks may sit on the ceiling plane itself.
    /// </summary>
    static bool FitsLockedElevationFrame(Measurer m, Vector3 pageRight, Vector3 pageUp)
    {
        if (m?.Measurement == null)
            return true;

        float floor = ElevationDimPlacement.FloorTopY();
        float ceil = ElevationDimPlacement.CeilingUndersideY();
        float pad = ElevationDimPlacement.TickHalfLengthMeters;

        var text = m.MeasurementText;
        if (text != null && text.gameObject.activeSelf && text.Text != null)
        {
            text.Text.ForceMeshUpdate();
            Bounds gb = text.Text.textBounds;
            var rt = text.Text.rectTransform;
            Vector3 c = gb.center;
            Vector3 e = gb.extents;
            Vector3[] corners =
            {
                c + new Vector3(-e.x, -e.y, 0f),
                c + new Vector3(-e.x,  e.y, 0f),
                c + new Vector3( e.x, -e.y, 0f),
                c + new Vector3( e.x,  e.y, 0f),
            };
            for (int i = 0; i < 4; i++)
            {
                float y = rt.TransformPoint(corners[i]).y;
                if (y > ceil - 0.01f || y < floor + 0.01f)
                    return false;
            }
        }

        Vector3 body = m.Measurement.HitPoint - m.Measurement.Origin;
        bool horizontal = Mathf.Abs(Vector3.Dot(body, pageRight))
            >= Mathf.Abs(Vector3.Dot(body, pageUp));
        if (!horizontal)
            return true;

        float margin = ElevationDimPlacement.CutsheetInFrameMarginMeters;
        float lo = floor + margin;
        float hi = ceil - margin;
        float ya = m.Measurement.Origin.y;
        float yb = m.Measurement.HitPoint.y;
        return ya >= lo - pad && yb >= lo - pad && ya <= hi + pad && yb <= hi + pad;
    }
}
