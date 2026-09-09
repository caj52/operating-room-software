using UnityEngine;

/// <summary>
/// Single authority for elevation cutsheet length tick endpoints.
/// Ownership stays in ElevationDimCurator / Policy.
/// </summary>
public static class ElevationDimSolver
{
    public const float FlushCeilingGapM = 0.12f;
    public const float CatalogMatchTolM = 0.02f;

    /// <summary>
    /// Printed number vs drawn length at sheet scale. 1 mm at full size is well under
    /// a line width once the sheet is scaled, so anything past this is a real defect.
    /// </summary>
    public const float ScaleAssertMm = 1f;

    /// <summary>
    /// Slope (rise/run) past which an arm is dimensioned aligned rather than horizontal.
    /// ~2.9°, well under any real boom droop but above modelling noise.
    /// </summary>
    public const float PitchedArmTanMin = 0.05f;

    public enum PartKind
    {
        CeilingTube,
        VerticalColumn,
        HorizontalArm
    }

    public readonly struct Solution
    {
        public readonly Vector3 A;
        public readonly Vector3 B;
        public readonly Vector3 Axis;
        public readonly bool Vertical;
        public readonly PartKind Kind;
        public readonly string MeshSource;
        public readonly string FitMode;
        public readonly float MeshSpanM;
        public readonly float PageSpanM;
        public readonly float CeilingGapM;

        public Solution(
            Vector3 a, Vector3 b, Vector3 axis, bool vertical,
            PartKind kind, string meshSource, string fitMode,
            float meshSpanM, float pageSpanM, float ceilingGapM)
        {
            A = a;
            B = b;
            Axis = axis;
            Vertical = vertical;
            Kind = kind;
            MeshSource = meshSource ?? "";
            FitMode = fitMode ?? "";
            MeshSpanM = meshSpanM;
            PageSpanM = pageSpanM;
            CeilingGapM = ceilingGapM;
        }

        public float SpanM => Vector3.Distance(A, B);
    }

    public static PartKind Classify(Selectable owner, float catalogM, bool layoutVertical)
    {
        if (owner == null)
            return layoutVertical ? PartKind.VerticalColumn : PartKind.HorizontalArm;

        string n = owner.name ?? "";
        if (Measurable.IsDropTubeName(n) || Measurable.IsVerticalHangLengthName(n))
            return PartKind.CeilingTube;

        // Never hang PoweredXL / PdfData arms as vertical columns.
        if (Measurable.IsForcedHorizontalCatalogArm(owner))
            return PartKind.HorizontalArm;

        if (layoutVertical || ElevationLengthGeometry.IsVerticalTube(owner, catalogM))
            return PartKind.VerticalColumn;

        return PartKind.HorizontalArm;
    }

    public static bool TrySolve(
        Selectable owner,
        float catalogM,
        Camera camera,
        bool layoutVertical,
        out Solution solution)
    {
        solution = default;
        if (owner == null || catalogM <= 0f)
            return false;

        PartKind kind = Classify(owner, catalogM, layoutVertical);
        bool ok = kind == PartKind.HorizontalArm
            ? TrySolveHorizontal(owner, catalogM, camera, out solution)
            : TrySolveVertical(owner, catalogM, kind, out solution);
        if (!ok)
            return false;

        return true;
    }

