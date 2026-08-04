using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Page-plane elev dim layout.
///
/// Rules:
/// - Label gap to its own dim line is always <see cref="ElevationDimPlacement.LabelGapMeters"/>
///   (never nudge text alone).
/// - Each length dim may sit on either side perpendicular to its body (up/down for
///   horizontal dims, left/right for vertical). Pick the side + offset that maximizes
///   separation from already-placed dims while staying as close to the part as possible.
/// - Never park a dim line through the measured part; prefer the other face instead.
/// - Stay close to the part (hard offset cap) — do not clear the whole service head.
/// - Overlap tests use camera right/up as the page X/Y axes.
/// </summary>
public static class ElevationDimLayoutResolve
{
    const float PagePadMeters = 0.025f;
    const float OutboardStepMeters = 0.04f;
    const float MinOffsetMeters = 0.08f;
    /// <summary>Hard cap — never park a length dim farther than this from the part face.</summary>
    const float MaxOffsetMeters = 0.26f;
    const float BodyHalfWidthMeters = 0.012f;
    const float SepWeight = 6f;
    const float OffsetWeight = 18f;
    const float ThroughPartPenalty = 400f;

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

            // Floor dims stay on the product — fixed obstacles for length layout.
            if (m.Measurement.MeasurementType == MeasurementType.Floor)
            {
                if (TryBuildPageGeom(m, pageRight, pageUp, out PageGeom floorGeom))
                    settled.Add(floorGeom);
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

        int flips = 0;
        int blockedSides = 0;
        float maxOutboard = 0f;
        float sumSep = 0f;

        foreach (var measurer in candidates)
        {
            Vector3 body = measurer.ElevationLeaderFeatureB - measurer.ElevationLeaderFeatureA;
            Vector3 bodyPage = Vector3.ProjectOnPlane(body, pageFwd);
            if (bodyPage.sqrMagnitude < 1e-8f)
                bodyPage = Vector3.ProjectOnPlane(
                    measurer.Measurement.HitPoint - measurer.Measurement.Origin, pageFwd);
            if (bodyPage.sqrMagnitude < 1e-8f)
                bodyPage = pageRight;
            bodyPage.Normalize();

            // Perpendicular in the page plane — the only legal leader directions.
            Vector3 perp = Vector3.Cross(pageFwd, bodyPage);
            if (perp.sqrMagnitude < 1e-8f)
                perp = pageUp;
            perp.Normalize();

            Vector3 preferred = measurer.ElevationOutboardDir.sqrMagnitude > 1e-8f
                ? measurer.ElevationOutboardDir.normalized
                : perp;
            Vector3 dirA = Vector3.Dot(preferred, perp) >= 0f ? perp : -perp;
            Vector3 dirB = -dirA;

            Selectable owner = GetLengthOwner(measurer);
            bool haveOwnerRect = TryOwnerPageRect(owner, pageRight, pageUp, out Rect ownerRect);

            float bestScore = float.NegativeInfinity;
            Vector3 bestDir = dirA;
            float bestOffset = MinOffsetMeters;
            PageGeom bestGeom = default;
            bool haveBest = false;

            // Pass 1: only outside placements (path clear of foreign gear + clear of owner).
            int blocked = 0;
            EvaluateSide(measurer, dirA, camera, pageRight, pageUp, settled,
                owner, haveOwnerRect, ownerRect, requireOutside: true,
                ref bestScore, ref bestDir, ref bestOffset, ref bestGeom, ref haveBest, ref blocked);
            EvaluateSide(measurer, dirB, camera, pageRight, pageUp, settled,
                owner, haveOwnerRect, ownerRect, requireOutside: true,
                ref bestScore, ref bestDir, ref bestOffset, ref bestGeom, ref haveBest, ref blocked);
            blockedSides += blocked;

            // Pass 2: only if no outside slot exists (should be rare).
            if (!haveBest)
            {
                EvaluateSide(measurer, dirA, camera, pageRight, pageUp, settled,
                    owner, haveOwnerRect, ownerRect, requireOutside: false,
                    ref bestScore, ref bestDir, ref bestOffset, ref bestGeom, ref haveBest, ref blocked);
                EvaluateSide(measurer, dirB, camera, pageRight, pageUp, settled,
                    owner, haveOwnerRect, ownerRect, requireOutside: false,
                    ref bestScore, ref bestDir, ref bestOffset, ref bestGeom, ref haveBest, ref blocked);
            }

            if (!haveBest)
            {
                bestDir = dirA;
                bestOffset = MinOffsetMeters;
                ApplyPlacement(measurer, bestDir, bestOffset, camera, pageRight, pageUp);
                TryBuildPageGeom(measurer, pageRight, pageUp, out bestGeom);
            }
            else
            {
                ApplyPlacement(measurer, bestDir, bestOffset, camera, pageRight, pageUp);
                if (!TryBuildPageGeom(measurer, pageRight, pageUp, out bestGeom))
                    bestGeom = default;
            }

            if (Vector3.Dot(bestDir, dirA) < 0.5f)
                flips++;

            settled.Add(bestGeom);
            maxOutboard = Mathf.Max(maxOutboard, bestOffset);
            sumSep += SeparationFromSettled(bestGeom, settled, excludeLast: true);
        }

        float avgSep = candidates.Count > 0 ? sumSep / candidates.Count : 0f;
        Debug.Log(
            $"[ElevDim] LAYOUT RESOLVE dims={candidates.Count} sideFlips={flips} " +
            $"blockedInside={blockedSides} maxOutboard={maxOutboard:F3} " +
            $"avgSep={avgSep:F3} settled={settled.Count}");
    }

