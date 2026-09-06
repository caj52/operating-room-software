using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mesh/column bounds helpers for elevation length dims.
/// Policy (ceiling flush, catalog fit, head-on skip) lives in <see cref="ElevationDimSolver"/>.
/// </summary>
public static class ElevationLengthGeometry
{
    public static bool IsCatalogLengthDimOwner(Selectable sel) =>
        ElevationDimPolicy.IsCatalogLengthOwner(sel);

    public static bool IsGeometricLengthJoint(Selectable sel)
    {
        if (sel == null)
            return false;
        if (ElevationDimPolicy.IsCatalogLengthOwner(sel))
            return false;
        if (sel.GetLengthScaleKind() != LengthScaleKind.LengthTube)
            return false;
        if (Measurable.IsServiceHeadAccessoryDimOwner(sel))
            return false;
        string n = sel.name ?? "";
        if (n.IndexOf("Cardanic", System.StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return Measurable.ResolveGeometricLengthMeters(sel) >= 0.03f;
    }

    /// <summary>
    /// Drop/ceiling tubes by name, or mesh height explains catalog better than xz.
    /// Does not use transform.forward (elevation align breaks it).
    /// </summary>
    public static bool IsVerticalTube(Selectable owner, float catalogLenMeters)
    {
        if (owner == null || catalogLenMeters <= 0f)
            return false;

        string n = owner.name ?? "";
        if (Measurable.IsDropTubeName(n) || Measurable.IsVerticalHangLengthName(n))
            return true;
        if (Measurable.IsForcedHorizontalCatalogArm(owner))
            return false;

        if (!TryGetLengthMeshBounds(owner, out Bounds rb))
            return false;

        float y = rb.size.y;
        float xz = Mathf.Max(rb.size.x, rb.size.z);
        float yErr = Mathf.Abs(y - catalogLenMeters);
        float xzErr = Mathf.Abs(xz - catalogLenMeters);
        if (y >= catalogLenMeters * 0.45f && yErr + 0.02f < xzErr)
            return true;
        if (xz >= catalogLenMeters * 0.45f && xzErr + 0.02f <= yErr)
            return false;
        return y >= catalogLenMeters * 0.45f && y > xz * 1.15f;
    }

    /// <summary>
    /// Candidate PdfData length meshes (scored one-by-one, never unioned).
    /// Excludes PoweredXL root, _2_3 fold, and BoomSegment_3.
    /// </summary>
    static bool IsPoweredArmLengthShell(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (name.IndexOf("Powered", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        if (name.IndexOf("BoomSegment_2_3", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        if (name.IndexOf("BoomSegment_3", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        return name.IndexOf("BoomSegment_2", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Proximal joint for a catalog length: only the owner's ParentAttachmentPoint, or a
    /// same-stem dual-select twin's. Never walk ancestors (that grabbed the drop-tube AP
    /// for BoomSegment_1-2 → ticks at x=drop).
    /// </summary>
    public static bool TryResolveProximalAttachment(Selectable owner, out AttachmentPoint proximal)
    {
        proximal = null;
        if (owner == null)
            return false;
        if (owner.ParentAttachmentPoint != null)
        {
            proximal = owner.ParentAttachmentPoint;
            return true;
        }
        if (owner.RelatedSelectables != null)
        {
            string stem = Measurable.DualSelectStem(owner.name);
            foreach (var rel in owner.RelatedSelectables)
            {
                if (rel == null || rel.ParentAttachmentPoint == null)
                    continue;
                if (Measurable.DualSelectStem(rel.name) != stem)
                    continue;
                proximal = rel.ParentAttachmentPoint;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// AP belongs to this length claim if it sits under owner without crossing a nested
    /// catalog-length Selectable (e.g. PoweredXL must not claim BoomSegment_3's APs).
    /// </summary>
    public static bool AttachmentPointInLengthClaim(AttachmentPoint ap, Selectable lengthOwner)
    {
        if (ap == null || lengthOwner == null)
            return false;
        var nearest = ap.GetComponentInParent<Selectable>(true);
        if (nearest == null)
            return false;
        Transform t = nearest.transform;
        while (t != null)
        {
            if (t.TryGetComponent(out Selectable s))
            {
                if (s == lengthOwner)
                    return true;
                if (s != nearest && ElevationDimPolicy.IsCatalogLengthOwner(s))
                    return false;
            }
            if (t == lengthOwner.transform)
                return true;
            t = t.parent;
        }
        return false;
    }

    /// <summary>
    /// Distal joint for axis direction only (length comes from catalog at solve time).
    /// Prefer the farthest nested catalog-length child's ParentAttachmentPoint; else
    /// farthest in-claim AP along owner forward. Never pick by catalog-distance match.
    /// </summary>
    public static AttachmentPoint FindDistalAttachmentPoint(
        Selectable owner, AttachmentPoint proximal, float catalogM = -1f)
    {
        // catalogM reserved for callers/diagnostics — distal is direction-only.
        _ = catalogM;
        if (owner == null)
            return null;

        Vector3 origin = proximal != null
            ? proximal.transform.position
            : owner.transform.position;

        AttachmentPoint bestChildJoint = null;
        float bestDist = 0.05f;

        foreach (var child in owner.GetComponentsInChildren<Selectable>(true))
        {
            if (child == null || child == owner)
                continue;
            if (!ElevationDimPolicy.IsCatalogLengthOwner(child))
                continue;
            var joint = child.ParentAttachmentPoint;
            if (joint == null || joint == proximal)
                continue;
            if (!joint.transform.IsChildOf(owner.transform))
                continue;
            Vector3 flat = joint.transform.position - origin;
            flat.y = 0f;
            float d = flat.magnitude;
            if (d > bestDist)
            {
                bestDist = d;
                bestChildJoint = joint;
            }
        }
        if (bestChildJoint != null)
            return bestChildJoint;

        Vector3 axis = owner.transform.TransformDirection(Vector3.forward);
        axis.y = 0f;
        if (axis.sqrMagnitude < 1e-8f)
            axis = Vector3.right;
        axis.Normalize();

        AttachmentPoint best = null;
        float bestAlong = 0.05f;
        foreach (var ap in owner.GetComponentsInChildren<AttachmentPoint>(true))
        {
            if (ap == null || ap == proximal)
                continue;
            string n = ap.name ?? "";
            if (n.IndexOf("Sphere", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (n.IndexOf("Measur", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (!AttachmentPointInLengthClaim(ap, owner))
                continue;
            float along = Mathf.Abs(Vector3.Dot(ap.transform.position - origin, axis));
            if (along > bestAlong)
            {
                bestAlong = along;
                best = ap;
            }
        }
        return best;
    }

    /// <summary>
    /// The cutsheet's own reference points on the model: the proximal mount joint, and
    /// the in-claim joint that actually sits the catalog length away on the sheet.
    /// <para>
    /// Architect review note 2 — a catalog dim has to start and end where the cutsheet
    /// measures it. A cutsheet length is by definition the separation of two joints, so
    /// the distal reference is identified by that separation rather than by "farthest"
    /// or by a fixed child name: the fold joint moves with the pose and is the wrong
    /// point in one boom and the right one in another.
    /// </para>
    /// </summary>
    public static bool TryGetCutsheetReferencePoints(
        Selectable owner, float catalogM, Camera camera,
        out Vector3 proximal, out Vector3 distal, out string refName)
    {
        proximal = distal = default;
        refName = "none";
        if (owner == null || catalogM <= 0.05f)
            return false;
        if (!TryResolveProximalAttachment(owner, out var proxAp) || proxAp == null)
            return false;

        Vector3 prox = proxAp.transform.position;
        float PageLen(Vector3 delta) => camera != null
            ? Vector3.ProjectOnPlane(delta, camera.transform.forward).magnitude
            : delta.magnitude;

        AttachmentPoint best = null;
        float bestErr = float.MaxValue;

        foreach (var ap in owner.GetComponentsInChildren<AttachmentPoint>(true))
        {
            if (ap == null || ap == proxAp)
                continue;
            string n = ap.name ?? "";
            if (n.IndexOf("Sphere", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Measur", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (!AttachmentPointInLengthClaim(ap, owner))
                continue;

            Vector3 delta = ap.transform.position - prox;
            float pageLen = PageLen(delta);
            if (pageLen < 0.05f)
                continue;
            float err = Mathf.Abs(pageLen - catalogM);
            if (err < bestErr)
            {
                bestErr = err;
                best = ap;
            }
        }

        float tol = Mathf.Max(0.025f, catalogM * 0.06f);
        if (best == null || bestErr > tol)
            return false;

        proximal = prox;
        distal = best.transform.position;
        var bestHost = best.GetComponentInParent<Selectable>(true);
        refName = bestHost != null ? bestHost.name : best.name;
        return true;
    }

    static readonly Dictionary<int, (Vector3 a, Vector3 b)> ClaimEndpointsByOwnerId =
        new Dictionary<int, (Vector3 a, Vector3 b)>();

    public static void ClearClaimEndpointCache() => ClaimEndpointsByOwnerId.Clear();

    public static void RememberClaimEndpoints(Selectable owner, Vector3 a, Vector3 b)
    {
        if (owner == null)
            return;
        ClaimEndpointsByOwnerId[owner.GetInstanceID()] = (a, b);
    }

    public static bool TryGetRememberedClaimEndpoints(
        Selectable owner, out Vector3 a, out Vector3 b)
    {
        a = b = default;
        if (owner == null)
            return false;
        if (!ClaimEndpointsByOwnerId.TryGetValue(owner.GetInstanceID(), out var pair))
            return false;
        a = pair.a;
        b = pair.b;
        return true;
    }

    /// <summary>
    /// Render bounds for tick ownership: owner meshes plus descendant shells that are not
    /// themselves catalog-length owners (keeps PoweredXL elbow in-claim, excludes BoomSegment_3).
    /// </summary>
    public static bool TryGetClaimPartBounds(Selectable owner, out Bounds bounds)
    {
        bounds = default;
        if (owner == null)
            return false;
        bool any = false;
        foreach (var r in owner.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            if (r.bounds.size.sqrMagnitude < 1e-4f)
                continue;
            var nearest = r.GetComponentInParent<Selectable>(true);
            if (nearest == null)
                continue;
            if (nearest != owner && ElevationDimPolicy.IsCatalogLengthOwner(nearest))
                continue;
            if (!any)
            {
                bounds = r.bounds;
                any = true;
            }
            else
                bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    /// <summary>Legacy entry — prefer <see cref="ElevationDimSolver.TrySolve"/>.</summary>
    public static bool TryGetLengthEndpoints(
        Selectable owner,
        float catalogLen,
        bool vertical,
        out Vector3 featureA,
        out Vector3 featureB,
        out Vector3 axis,
        out string proximalName,
        out string distalName)
    {
        featureA = featureB = default;
        axis = Vector3.forward;
        proximalName = distalName = "";
        if (!ElevationDimSolver.TrySolve(
                owner, catalogLen, camera: null, layoutVertical: vertical,
                out var sol))
            return false;
        featureA = sol.A;
        featureB = sol.B;
        axis = sol.Axis;
        proximalName = sol.Vertical ? "top" : "near";
        distalName = sol.Vertical ? "tip" : "far";
        return true;
    }

    public static bool TryGetLengthMeshBounds(Selectable owner, out Bounds bounds)
        => TryGetLengthMeshBounds(owner, catalogLenMeters: -1f, out bounds, out _, out _, out _);

    public static bool TryGetLengthMeshBounds(
        Selectable owner, float catalogLenMeters, out Bounds bounds, out string source)
        => TryGetLengthMeshBounds(owner, catalogLenMeters, out bounds, out source, out _, out _);

    /// <summary>
    /// Modeling-element mesh for a catalog length. PdfData shells are scored one-by-one
    /// (never unioned). <paramref name="meshOwner"/> is the Selectable that mesh lives on,
    /// and <paramref name="meshStrict"/> says whether the winning bounds came from that
    /// Selectable alone or from its dual-select group — the axis pass has to rebuild the
    /// same mesh set or it measures a stub instead of the arm.
    /// </summary>
    public static bool TryGetLengthMeshBounds(
        Selectable owner, float catalogLenMeters, out Bounds bounds, out string source,
        out Selectable meshOwner, out bool meshStrict)
    {
        bounds = default;
        source = "none";
        meshOwner = owner;
        meshStrict = true;
        if (owner == null)
            return false;

        Bounds best = default;
        float bestScore = float.MaxValue;
        bool any = false;
        string bestSrc = "none";
        Selectable bestSel = owner;
        bool bestStrict = true;

        void ConsiderKind(string src, Bounds b, Selectable sel, bool strict)
        {
            if (b.size.sqrMagnitude < 1e-4f)
                return;
            float y = b.size.y;
            float xz = Mathf.Max(b.size.x, b.size.z);
            float score = catalogLenMeters > 0.05f
                ? Mathf.Min(Mathf.Abs(y - catalogLenMeters), Mathf.Abs(xz - catalogLenMeters))
                : -Mathf.Max(y, xz);
            if (!any || score < bestScore - 1e-4f
                || (Mathf.Abs(score - bestScore) < 1e-4f
                    && Mathf.Max(y, xz) > Mathf.Max(best.size.y, Mathf.Max(best.size.x, best.size.z))))
            {
                best = b;
                bestScore = score;
                bestSrc = src;
                bestSel = sel != null ? sel : owner;
                bestStrict = strict;
                any = true;
            }
        }

        void Consider(string src, Bounds b, Selectable sel) => ConsiderKind(src, b, sel, true);

        if (Measurable.TryGetStrictOwnRendererBounds(owner, out Bounds strict))
            ConsiderKind("strict:" + owner.name, strict, owner, true);
        if (Measurable.TryGetOwnRendererBounds(owner, out Bounds own))
            ConsiderKind("own:" + owner.name, own, owner, false);
        if (TryGetColumnBounds(owner, out Bounds col))
            ConsiderKind("column:" + owner.name, col, owner, true);

        if (owner.RelatedSelectables != null)
        {
            foreach (var rel in owner.RelatedSelectables)
            {
                if (rel == null || rel == owner)
                    continue;
                if (Measurable.TryGetStrictOwnRendererBounds(rel, out Bounds rs))
                    ConsiderKind("relStrict:" + rel.name, rs, rel, true);
                if (Measurable.TryGetOwnRendererBounds(rel, out Bounds ro))
                    ConsiderKind("relOwn:" + rel.name, ro, rel, false);
            }
        }

        string stem = Measurable.DualSelectStem(owner.name);
        Transform parent = owner.transform.parent;
        if (parent != null && !string.IsNullOrEmpty(stem))
        {
            foreach (var sib in parent.GetComponentsInChildren<Selectable>(true))
            {
                if (sib == null || sib == owner)
                    continue;
                if (Measurable.DualSelectStem(sib.name) != stem)
                    continue;
                if (Measurable.TryGetStrictOwnRendererBounds(sib, out Bounds ss))
                    ConsiderKind("sibStrict:" + sib.name, ss, sib, true);
                if (Measurable.TryGetOwnRendererBounds(sib, out Bounds so))
                    ConsiderKind("sibOwn:" + sib.name, so, sib, false);
            }
        }

        // PdfData root has no Size mesh. Score each length-shell on its own bounds —
        // never encapsulate _2 (housing) with _2_2 (tube).
        if (catalogLenMeters > 0.05f
            && ElevationLengthFormat.ResolveOwnSizeMeters(owner) <= 0f
            && ElevationLengthFormat.ResolvePdfDataLengthMeters(owner) > 0.05f)
        {
            foreach (var s in owner.GetComponentsInChildren<Selectable>(true))
            {
                if (s == null || !IsPoweredArmLengthShell(s.name))
                    continue;
                if (Measurable.TryGetStrictOwnRendererBounds(s, out Bounds shell))
                    Consider("poweredShell:" + s.name, shell, s);
            }
        }

        if (!any)
            return false;
        bounds = best;
        source = bestSrc;
        meshOwner = bestSel;
        meshStrict = bestStrict;
        return true;
    }

    public static bool TryGetColumnOrOwnBounds(Selectable owner, out Bounds bounds)
    {
        if (TryGetColumnBounds(owner, out bounds))
            return true;
        if (Measurable.TryGetStrictOwnRendererBounds(owner, out bounds) && bounds.size.y > 0.02f)
            return true;
        return Measurable.TryGetOwnRendererBounds(owner, out bounds) && bounds.size.y > 0.02f;
    }

    /// <summary>
    /// Posed centerline of a part along one of its own authored axes (oriented box),
    /// with the extent taken from every mesh the part owns.
    /// <para>
    /// A world AABB is not usable here: its support function peaks on box diagonals, so
    /// a pitched tube measured that way reports catalog length along a direction the
    /// part does not run in (the 60° dim drawn across a 27° arm). Choosing among the
    /// part's three authored axes cannot inflate that way — the length is only ever the
    /// mesh's own extent, whatever the pose.
    /// </para>
    /// </summary>
    /// <param name="maxVerticalDot">
    /// Reject authored axes this close to vertical. A drooping boom is still a
    /// horizontal arm and must keep its real slope, so this only rules out axes that
    /// run essentially up the page — not merely steep ones.
    /// </param>
    public static bool TryGetOwnMeshAxisSegment(
        Selectable sel, bool strict, float catalogLenMeters, float maxVerticalDot,
        Camera camera, out Vector3 a, out Vector3 b, out Vector3 axis, out float lenM)
    {
        a = b = default;
        axis = Vector3.right;
        lenM = 0f;
        if (sel == null)
            return false;

        // Same mesh set the winning bounds came from. Gathering only sel's own meshes
        // measured a 111 mm stub on a dual-select arm and drew a zero-length 800 mm dim.
        var owners = new HashSet<Selectable> { sel };
        if (!strict)
        {
            string stem = Measurable.DualSelectStem(sel.name);
            if (!string.IsNullOrEmpty(stem))
            {
                if (sel.RelatedSelectables != null)
                {
                    foreach (var rel in sel.RelatedSelectables)
                    {
                        if (rel != null && Measurable.DualSelectStem(rel.name) == stem)
                            owners.Add(rel);
                    }
                }
                Transform parent = sel.transform.parent;
                if (parent != null)
                {
                    foreach (var sib in parent.GetComponentsInChildren<Selectable>(true))
                    {
                        if (sib != null && Measurable.DualSelectStem(sib.name) == stem)
                            owners.Add(sib);
                    }
                }
            }
        }

        var meshes = new List<MeshFilter>();
        foreach (var root in owners)
        {
            if (root == null)
                continue;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null)
                    continue;
                if (!mf.gameObject.activeInHierarchy)
                    continue;
                var nearest = mf.GetComponentInParent<Selectable>(true);
                if (nearest == null || !owners.Contains(nearest))
                    continue;
                string n = mf.name ?? "";
                if (n.IndexOf("Sphere", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Measur", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Decal", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Logo", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (!meshes.Contains(mf))
                    meshes.Add(mf);
            }
        }
        if (meshes.Count == 0)
            return false;

        Vector3 centroid = Vector3.zero;
        foreach (var mf in meshes)
            centroid += mf.transform.TransformPoint(mf.sharedMesh.bounds.center);
        centroid /= meshes.Count;

        // Union extent along a direction, from oriented corners (no AABB inflation).
        void Extent(Vector3 dir, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (var mf in meshes)
            {
                Bounds lb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = lb.center + Vector3.Scale(
                        lb.extents,
                        new Vector3((c & 1) == 0 ? -1f : 1f,
                                    (c & 2) == 0 ? -1f : 1f,
                                    (c & 4) == 0 ? -1f : 1f));
                    float along = Vector3.Dot(
                        mf.transform.TransformPoint(corner) - centroid, dir);
                    min = Mathf.Min(min, along);
                    max = Mathf.Max(max, along);
                }
            }
        }

        // Candidates are the parts' own authored directions, scored on the union extent
        // — never on a world box, whose support function peaks on diagonals and reported
        // catalog length along a direction the arm does not run in.
        Vector3 bestAxis = Vector3.zero;
        float bestScore = float.MaxValue;
        float bestMin = 0f, bestMax = 0f;
        Vector3[] localAxes = { Vector3.right, Vector3.up, Vector3.forward };

        foreach (var mf in meshes)
        {
            for (int i = 0; i < localAxes.Length; i++)
            {
                Vector3 dir = mf.transform.TransformDirection(localAxes[i]);
                if (dir.sqrMagnitude < 1e-8f)
                    continue;
                dir.Normalize();
                if (maxVerticalDot < 1f
                    && Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > maxVerticalDot)
                    continue;

                Extent(dir, out float mn, out float mx);
                float len = mx - mn;
                if (len < 0.05f)
                    continue;

                float score = catalogLenMeters > 0.05f
                    ? Mathf.Abs(len - catalogLenMeters)
                    : -len;
                // An elevation dim has to be measurable on the sheet; an axis running
                // into the camera drew 800 mm as 0 mm of page.
                if (camera != null && catalogLenMeters > 0.05f)
                {
                    float pageRatio = Vector3.ProjectOnPlane(
                        dir, camera.transform.forward).magnitude;
                    score += catalogLenMeters * (1f - pageRatio) * 2f;
                }
                if (score < bestScore - 1e-4f)
                {
                    bestScore = score;
                    bestAxis = dir;
                    bestMin = mn;
                    bestMax = mx;
                }
            }
        }

        if (bestAxis.sqrMagnitude < 1e-8f || bestMax - bestMin < 0.05f)
            return false;

        a = centroid + bestAxis * bestMin;
        b = centroid + bestAxis * bestMax;
        axis = bestAxis;
        lenM = bestMax - bestMin;
        return true;
    }

    public static Vector3 ResolveHorizontalAxis(Selectable owner, Bounds meshRb, float catalogLen)
        => ResolveHorizontalAxis(owner, meshRb, catalogLen, camera: null);

    /// <summary>
    /// Pick the horizontal axis that best explains catalog length on the mesh, with a
    /// penalty for axes that run into the camera (foreshortened on the elevation page).
    /// </summary>
    public static Vector3 ResolveHorizontalAxis(
        Selectable owner, Bounds meshRb, float catalogLen, Camera camera)
    {
        Vector3[] candidates =
        {
            Vector3.right,
            Vector3.forward,
            new Vector3(1f, 0f, 1f).normalized,
            new Vector3(1f, 0f, -1f).normalized,
        };

        Vector3 camFwd = Vector3.forward;
        bool haveCam = false;
        if (camera != null)
        {
            camFwd = camera.transform.forward;
            camFwd.y = 0f;
            if (camFwd.sqrMagnitude > 1e-6f)
            {
                camFwd.Normalize();
                haveCam = true;
            }
        }

        Vector3 axis = Vector3.right;
        float bestScore = float.MaxValue;
        float bestSpan = 0f;

        void ConsiderOn(Bounds rb, Vector3 c)
        {
            if (c.sqrMagnitude < 1e-8f)
                return;
            c.Normalize();
            float span = ProjectBoundsMax(rb, c) - ProjectBoundsMin(rb, c);
            if (span < 0.05f)
                return;
            float err = catalogLen > 0f ? Mathf.Abs(span - catalogLen) : -span;
            float pageRatio = 1f;
            if (haveCam && span > 1e-6f)
            {
                Vector3 page = Vector3.ProjectOnPlane(c * span, camera.transform.forward);
                pageRatio = page.magnitude / span;
            }
            float score = err + (catalogLen > 0f ? catalogLen * (1f - pageRatio) * 0.85f : 0f);
            if (score < bestScore - 1e-4f
                || (Mathf.Abs(score - bestScore) < 1e-4f && span > bestSpan))
            {
                bestScore = score;
                bestSpan = span;
                axis = c;
            }
        }

        void Consider(Vector3 c) => ConsiderOn(meshRb, c);

        for (int i = 0; i < candidates.Length; i++)
            Consider(candidates[i]);

        if (owner != null)
        {
            Consider(owner.transform.TransformDirection(Vector3.forward));
            Consider(owner.transform.TransformDirection(Vector3.right));
            Vector3 flatFwd = owner.transform.TransformDirection(Vector3.forward);
            flatFwd.y = 0f;
            Consider(flatFwd);
            Vector3 flatRight = owner.transform.TransformDirection(Vector3.right);
            flatRight.y = 0f;
            Consider(flatRight);
        }

        if (haveCam)
        {
            Vector3 pageRight = camera.transform.right;
            pageRight.y = 0f;
            Consider(pageRight);
        }

        return axis;
    }

    /// <summary>
    /// The ceiling cover / tandem plate the flange sits on — the modelling element
    /// Imagine dimensions (review p3, example sheet). Named cover wins over any
    /// geometric slab so we never seat on a trim ring or the empty room plane.
    /// </summary>
    public static bool TryGetCeilingPlateBand(
        float mountX, float mountZ, float ceilingY, out float plateTop, out float plateUnder)
        => TryGetCeilingPlateBand(
            mountX, mountZ, ceilingY, out plateTop, out plateUnder, out _);

    public static bool TryGetCeilingPlateBand(
        float mountX, float mountZ, float ceilingY,
        out float plateTop, out float plateUnder, out string plateName)
    {
        plateTop = ceilingY;
        plateUnder = ceilingY;
        plateName = "none";
        Vector3 mount = new Vector3(mountX, 0f, mountZ);

        Selectable bestCover = null;
        Bounds bestCoverB = default;
        float bestCoverScore = float.MaxValue;

        Selectable bestSlab = null;
        Bounds bestSlabB = default;
        float bestSlabScore = float.MaxValue;

        foreach (var sel in Object.FindObjectsByType<Selectable>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (sel == null || !sel.gameObject.activeInHierarchy)
                continue;
            if (Measurable.IsDropTubeName(sel.name))
                continue;
            if (!TryGetCoverSlabBounds(sel, out Bounds b) || b.size.y < 0.01f)
                continue;

            Vector3 flat = new Vector3(b.center.x, 0f, b.center.z);
            float dist = Vector3.Distance(flat, mount);
            float half = 0.5f * Mathf.Max(b.size.x, b.size.z) + 0.35f;

            if (IsCeilingCoverProduct(sel))
            {
                // Product identity — not a distance-to-room-ceiling gate.
                // Tandem / light covers sit ~150 mm under a 10' deck; a 120–150 mm
                // flush test is what kept hanging the flange from empty air.
                if (dist > Mathf.Max(half, 1.2f))
                    continue;
                float score = dist - Mathf.Max(b.size.x, b.size.z);
                if (score < bestCoverScore)
                {
                    bestCover = sel;
                    bestCoverB = b;
                    bestCoverScore = score;
                }
                continue;
            }

            if (!LooksLikeCeilingPlatePublic(sel, ceilingY)
                && !LooksLikeCeilingPlateLoose(sel, ceilingY))
                continue;
            if (dist > half)
                continue;
            // Unnamed slabs: only those flush to the ceiling (trim rings lose).
            float fromCeil = Mathf.Max(0f, ceilingY - b.max.y);
            if (fromCeil > 0.12f)
                continue;
            float slabScore = fromCeil * 8f + dist * 0.15f;
            if (slabScore < bestSlabScore)
            {
                bestSlab = sel;
                bestSlabB = b;
                bestSlabScore = slabScore;
            }
        }

        if (bestCover != null)
        {
            plateTop = bestCoverB.max.y;
            plateUnder = bestCoverB.min.y;
            plateName = CoverLabel(bestCover);
            return plateUnder < plateTop - 0.01f;
        }

        if (bestSlab != null)
        {
            plateTop = bestSlabB.max.y;
            plateUnder = bestSlabB.min.y;
            plateName = bestSlab.name;
            return plateUnder < plateTop - 0.01f;
        }

        return false;
    }

    /// <summary>
    /// Catalog ceiling-cover products: "Boom - Tandem Ceiling Cover", single covers, etc.
    /// GUID scene names still resolve via UIButtonName / metadata.
    /// </summary>
    public static bool IsCeilingCoverProduct(Selectable sel)
    {
        if (sel == null)
            return false;
        if (Measurable.IsCeilingMountName(sel.name)
            || Measurable.IsCeilingMountName(sel.UIButtonName))
            return true;
        return NameSaysCeilingCover(sel.UIButtonName)
            || NameSaysCeilingCover(sel.name);
    }

    static bool NameSaysCeilingCover(string n)
    {
        if (string.IsNullOrEmpty(n))
            return false;
        if (n.IndexOf("Tandem Ceiling Cover", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("Tandem Cover", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("TandemCover", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("Ceiling Cover", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (n.IndexOf("CeilingCover", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return n.IndexOf("Single Ceiling Cover", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Visible cover / mount slab on a ceiling-cover product — not spheres, cameras,
    /// or the union of every child renderer.
    /// </summary>
    public static bool TryGetCoverSlabBounds(Selectable sel, out Bounds slab)
    {
        slab = default;
        if (sel == null)
            return false;

        bool any = false;
        float bestScore = float.MinValue;
        foreach (var r in sel.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            var nearest = r.GetComponentInParent<Selectable>(true);
            if (nearest != sel)
                continue;

            string n = r.gameObject.name ?? "";
            if (n.IndexOf("Sphere", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (n.IndexOf("Measur", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            float xz = Mathf.Max(r.bounds.size.x, r.bounds.size.z);
            float y = r.bounds.size.y;
            if (xz < 0.08f || y < 0.01f)
                continue;

            bool named = n.IndexOf("Cover", System.StringComparison.OrdinalIgnoreCase) >= 0
                         || n.IndexOf("CeilingMount", System.StringComparison.OrdinalIgnoreCase) >= 0;
            float score = r.bounds.size.x * r.bounds.size.z + (named ? 10f : 0f);
            if (score > bestScore)
            {
                bestScore = score;
                slab = r.bounds;
                any = true;
            }
        }

        return any;
    }

    static string CoverLabel(Selectable sel)
    {
        if (sel == null)
            return "none";
        if (!string.IsNullOrEmpty(sel.UIButtonName))
            return sel.UIButtonName;
        return sel.name;
    }

    /// <summary>GUID / metadata ceiling covers that fail the strict name list.</summary>
    static bool LooksLikeCeilingPlateLoose(Selectable sel, float ceilingY)
    {
        if (sel == null)
            return false;
        if (!Measurable.TryGetOwnRendererBounds(sel, out Bounds b) || b.size.y < 0.01f)
            return false;
        if (b.max.y < ceilingY - 0.35f)
            return false;
        string n = sel.name ?? "";
        string ui = sel.UIButtonName ?? "";
        if (ui.IndexOf("Ceiling", System.StringComparison.OrdinalIgnoreCase) >= 0
            || ui.IndexOf("Tandem", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("Cover", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        float xz = Mathf.Max(b.size.x, b.size.z);
        // Wide flat slab under the ceiling plane.
        return xz > 0.8f && b.size.y < 0.45f && b.size.y > 0.02f && xz > b.size.y * 2f;
    }

    /// <summary>
    /// Ceiling plate / tandem cover whose footprint contains or sits above the tube column.
    /// </summary>
    public static bool TryGetCeilingPlateOverColumn(
        Bounds column, float ceilingY, out Bounds plate)
    {
        plate = default;
        if (!TryGetCeilingPlateBand(
                column.center.x, column.center.z, ceilingY, out float top, out float under))
            return false;
        // Reconstruct a bounds strip for callers that still use this API.
        plate = new Bounds(
            new Vector3(column.center.x, 0.5f * (top + under), column.center.z),
            new Vector3(0.2f, Mathf.Max(0.01f, top - under), 0.2f));
        return true;
    }

    public static bool LooksLikeCeilingPlatePublic(Selectable sel, float ceilingY)
    {
        if (sel == null)
            return false;
        if (!Measurable.TryGetOwnRendererBounds(sel, out Bounds b) || b.size.y < 0.005f)
            return false;
        if (b.max.y < ceilingY - 0.15f)
            return false;

        string n = sel.name ?? "";
        if (Measurable.IsCeilingMountName(n)
            || n.IndexOf("Tandem", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("CeilingCover", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("BoomCieling", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("BoomCeiling", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("CielingFlange", System.StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("CeilingFlange", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        string mn = sel.UIButtonName ?? "";
        if (mn.IndexOf("Tandem", System.StringComparison.OrdinalIgnoreCase) >= 0
            || mn.IndexOf("Ceiling Cover", System.StringComparison.OrdinalIgnoreCase) >= 0
            || mn.IndexOf("CeilingCover", System.StringComparison.OrdinalIgnoreCase) >= 0
            || mn.IndexOf("Ceiling Mount", System.StringComparison.OrdinalIgnoreCase) >= 0
            || mn.IndexOf("Ceiling Flange", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        float xz = Mathf.Max(b.size.x, b.size.z);
        return xz > 0.35f && b.size.y < 0.35f && xz > b.size.y * 1.5f;
    }

    static bool TryGetColumnBounds(Selectable owner, out Bounds bounds)
    {
        if (TryGetColumnLikeFromOwner(owner, out bounds))
            return true;
        if (owner?.RelatedSelectables != null)
        {
            foreach (var rel in owner.RelatedSelectables)
            {
                if (rel != null && TryGetColumnLikeFromOwner(rel, out bounds))
                    return true;
            }
        }
        bounds = default;
        return false;
    }

    static bool TryGetColumnLikeFromOwner(Selectable owner, out Bounds bounds)
    {
        if (Measurable.TryGetStrictOwnRendererBounds(owner, out bounds)
            && bounds.size.y > 0.02f
            && bounds.size.y >= Mathf.Max(bounds.size.x, bounds.size.z) * 0.8f)
            return true;

        bounds = default;
        if (owner == null)
            return false;

        bool any = false;
        foreach (var r in owner.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            if (r.GetComponentInParent<Selectable>(true) != owner)
                continue;
            float h = r.bounds.size.y;
            float xz = Mathf.Max(r.bounds.size.x, r.bounds.size.z);
            if (h < 0.02f || h < xz * 0.8f)
                continue;
            if (!any)
            {
                bounds = r.bounds;
                any = true;
            }
            else
                bounds.Encapsulate(r.bounds);
        }
        return any && bounds.size.y > 0.02f;
    }

    public static float ProjectBoundsMin(Bounds b, Vector3 axis)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;
        float min = float.MaxValue;
        for (int ix = -1; ix <= 1; ix += 2)
        for (int iy = -1; iy <= 1; iy += 2)
        for (int iz = -1; iz <= 1; iz += 2)
            min = Mathf.Min(min, Vector3.Dot(c + new Vector3(e.x * ix, e.y * iy, e.z * iz), axis));
        return min;
    }

    public static float ProjectBoundsMax(Bounds b, Vector3 axis)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;
        float max = float.MinValue;
        for (int ix = -1; ix <= 1; ix += 2)
        for (int iy = -1; iy <= 1; iy += 2)
        for (int iz = -1; iz <= 1; iz += 2)
            max = Mathf.Max(max, Vector3.Dot(c + new Vector3(ix * e.x, iy * e.y, iz * e.z), axis));
        return max;
    }
}