    static bool TrySolveVertical(
        Selectable owner, float catalogM, PartKind kind, out Solution solution)
    {
        solution = default;
        if (!ElevationLengthGeometry.TryGetColumnOrOwnBounds(owner, out Bounds col))
            return false;

        float cx = col.center.x;
        float cz = col.center.z;
        Vector3 top = new Vector3(cx, col.max.y, cz);
        Vector3 tip = new Vector3(cx, col.min.y, cz);
        if (top.y < tip.y)
            (top, tip) = (tip, top);

        float meshSpan = Mathf.Abs(top.y - tip.y);
        var room = ElevationRoomFrame.Current;
        string fit = "meshColumn";
        float gap = room.CeilingY - col.max.y;

        // Keep flange dim on the mount, not the tandem plate centroid.
        if (owner.ParentAttachmentPoint != null)
        {
            Vector3 ap = owner.ParentAttachmentPoint.transform.position;
            top.x = tip.x = ap.x;
            top.z = tip.z = ap.z;
        }

        if (kind == PartKind.CeilingTube)
        {
            // With a cover/tandem plate: ticks span the plate's own thickness
            // (plate top → plate underside). Starting at the underside left the
            // whole dim hanging under the box — top tick on the bottom face.
            // No cover: fall back to the tube mesh top → tip.
            cx = top.x;
            cz = top.z;
            float meshTopY = Mathf.Max(top.y, tip.y);
            float meshTipY = Mathf.Min(top.y, tip.y);

            if (ElevationLengthGeometry.TryGetCeilingPlateBand(
                    cx, cz, room.CeilingY,
                    out float plateTop, out float plateUnder, out string plateName)
                && plateUnder < plateTop - 0.01f)
            {
                // One dim per physical plate — second tube on a tandem must not redraw it.
                if (!ElevationLengthGeometry.TryClaimPlateBand(plateTop, plateUnder, plateName))
                    return false;

                top = new Vector3(cx, plateTop, cz);
                tip = new Vector3(cx, plateUnder, cz);
                fit = "plate(" + plateName + ")";
                gap = room.CeilingY - plateTop;
            }
            else
            {
                top = new Vector3(cx, meshTopY, cz);
                tip = new Vector3(cx, meshTipY, cz);
                fit = "meshColumn";
                gap = room.CeilingY - meshTopY;
            }

            if (tip.y < room.FloorY)
                tip.y = room.FloorY;
        }
        else
        {
            FitVerticalCatalog(ref top, ref tip, catalogM, out fit);
        }

        float pageSpanM = Mathf.Abs(top.y - tip.y);
        Debug.Log(
            $"[ElevDim] FIT owner={owner.name} catalogMm={Mathf.RoundToInt(catalogM * 1000f)} " +
            $"fit={fit} vertical=True meshSpanMm={Mathf.RoundToInt(meshSpan * 1000f)} " +
            $"pageSpanMm={Mathf.RoundToInt(pageSpanM * 1000f)} a={top:F3} b={tip:F3}");

        solution = new Solution(
            top, tip, Vector3.down, vertical: true,
            kind, "column", fit,
            meshSpan, pageSpanM, ceilingGapM: gap);
        ElevationLengthGeometry.RememberClaimEndpoints(owner, top, tip);
        return true;
    }

    static void FitVerticalCatalog(
        ref Vector3 top, ref Vector3 tip, float catalogM, out string fit)
    {
        float meshH = Mathf.Abs(top.y - tip.y);
        float cx = top.x;
        float cz = top.z;
        float topY = Mathf.Max(top.y, tip.y);

        // Mesh picks the anchor; the drawn length is always the printed catalog length.
        top = new Vector3(cx, topY, cz);
        tip = new Vector3(cx, topY - catalogM, cz);
        fit = Mathf.Abs(meshH - catalogM) <= CatalogMatchTolM
            ? "hangTop(meshMatch)"
            : (meshH + CatalogMatchTolM < catalogM ? "hangTop(shortMesh)" : "hangTop");
    }

    static void FitCeilingTubeOnMesh(
        ref Vector3 top, ref Vector3 tip, float catalogM, out string fit)
    {
        float meshH = Mathf.Abs(top.y - tip.y);
        float cx = top.x;
        float cz = top.z;
        float midY = 0.5f * (top.y + tip.y);

        // Mesh picks the anchor; the drawn length is always the printed catalog length.
        top = new Vector3(cx, midY + 0.5f * catalogM, cz);
        tip = new Vector3(cx, midY - 0.5f * catalogM, cz);
        fit = Mathf.Abs(meshH - catalogM) <= CatalogMatchTolM
            ? "centerTrim(meshMatch)"
            : (meshH + CatalogMatchTolM < catalogM ? "centerTrim(shortMesh)" : "centerTrim");
    }