    static void EvaluateSide(
        Measurer m, Vector3 dir, Camera camera,
        Vector3 pageRight, Vector3 pageUp, List<PageGeom> settled,
        Selectable owner, bool haveOwnerRect, Rect ownerRect, bool requireOutside,
        ref float bestScore, ref Vector3 bestDir, ref float bestOffset,
        ref PageGeom bestGeom, ref bool haveBest, ref int blockedCount)
    {
        for (float offset = MinOffsetMeters; offset <= MaxOffsetMeters + 1e-4f; offset += OutboardStepMeters)
        {
            // Only the measured part — do NOT clear the whole service head / assembly
            // (that shoved 300mm far past the SH). Through-part = body on the wrong face.
            bool throughPart = false;
            if (haveOwnerRect)
            {
                TryPreviewLeaderFeet(m, dir, owner, out Vector3 predFeatA, out Vector3 predFeatB);
                Vector3 predA = predFeatA + dir * offset;
                Vector3 predB = predFeatB + dir * offset;
                if (TrySegmentRect(predA, predB, pageRight, pageUp, BodyHalfWidthMeters, out Rect predBody))
                {
                    Inflate(ref predBody, PagePadMeters);
                    if (predBody.Overlaps(ownerRect))
                        throughPart = true;
                }
            }

            if (throughPart)
            {
                blockedCount++;
                if (requireOutside)
                    continue;
            }

            ApplyPlacement(m, dir, offset, camera, pageRight, pageUp);
            if (!TryBuildPageGeom(m, pageRight, pageUp, out PageGeom geom))
                continue;

            if (haveOwnerRect && geom.Body.Overlaps(ownerRect))
            {
                blockedCount++;
                if (requireOutside)
                    continue;
            }
            if (haveOwnerRect && geom.HasLabel && geom.Label.Overlaps(ownerRect))
            {
                blockedCount++;
                if (requireOutside)
                    continue;
            }

            float score = ScorePlacement(geom, settled, offset, throughPart);
            if (!haveBest || score > bestScore + 1e-4f)
            {
                bestScore = score;
                bestDir = dir;
                bestOffset = offset;
                bestGeom = geom;
                haveBest = true;
            }

            // First close, clear slot wins — do not walk farther for "more separation".
            if (!throughPart && !OverlapsSettled(geom, settled))
                break;
        }
    }

    static float ScorePlacement(PageGeom geom, List<PageGeom> settled, float offset, bool throughPart)
    {
        bool overlaps = OverlapsSettled(geom, settled);
        float sep = SeparationFromSettled(geom, settled, excludeLast: false);
        // Prefer close to the part; separation is a tie-break only.
        float score = (overlaps ? -50f : 50f) + SepWeight * sep - OffsetWeight * offset;
        if (throughPart)
            score -= ThroughPartPenalty;
        return score;
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

    static bool TryPreviewLeaderFeet(
        Measurer m, Vector3 dir, Selectable owner, out Vector3 featA, out Vector3 featB)
    {
        featA = m.ElevationLeaderFeatureA;
        featB = m.ElevationLeaderFeatureB;
        if (owner == null)
            return false;
        if (!Measurable.TryGetStrictOwnRendererBounds(owner, out Bounds b)
            && !Measurable.TryGetOwnRendererBounds(owner, out b))
            return false;

        Vector3 horiz = dir;
        horiz.y = 0f;
        if (horiz.sqrMagnitude >= 1e-6f)
        {
            horiz.Normalize();
            float faceExtent =
                Mathf.Abs(horiz.x) * b.extents.x + Mathf.Abs(horiz.z) * b.extents.z;
            Vector3 toFace = horiz * faceExtent;
            Vector3 c = b.center;
            featA = new Vector3(c.x, featA.y, c.z) + toFace;
            featB = new Vector3(c.x, featB.y, c.z) + toFace;
            return true;
        }

        float faceY = dir.y >= 0f ? b.max.y : b.min.y;
        featA = new Vector3(featA.x, faceY, featA.z);
        featB = new Vector3(featB.x, faceY, featB.z);
        return true;
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

    static void ApplyPlacement(
        Measurer m, Vector3 dir, float meters, Camera camera,
        Vector3 pageRight, Vector3 pageUp)
    {
        dir.Normalize();
        m.ElevationOutboardDir = dir;

        // Flip moves the WHOLE dim: leader feet must sit on the same face as the
        // callout. Leaving feet on the opposite wall draws leaders through the part.
        ReseatLeaderFeetOnDimSide(m, dir, GetLengthOwner(m));

        m.Measurement.Origin = m.ElevationLeaderFeatureA + dir * meters;
        m.Measurement.HitPoint = m.ElevationLeaderFeatureB + dir * meters;

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

    /// <summary>
    /// Park both extension-line feet on the part face toward the dim (dir).
    /// </summary>
    static void ReseatLeaderFeetOnDimSide(Measurer m, Vector3 dir, Selectable owner)
    {
        if (m == null)
            return;
        if (TryPreviewLeaderFeet(m, dir, owner, out Vector3 featA, out Vector3 featB))
        {
            m.ElevationLeaderFeatureA = featA;
            m.ElevationLeaderFeatureB = featB;
        }
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
}
