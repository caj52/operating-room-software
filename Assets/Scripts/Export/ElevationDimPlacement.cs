using UnityEngine;
using TMPro;

/// <summary>
/// Single pathway for elevation cutsheet dim presentation.
/// Line anchors (underside / floor top) and label gap live here — not scattered
/// across Measurable / Measurer / MeasurementText.
/// </summary>
public static class ElevationDimPlacement
{
    /// <summary>
    /// Clearance from dim line to the nearest edge of the mm glyph.
    /// Sized so rotated vertical labels clear the dimension line itself.
    /// </summary>
    public const float LabelGapMeters = 0.035f;

    /// <summary>Half-length of floor/tube end ticks (independent of label gap).</summary>
    public const float TickHalfLengthMeters = 0.06f;

    /// <summary>
    /// Clearance outside the part AABB before the dim line.
    /// </summary>
    public const float AssemblyClearPadMeters = 0.06f;

    /// <summary>Default offset from the part for the first length dim lane.</summary>
    public const float CutsheetLaneBaseMeters = 0.14f;

    /// <summary>Lane step between stacked length dims of the same orientation.</summary>
    public const float CutsheetLaneStepMeters = 0.22f;

    /// <summary>Decorative PDF floor top (matches ground graphic).</summary>
    public static float FloorTopY() => ElevationRoomFrame.ComputeFloorTopY();

    /// <summary>Room ceiling underside — top of the locked elevation photo frame.</summary>
    public static float CeilingUndersideY() => ElevationRoomFrame.Current.CeilingY;

    /// <summary>
    /// Clearance reserved under the ceiling / above the floor for dim labels inside the
    /// locked floor→ceiling photo (leaders must not run off the top of the RT crop).
    /// </summary>
    public const float CutsheetInFrameMarginMeters = 0.12f;

    static float? _ceilingHardwareYCache;

    /// <summary>Clear per-export caches (call once at cutsheet Apply start).</summary>
    public static void BeginCutsheetPass()
    {
        _ceilingHardwareYCache = null;
    }

    /// <summary>
    /// Lowest Y of ceiling plates / covers / flanges / mounts so horizontal arm labels
    /// do not sit on the tandem plate band. Drop tubes excluded (tips hang into arm zone).
    /// </summary>
    public static float CeilingHardwareClearanceY()
    {
        if (_ceilingHardwareYCache.HasValue)
            return _ceilingHardwareYCache.Value;

        float ceiling = CeilingUndersideY();
        float lowest = ceiling;
        bool any = false;

        foreach (var sel in Object.FindObjectsByType<Selectable>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (sel == null || !sel.gameObject.activeInHierarchy)
                continue;
            if (Measurable.IsDropTubeName(sel.name))
                continue;
            if (!ElevationLengthGeometry.LooksLikeCeilingPlatePublic(sel, ceiling))
                continue;

            if (!Measurable.TryGetOwnRendererBounds(sel, out Bounds b) || b.size.y < 0.005f)
                continue;
            if (b.max.y < ceiling - 0.5f)
                continue;
            if (!any || b.min.y < lowest)
            {
                lowest = b.min.y;
                any = true;
            }
        }

        float result = any ? lowest : ceiling;
        _ceilingHardwareYCache = result;
        return result;
    }