    /// <summary>
    /// One rule for every horizontal catalog length, Size or PdfData: the modelling
    /// element's own mesh gives the posed axis and extent, then FitHorizontalCatalog
    /// draws the printed length on it. PoweredXL gets no second oracle — fold AP,
    /// child local +X, column ParentAttachmentPoint and world-AABB support all put the
    /// dim on a line the part does not run along.
    /// </summary>
    static bool TrySolveHorizontal(
        Selectable owner, float catalogM, Camera camera, out Solution solution)
    {
        solution = default;

        bool hasSize = ElevationLengthFormat.ResolveOwnSizeMeters(owner) > 0.05f;
        bool hasPdf = ElevationLengthFormat.ResolvePdfDataLengthMeters(owner) > 0.05f;

        if (!ElevationLengthGeometry.TryGetLengthMeshBounds(
                owner, catalogM, out Bounds mesh, out string src,
                out Selectable meshSel, out bool meshStrict)
            || mesh.size.sqrMagnitude < 1e-4f)
            return false;

        string rulePrefix = hasSize ? "Size→mesh:" : (hasPdf ? "PdfData→mesh:" : "mesh:");
        src = rulePrefix + src;

        Vector3 a, b, axis;
        float meshSpan;

        // Architect note 2: a catalog dim starts and ends on the cutsheet's own
        // reference points. Mesh extents are only the fallback when the model has no
        // joint pair that realises the catalog length.
        if (ElevationLengthGeometry.TryGetCutsheetReferencePoints(
                owner, catalogM, camera, out Vector3 refA, out Vector3 refB,
                out string refName))
        {
            a = refA;
            b = refB;
            axis = (b - a).sqrMagnitude > 1e-8f ? (b - a).normalized : Vector3.right;
            meshSpan = Vector3.Distance(a, b);
            src = (hasSize ? "Size→ref:" : "PdfData→ref:") + refName;
            return FinishHorizontal(
                owner, catalogM, camera, a, b, axis, meshSpan, src,
                meshSel, meshStrict, snapToCatalog: false, out solution);
        }

        if (ElevationLengthGeometry.TryGetOwnMeshAxisSegment(
                meshSel != null ? meshSel : owner, meshStrict, catalogM,
                maxVerticalDot: 0.9f, camera,
                out a, out b, out axis, out float obbLenM))
        {
            src += "+obb";
            meshSpan = obbLenM;
        }
        else
        {
            axis = ElevationLengthGeometry.ResolveHorizontalAxis(
                owner, mesh, catalogM, camera);
            if (axis.sqrMagnitude < 1e-8f)
                return false;
            axis.Normalize();
            Vector3 center = mesh.center;
            float cAlong = Vector3.Dot(center, axis);
            float meshMin = ElevationLengthGeometry.ProjectBoundsMin(mesh, axis);
            float meshMax = ElevationLengthGeometry.ProjectBoundsMax(mesh, axis);
            a = center + axis * (meshMin - cAlong);
            b = center + axis * (meshMax - cAlong);
            meshSpan = Vector3.Distance(a, b);
        }

        return FinishHorizontal(
            owner, catalogM, camera, a, b, axis, meshSpan, src,
            meshSel, meshStrict, snapToCatalog: true, out solution);
    }

