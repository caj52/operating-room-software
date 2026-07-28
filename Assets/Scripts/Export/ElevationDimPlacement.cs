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
    /// Same for floor clearances and catalog lengths — never lane-stacked.
    /// </summary>
    public const float LabelGapMeters = 0.06f;

    /// <summary>Half-length of floor/tube end ticks (independent of label gap).</summary>
    public const float TickHalfLengthMeters = 0.06f;

    /// <summary>Decorative PDF floor top (matches ground graphic).</summary>
    public static float FloorTopY()
    {
        var floor = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor);
        if (floor == null)
            return 0f;
        return floor.transform.position.y + floor.transform.localScale.y * 0.5f;
    }

    /// <summary>
    /// True underside of <paramref name="partRoot"/> for floor clearance:
    /// cast upward from just above the floor across the footprint and take the
    /// lowest equipment hit. Down-casts from above hit internal colliders and
    /// parked the tick mid-service-head.
    /// </summary>
    public static Vector3 UndersideOrigin(
        Transform partRoot,
        Vector3 fallback,
        bool allowParentSelectable = true,
        bool allRenderersUnderRoot = false)
    {
        if (partRoot == null)
            return fallback;

        // Own selectable only — never child heads/lights under a boom arm (that parked
        // floor ticks mid-service-head when the arm's Floor measurable owned the dim).
        // Lights pass allRenderersUnderRoot so the head is included with the yoke.
        Selectable ownerSel = null;
        if (!allRenderersUnderRoot)
        {
            ownerSel = partRoot.GetComponent<Selectable>();
            if (ownerSel == null && allowParentSelectable)
                ownerSel = partRoot.GetComponentInParent<Selectable>();
            if (ownerSel == null)
            {
                foreach (var s in partRoot.GetComponentsInChildren<Selectable>(true))
                {
                    if (s != null)
                    {
                        ownerSel = s;
                        break;
                    }
                }
            }
        }

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
            if (ownerSel != null)
            {
                var nearest = r.GetComponentInParent<Selectable>(true);
                if (nearest != ownerSel)
                    continue;
            }

            if (r.bounds.min.y < lowestBoundsY)
            {
                lowestBoundsY = r.bounds.min.y;
                lowestRenderer = r;
            }

            // Tilted lights: AABB center at minY is empty space — use the true lowest vertex.
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
        // Prefer real mesh tip; else bottom of the lowest renderer (not combined AABB center).
        Vector3 tip = haveMeshPoint
            ? lowestMeshPoint
            : new Vector3(lb.center.x, lb.min.y, lb.center.z);

        Physics.SyncTransforms();

        float floorTop = FloorTopY();
        float bestY = float.MaxValue;
        Vector3 best = tip;
        bool hitAny = false;

        // Cast only under the lowest renderer's footprint — find surface at the tip.
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
                if (!TryHitOwnSurface(start, Vector3.up, maxDist, partRoot, ownerSel, out Vector3 surface))
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
            // Collider below visible tip (stale articulation) or internal shelf above tip.
            if (bestY < visualMinY - 0.015f || bestY > visualMinY + 0.06f)
                return tip;
            return best;
        }

        return tip;
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
        Vector3 start, Vector3 dir, float maxDist, Transform root, Selectable ownerSel, out Vector3 point)
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
            if (ownerSel != null)
            {
                var nearest = t.GetComponentInParent<Selectable>(true);
                if (nearest != ownerSel)
                    continue;
            }
            point = hit.point;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Place cutsheet mm label beside/above the dim line. One gap for all types;
    /// floor lane only flips side, never inflates distance.
    /// </summary>
    public static void PlaceLabel(
        Transform label,
        TextMeshProUGUI text,
        Vector3 onLine,
        Vector3 dimDirection,
        Camera camera,
        bool isFloor,
        int floorLane)
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

        // Near-edge clearance: put the closest glyph edge exactly LabelGapMeters from
        // the line. Using halfExtent assumed symmetric bounds and made long labels
        // ("1772 mm") sit much farther out than short ones ("150 mm").
        float minAlong = 0f;
        float maxAlong = 0f;
        if (text != null)
        {
            var rt = text.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            label.position = onLine;
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
    }
}
