using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Avoids convex MeshCollider hull cooking during Instantiate on high-poly placeables.
/// Convex cooking on large meshes (e.g. DaVinci robot) can take tens of seconds.
/// </summary>
public static class PlacementLoadOptimizer
{
    private static readonly HashSet<int> PreparedPrefabIds = new();

    /// <summary>
    /// Call once per cached bundle prefab before Instantiate. Mutates the shared template.
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
    /// Re-enable picking/placement colliders without cooking convex hulls on high-poly meshes.
    /// </summary>
    public static void EnablePostPlacementCollider(GameObject root)
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

    private static void EnsureBoxColliderFromRenderers(GameObject root)
    {
        if (root.TryGetComponent<BoxCollider>(out BoxCollider existing))
        {
            existing.enabled = true;
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(bounds.center);
        Vector3 lossyScale = root.transform.lossyScale;
        box.size = new Vector3(
            bounds.size.x / Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f),
            bounds.size.y / Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f),
            bounds.size.z / Mathf.Max(Mathf.Abs(lossyScale.z), 0.001f));
    }
}