    /// <summary>
    /// Shared tail for both horizontal paths: orient, keep real slope, then make the
    /// drawn page length equal the printed catalog length.
    /// </summary>
    /// <param name="snapToCatalog">
    /// Mesh path re-anchors the span (centreTrim / distalPad). The cutsheet-reference
    /// path must not — its endpoints are the reference points, so it is only rescaled
    /// along its own direction to absorb modelling noise.
    /// </param>
    static bool FinishHorizontal(
        Selectable owner, float catalogM, Camera camera,
        Vector3 a, Vector3 b, Vector3 axis, float meshSpan, string src,
        Selectable meshSel, bool meshStrict, bool snapToCatalog, out Solution solution)
    {
        if (Vector3.Dot(b - a, axis) < 0f)
            (a, b) = (b, a);
        axis = (b - a).sqrMagnitude > 1e-8f ? (b - a).normalized : axis;

        // Level vs pitched has to be decided from the physical part, not from the two
        // reference-point markers: a rigid, visually-level tube's proximal/distal AP
        // markers can each sit a few mm off-centre on their own joint housing (e.g. one
        // parked toward the top of a ball socket, the other toward its side), and that
        // small per-marker offset reads as several degrees of "droop" over the tube's
        // own length even though the mesh runs dead level. The mesh path already derives
        // axis straight off the part's own OBB, so it is already physical truth; only the
        // cutsheet-reference path needs this cross-check, since its axis is two markers'
        // raw delta.
        Vector3 slopeAxis = axis;
        if (!snapToCatalog
            && ElevationLengthGeometry.TryGetOwnMeshAxisSegment(
                meshSel != null ? meshSel : owner, meshStrict, catalogLenMeters: -1f,
                maxVerticalDot: 0.98f, camera,
                out _, out _, out Vector3 meshAxis, out _)
            && meshAxis.sqrMagnitude > 1e-6f)
        {
            slopeAxis = meshAxis;
        }

        float run = new Vector2(slopeAxis.x, slopeAxis.z).magnitude;
        bool pitched = run > 1e-4f && Mathf.Abs(slopeAxis.y) >= run * PitchedArmTanMin;
        if (!pitched)
        {
            axis.y = 0f;
            if (axis.sqrMagnitude < 1e-8f)
                axis = camera != null ? camera.transform.right : Vector3.right;
            axis.y = 0f;
            axis.Normalize();
            float midY = 0.5f * (a.y + b.y);
            a.y = midY;
            b.y = midY;
        }

        string fit;
        if (snapToCatalog)
        {
            Vector3 proximalHint =
                ElevationLengthGeometry.TryResolveProximalAttachment(owner, out var proxAp)
                && proxAp != null
                    ? proxAp.transform.position
                    : (owner.ParentAttachmentPoint != null
                        ? owner.ParentAttachmentPoint.transform.position
                        : owner.transform.position);
            FitHorizontalCatalog(ref a, ref b, axis, catalogM, proximalHint, out fit);
        }
        else
        {
            // Hold the proximal joint (not whichever end axis-align swapped to).
            if (ElevationLengthGeometry.TryResolveProximalAttachment(owner, out var holdAp)
                && holdAp != null)
            {
                Vector3 p = holdAp.transform.position;
                if (Vector3.Distance(b, p) + 1e-4f < Vector3.Distance(a, p))
                    (a, b) = (b, a);
            }
            float page = camera != null
                ? Vector3.ProjectOnPlane(b - a, camera.transform.forward).magnitude
                : Vector3.Distance(a, b);
            if (page > 1e-4f)
                b = a + (b - a) * (catalogM / page);
            fit = "cutsheetRef";
        }

        float pageSpanM = Vector3.Distance(a, b);
        if (camera != null)
            pageSpanM = Vector3.ProjectOnPlane(b - a, camera.transform.forward).magnitude;

        Debug.Log(
            $"[ElevDim] FIT owner={owner.name} catalogMm={Mathf.RoundToInt(catalogM * 1000f)} " +
            $"fit={fit} src={src} meshSpanMm={Mathf.RoundToInt(meshSpan * 1000f)} " +
            $"pageSpanMm={Mathf.RoundToInt(pageSpanM * 1000f)} pitched={pitched} " +
            $"slopeAxis={slopeAxis:F3} a={a:F3} b={b:F3}");

        ElevationLengthGeometry.RememberClaimEndpoints(owner, a, b);
        solution = new Solution(
            a, b, axis, vertical: false,
            PartKind.HorizontalArm, src, pitched ? fit + "(aligned)" : fit,
            meshSpan, pageSpanM, ceilingGapM: 0f);
        return true;
    }

