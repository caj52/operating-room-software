using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Three-phase collider handling for bundle prefabs with convex MeshColliders:
/// 1. <see cref="PrepareCachedPrefab"/> — disable convex hulls on the shared cached template before Instantiate (fast load).
/// 2. <see cref="FinalizeInstanceColliders"/> — interim picking via box fallback while load is in progress.
/// 3. <see cref="RestoreInstanceCollidersAfterLoad"/> — re-enable mesh colliders after load/placement (accurate picking).
/// </summary>
public static class PlacementLoadOptimizer
{
    /// <summary>
    /// When false, only box/primitive fallback colliders are used after load (faster, broken attachment picking).
    /// When true, convex mesh colliders are re-enabled after load completes (slower restore step, correct picking).
    /// </summary>
    public static bool RestoreMeshCollidersAfterLoad { get; set; } = true;

    private static readonly HashSet<int> PreparedPrefabIds = new();
    private static readonly HashSet<int> FallbackBoxColliderIds = new();

    /// <summary>
    /// Phase 1: call once per cached bundle prefab before Instantiate. Mutates the shared template.
    /// </summary>
    public static void PrepareCachedPrefab(GameObject prefab)
    {
        if (prefab == null)
            return;

        if (!PreparedPrefabIds.Add(prefab.GetInstanceID()))
            return;

        int disabled = 0;
        foreach (MeshCollider meshCollider in prefab.GetComponentsInChildren<MeshCollider>(true))
        {
            if (!meshCollider.convex || !meshCollider.enabled)
                continue;

            meshCollider.enabled = false;
            disabled++;
        }

        if (disabled > 0)
        {
            AssetPipelineDiagnostics.Log(
                "PlacementLoad",
                $"Disabled {disabled} convex MeshCollider(s) on cached prefab '{prefab.name}' before Instantiate");
        }
    }

    /// <summary>
    /// Phase 2: interim colliders during load/placement before mesh colliders are restored.
    /// </summary>
    public static void FinalizeInstanceColliders(GameObject root)
    {
        if (root == null)
            return;

        MeshCollider meshCollider = root.GetComponentInChildren<MeshCollider>(true);
        if (meshCollider != null && meshCollider.convex && !meshCollider.enabled)
        {
            EnsureBoxColliderFromRenderers(root);
            return;
        }

        Collider collider = root.GetComponentInChildren<Collider>(true);
        if (collider != null)
            collider.enabled = true;
    }

    /// <summary>
    /// Phase 3: restore convex mesh colliders for accurate picking after load or menu placement.
    /// </summary>
    public static int RestoreInstanceCollidersAfterLoad(GameObject root)
    {
        if (!RestoreMeshCollidersAfterLoad || root == null)
            return 0;

        int restored = 0;
        foreach (MeshCollider meshCollider in root.GetComponentsInChildren<MeshCollider>(true))
        {
            if (!meshCollider.convex || meshCollider.enabled)
                continue;

            meshCollider.enabled = true;
            restored++;
        }

        RemoveFallbackBoxColliders(root);

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider != null && !collider.enabled && collider is not MeshCollider)
                collider.enabled = true;
        }

        if (restored > 0 && !AssetPipelineDiagnostics.RoomLoadQuietMode)
        {
            AssetPipelineDiagnostics.Log(
                "PlacementLoad",
                $"Restored {restored} convex MeshCollider(s) on '{root.name}' after load");
        }

        return restored;
    }

    private static void EnsureBoxColliderFromRenderers(GameObject root)
    {
        if (root.TryGetComponent<BoxCollider>(out BoxCollider existing))
        {
            existing.enabled = true;
            FallbackBoxColliderIds.Add(existing.GetInstanceID());
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        BoxCollider box = root.AddComponent<BoxCollider>();
        FallbackBoxColliderIds.Add(box.GetInstanceID());
        box.center = root.transform.InverseTransformPoint(bounds.center);
        Vector3 lossyScale = root.transform.lossyScale;
        box.size = new Vector3(
            bounds.size.x / Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f),
            bounds.size.y / Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f),
            bounds.size.z / Mathf.Max(Mathf.Abs(lossyScale.z), 0.001f));
    }

    private static void RemoveFallbackBoxColliders(GameObject root)
    {
        foreach (BoxCollider box in root.GetComponents<BoxCollider>())
        {
            if (!FallbackBoxColliderIds.Remove(box.GetInstanceID()))
                continue;

            Object.Destroy(box);
        }
    }
}
