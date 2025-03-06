using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The point of this component is to prevent memory leaks
/// while providing an editable mesh for <see cref="SetUVToWorld"/>
/// and <see cref="Cuttable"/>. It's important that all scripts that
/// depend on this use the mesh in a certain order and that only one
/// calls the original while the others call the modified current instance
/// </summary>
public class MeshInstanceManager : MonoBehaviour
{
    public Mesh OriginalMesh { get; private set; }

    /// <summary>
    /// Guaranteed to be a safe instance that can be modified.
    /// Should be shared mesh of all relevant mesh filters.
    /// Modifying this mesh will modify it for every component
    /// that references it
    /// </summary>
    public Mesh InstancedMesh { get; private set; }

    /// <summary>
    /// Will cache an original copy of the mesh so it can be retrieved later.
    /// Does nothing if the cache already exists. Should be called during Awake
    /// by the scripts that depend on this, and, as good 
    /// practice, the exact same sharedmesh should be registered by all
    /// </summary>
    /// <param name="mesh"></param>
    public void RegisterMesh(Mesh mesh)
    {
        if (OriginalMesh == null)
        {
            OriginalMesh = mesh;
            InstancedMesh = CloneMesh(mesh);
        }
    }

    /// <summary>
    /// Regenerates the <see cref="InstancedMesh"/> as a clone 
    /// of the <see cref="OriginalMesh"/>
    /// </summary>
    public void ResetMesh()
    {
        CleanUp();
        InstancedMesh = CloneMesh(OriginalMesh);
    }

    /// <summary>
    /// Call this to destroy and replace the current 
    /// <see cref="InstancedMesh"/>
    /// </summary>
    /// <param name="mesh"></param>
    public void UpdateMesh(Mesh mesh)
    {
        CleanUp();
        InstancedMesh = mesh;
    }

    public void CleanUp()
    {
        if (InstancedMesh != null) 
        {
            Destroy(InstancedMesh);
        }
    }

    private void OnDestroy()
    {
        CleanUp();
    }

    /// <summary>
    /// Creates a clone of a Mesh. Will cause memory leaks if not properly destroyed
    /// Source: <see href="https://forum.unity.com/threads/how-do-i-duplicate-a-mesh-asset.35639/#post-9236958"/>
    /// </summary>
    private static Mesh CloneMesh(Mesh source)
    {
        Mesh mesh = new Mesh();

        // Using unity combine mesh to combine a single mesh into another one, making a full copy.
        CombineInstance[] instancesToCombine = new CombineInstance[source.subMeshCount];
        for (int i = 0; i < source.subMeshCount; i++)
        {
            instancesToCombine[i] = new CombineInstance()
            {
                mesh = source,
                subMeshIndex = i,
                lightmapScaleOffset = new Vector4(1, 1, 0, 0),
                realtimeLightmapScaleOffset = new Vector4(1, 1, 0, 0),
                transform = Matrix4x4.identity
            };
        }
        mesh.CombineMeshes(instancesToCombine, false, false, false);

        mesh.name = source.name;
        return mesh;
    }
}