    /// <summary>
    /// The drawn span is always the printed catalog length; the mesh only decides where
    /// it is anchored. Mesh at or over catalog → centre it (ticks stay on part). Mesh
    /// shorter but close → distalPad from the proximal end, never into the parent arm.
    /// </summary>
    public static void FitHorizontalCatalog(
        ref Vector3 a, ref Vector3 b, Vector3 axis, float catalogM, out string fit)
        => FitHorizontalCatalog(ref a, ref b, axis, catalogM, proximalHint: default, out fit);

    public static void FitHorizontalCatalog(
        ref Vector3 a, ref Vector3 b, Vector3 axis, float catalogM,
        Vector3 proximalHint, out string fit)
    {
        Vector3 spanAxis = b - a;
        if (spanAxis.sqrMagnitude < 1e-8f)
            spanAxis = axis;
        if (spanAxis.sqrMagnitude < 1e-8f)
        {
            fit = "degenerate";
            return;
        }
        spanAxis.Normalize();

        float meshSpan = Vector3.Distance(a, b);
        bool pitched = Mathf.Abs(a.y - b.y) >= catalogM * 0.08f;
        float y = 0.5f * (a.y + b.y);

        bool hasJoint = proximalHint != default;
        bool aIsProx = !hasJoint
            || Vector3.Distance(a, proximalHint) <= Vector3.Distance(b, proximalHint);
        Vector3 proxEnd = aIsProx ? a : b;
        Vector3 outward = aIsProx ? spanAxis : -spanAxis;

        // "Close enough to be the same part" scales with the part. A flat 20 mm called
        // the 961 mm powered tube short of its 1000 mm catalog and grew it out of the
        // shoulder pivot, parking the tick ~240 mm off the dome.
        float matchTol = Mathf.Max(CatalogMatchTolM, catalogM * 0.05f);

        if (meshSpan + matchTol < catalogM)
        {
            // Only when the member is genuinely shorter than its catalog length do we
            // have to invent run. Manufacturer sheets tick the joint centres, so grow
            // out of the pivot rather than a shell corner; centerPad stole ~110 mm into
            // BoomSegment_1-2 on PoweredXL.
            Vector3 anchor = proxEnd;
            string mode = "distalPad";
            if (hasJoint)
            {
                Vector3 onAxis = proxEnd + outward * Vector3.Dot(proximalHint - proxEnd, outward);
                // A stray AP elsewhere in the claim must not drag the dim off the part.
                if (Vector3.Distance(onAxis, proxEnd) <= Mathf.Max(0.05f, catalogM * 0.15f))
                {
                    anchor = onAxis;
                    mode = "jointAnchor";
                }
            }

            if (meshSpan >= catalogM * 0.85f)
            {
                a = anchor;
                b = anchor + outward * catalogM;
                if (!pitched)
                {
                    a.y = y;
                    b.y = y;
                }
                fit = mode;
                return;
            }
            fit = "keepMesh(short)";
            return;
        }

        if (hasJoint && meshSpan > catalogM + matchTol)
        {
            // The shell runs well past its catalog length (BoomSegment_1-2 measures
            // 732 mm against a 600 mm arm), so centring floats both ticks in the middle
            // of the tube with neither on a feature. Read off the pivot instead.
            Vector3 onAxis = proxEnd + outward * Vector3.Dot(proximalHint - proxEnd, outward);
            if (Vector3.Distance(onAxis, proxEnd) <= Mathf.Max(0.05f, catalogM * 0.15f))
            {
                a = onAxis;
                b = onAxis + outward * catalogM;
                if (!pitched)
                {
                    a.y = y;
                    b.y = y;
                }
                fit = "jointAnchor(trim)";
                return;
            }
        }

        Vector3 mid = 0.5f * (a + b);
        a = mid - spanAxis * (0.5f * catalogM);
        b = mid + spanAxis * (0.5f * catalogM);
        if (!pitched)
        {
            a.y = y;
            b.y = y;
        }
        fit = "centerTrim";
    }
}