    /// <summary>
    /// Lowest equipment underside under a product root.
    /// Product-scoped: meshes owned by the product Selectable (plus rail/shelf accessories).
    /// Nested product heads are excluded — SH+monitor previously shared one xz when
    /// allRenderersUnderRoot skipped Selectable filtering.
    /// </summary>
    public static Vector3 UndersideOrigin(
        Transform partRoot,
        Vector3 fallback,
        Selectable productOwner,
        bool includeAccessories = true)
    {
        if (partRoot == null)
            return fallback;

        Renderer lowestRenderer = null;
        float lowestBoundsY = float.MaxValue;
        Vector3 lowestMeshPoint = default;
        bool haveMeshPoint = false;

        foreach (var r in partRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            if (r.bounds.size.sqrMagnitude < 1e-8f)
                continue;
            if (!RendererBelongsToProductFloor(r, productOwner, includeAccessories))
                continue;

            if (r.bounds.min.y < lowestBoundsY)
            {
                lowestBoundsY = r.bounds.min.y;
                lowestRenderer = r;
            }

            if (TryLowestMeshVertex(r, out Vector3 meshPt))
            {
                if (!haveMeshPoint || meshPt.y < lowestMeshPoint.y)
                {
                    lowestMeshPoint = meshPt;
                    haveMeshPoint = true;
                }
            }
        }

        if (lowestRenderer == null)
            return fallback;

        Bounds lb = lowestRenderer.bounds;
        Vector3 tip = haveMeshPoint
            ? lowestMeshPoint
            : new Vector3(lb.center.x, lb.min.y, lb.center.z);

        Physics.SyncTransforms();

        float floorTop = FloorTopY();
        float bestY = float.MaxValue;
        Vector3 best = tip;
        bool hitAny = false;

        const int samples = 5;
        for (int ix = 0; ix < samples; ix++)
        {
            for (int iz = 0; iz < samples; iz++)
            {
                float u = ix / (float)(samples - 1);
                float v = iz / (float)(samples - 1);
                float x = Mathf.Lerp(lb.min.x, lb.max.x, u);
                float z = Mathf.Lerp(lb.min.z, lb.max.z, v);
                Vector3 start = new Vector3(x, floorTop + 0.02f, z);
                float maxDist = Mathf.Max(0.05f, lb.max.y - floorTop + 0.5f);
                if (!TryHitOwnSurface(start, Vector3.up, maxDist, partRoot, productOwner,
                        includeAccessories, out Vector3 surface))
                    continue;
                hitAny = true;
                if (surface.y < bestY)
                {
                    bestY = surface.y;
                    best = surface;
                }
            }
        }

        float visualMinY = haveMeshPoint ? lowestMeshPoint.y : lb.min.y;
        if (hitAny)
        {
            if (bestY < visualMinY - 0.015f || bestY > visualMinY + 0.06f)
                return tip;
            return best;
        }

        return tip;
    }

    public static bool RendererBelongsToProductFloor(
        Renderer r, Selectable productOwner, bool includeAccessories)
    {
        if (r == null)
            return false;
        if (productOwner == null)
            return true;

        var nearest = r.GetComponentInParent<Selectable>(true);
        if (nearest == null)
            return r.transform.IsChildOf(productOwner.transform)
                || r.transform == productOwner.transform;
        if (nearest == productOwner)
            return true;
        if (!nearest.transform.IsChildOf(productOwner.transform))
            return false;

        // Arms / drop tubes never contribute to a product-head underside.
        if (Measurable.IsSkippedMidArmFloorName(nearest))
            return false;
        if (Measurable.IsDropTubeName(nearest.name)
            || Measurable.IsVerticalHangLengthName(nearest.name))
            return false;

        // Light product: include yoke/head child Selectables under the same light root.
        if (Measurable.IsLightProductFloorOwner(productOwner))
        {
            if (Measurable.IsMonitorBoomProductHead(nearest))
                return false;
            if (Measurable.TryGetServiceHeadFloorRoot(nearest, out _)
                && !Measurable.IsLightProductFloorOwner(nearest))
                return false;
            return true;
        }

        // Monitor boom head: only this head (+ non-product children), never a sibling SH.
        if (Measurable.IsMonitorBoomProductHead(productOwner))
        {
            if (Measurable.IsFloorClearanceProductHead(nearest)
                && !Measurable.IsMonitorBoomProductHead(nearest))
                return false;
            if (includeAccessories && Measurable.IsServiceHeadAccessoryDimOwner(nearest))
                return true;
            return ElevationLengthFormat.ResolveOwnSizeMeters(nearest) <= 0f;
        }

        // Service-head cabinet: include body/rails/shelves under BoomHeadScaleHandler.
        // (Spring-arm products are not floor owners — see IsFloorClearanceProductHead.)
        if (productOwner.GetComponent<BoomHeadScaleHandler>() != null
            || Measurable.TryGetServiceHeadFloorRoot(productOwner, out _))
        {
            if (Measurable.IsMonitorBoomProductHead(nearest))
                return false;
            if (Measurable.IsLightProductFloorOwner(nearest))
                return false;
            return true;
        }

        // Fallback: accessories + non-length children.
        if (Measurable.IsFloorClearanceProductHead(nearest))
            return false;
        if (includeAccessories && Measurable.IsServiceHeadAccessoryDimOwner(nearest))
            return true;
        return ElevationLengthFormat.ResolveOwnSizeMeters(nearest) <= 0f;
    }

    /// <summary>Legacy callers — resolve product Selectable, always product-scoped.</summary>
    public static Vector3 UndersideOrigin(
        Transform partRoot,
        Vector3 fallback,
        bool allowParentSelectable = true,
        bool allRenderersUnderRoot = false)
    {
        Selectable product = null;
        if (partRoot != null)
        {
            product = partRoot.GetComponent<Selectable>();
            if (product == null && allowParentSelectable)
                product = partRoot.GetComponentInParent<Selectable>();
        }
        return UndersideOrigin(partRoot, fallback, product, includeAccessories: true);
    }

    /// <summary>
    /// World-space lowest mesh vertex for a MeshFilter renderer. Skinned/other → false.
    /// </summary>
    static bool TryLowestMeshVertex(Renderer r, out Vector3 worldPoint)
    {
        worldPoint = default;
        if (r == null)
            return false;

        Mesh mesh = null;
        Transform t = r.transform;
        if (r is MeshRenderer)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null)
                mesh = mf.sharedMesh;
        }
        if (mesh == null || mesh.vertexCount == 0)
            return false;

        var verts = mesh.vertices;
        float bestY = float.MaxValue;
        Vector3 bestLocal = Vector3.zero;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 w = t.TransformPoint(verts[i]);
            if (w.y < bestY)
            {
                bestY = w.y;
                bestLocal = verts[i];
            }
        }
        if (bestY >= float.MaxValue * 0.5f)
            return false;
        worldPoint = t.TransformPoint(bestLocal);
        return true;
    }

    public static bool TryHitOwnSurface(
        Vector3 start, Vector3 dir, float maxDist, Transform root, Selectable productOwner,
        bool includeAccessories, out Vector3 point)
    {
        point = start;
        var hits = Physics.RaycastAll(start, dir, maxDist, ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null)
                continue;
            if (hit.collider.GetComponentInParent<RoomBoundary>() != null)
                continue;
            var t = hit.collider.transform;
            if (t != root && !t.IsChildOf(root))
                continue;
            if (productOwner != null)
            {
                var nearest = t.GetComponentInParent<Selectable>(true);
                if (nearest == productOwner)
                {
                    // ok
                }
                else if (nearest != null
                    && nearest.transform.IsChildOf(productOwner.transform)
                    && includeAccessories
                    && Measurable.IsServiceHeadAccessoryDimOwner(nearest))
                {
                    // ok — rail/shelf under product
                }
                else if (nearest == null
                    && (t.IsChildOf(productOwner.transform) || t == productOwner.transform))
                {
                    // ok — mesh with no Selectable
                }
                else
                    continue;
            }
            point = hit.point;
            return true;
        }
        return false;
    }

    /// <summary>Legacy signature — product-scoped hits only.</summary>
    public static bool TryHitOwnSurface(
        Vector3 start, Vector3 dir, float maxDist, Transform root, Selectable ownerSel, out Vector3 point)
    {
        return TryHitOwnSurface(start, dir, maxDist, root, ownerSel, includeAccessories: true, out point);
    }

    /// <summary>
    /// Place cutsheet mm label beside/above the dim line.
    /// Floor lane flips side; horizontal dims near the ceiling lock place text below
    /// the line so room-locked framing cannot crop the glyph.
    /// </summary>
    public static void PlaceLabel(
        Transform label,
        TextMeshProUGUI text,
        Vector3 onLine,
        Vector3 dimDirection,
        Camera camera,
        bool isFloor,
        int floorLane,
        bool preferLabelBelow = false,
        float verticalOutboardSign = 0f,
        Vector3 dimSpanA = default,
        Vector3 dimSpanB = default)
    {
        if (label == null || camera == null)
            return;

        Vector3 dimDir = dimDirection.sqrMagnitude > 1e-8f ? dimDirection.normalized : Vector3.down;
        Vector3 camUp = camera.transform.up;
        Vector3 camRight = camera.transform.right;
        bool horizontalOnPage =
            Mathf.Abs(Vector3.Dot(dimDir, camRight)) >= Mathf.Abs(Vector3.Dot(dimDir, camUp));
        Vector3 perp = horizontalOnPage ? camUp : camRight;
        if (perp.sqrMagnitude < 1e-8f)
            perp = Vector3.up;
        perp.Normalize();

        float sideSign = 1f;
        if (!horizontalOnPage && isFloor)
            sideSign = (Mathf.Max(0, floorLane) % 2 == 0) ? 1f : -1f;
        else if (!horizontalOnPage && Mathf.Abs(verticalOutboardSign) > 0.1f)
            sideSign = Mathf.Sign(verticalOutboardSign);
        else if (horizontalOnPage && preferLabelBelow)
            sideSign = -1f;

        // Near-edge clearance: put the closest glyph edge exactly LabelGapMeters from
        // the line. Using halfExtent assumed symmetric bounds and made long labels
        // ("1772 mm") sit much farther out than short ones ("150 mm").
        float minAlong = 0f;
        float maxAlong = 0f;
        Vector3[] localCorners = null;
        RectTransform rt = null;
        if (text != null)
        {
            rt = text.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            label.position = onLine;
            text.ForceMeshUpdate();
            Bounds glyphBounds = text.textBounds;
            Vector3 c = glyphBounds.center;
            Vector3 e = glyphBounds.extents;
            localCorners = new[]
            {
                c + new Vector3(-e.x, -e.y, 0f),
                c + new Vector3(-e.x,  e.y, 0f),
                c + new Vector3( e.x, -e.y, 0f),
                c + new Vector3( e.x,  e.y, 0f),
            };
            minAlong = float.MaxValue;
            maxAlong = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float d = Vector3.Dot(rt.TransformPoint(localCorners[i]) - onLine, perp);
                minAlong = Mathf.Min(minAlong, d);
                maxAlong = Mathf.Max(maxAlong, d);
            }
            if (minAlong > maxAlong)
            {
                minAlong = 0f;
                maxAlong = 0f;
            }
        }

        float nearEdge = sideSign > 0f ? minAlong : maxAlong;
        float shift = sideSign * LabelGapMeters - nearEdge;
        label.position = onLine + perp * shift;

        if (!isFloor && !horizontalOnPage)
            ClampLabelInsideDimSpan(label, text, dimSpanA, dimSpanB);
    }

    /// <summary>
    /// Short vertical dims: rotated "100 mm" is taller than half the span, so a centered
    /// label parks glyph tops in the ceiling plate. Keep corners inside the dim Y span.
    /// </summary>
    public static void ClampLabelInsideDimSpan(
        Transform label, TextMeshProUGUI text, Vector3 dimSpanA, Vector3 dimSpanB)
    {
        if (label == null || text == null || (dimSpanA - dimSpanB).sqrMagnitude < 1e-6f)
            return;

        var rt = text.rectTransform;
        text.ForceMeshUpdate();
        Bounds glyphBounds = text.textBounds;
        Vector3 c = glyphBounds.center;
        Vector3 e = glyphBounds.extents;
        Vector3[] localCorners =
        {
            c + new Vector3(-e.x, -e.y, 0f),
            c + new Vector3(-e.x,  e.y, 0f),
            c + new Vector3( e.x, -e.y, 0f),
            c + new Vector3( e.x,  e.y, 0f),
        };

        float spanMinY = Mathf.Min(dimSpanA.y, dimSpanB.y) + 0.008f;
        float spanMaxY = Mathf.Max(dimSpanA.y, dimSpanB.y) - 0.008f;
        if (spanMaxY <= spanMinY)
            return;

        float gMinY = float.MaxValue;
        float gMaxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float y = rt.TransformPoint(localCorners[i]).y;
            gMinY = Mathf.Min(gMinY, y);
            gMaxY = Mathf.Max(gMaxY, y);
        }

        float dy = 0f;
        if (gMaxY > spanMaxY)
            dy = spanMaxY - gMaxY;
        else if (gMinY < spanMinY)
            dy = spanMinY - gMinY;
        if (Mathf.Abs(dy) > 1e-5f)
            label.position += Vector3.up * dy;
    }
}
