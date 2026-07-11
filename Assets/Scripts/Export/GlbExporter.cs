using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Combines filtered meshes and writes a single self-contained .glb (textures embedded).
/// </summary>
public static class GlbExporter
{
    private static readonly string[] TexturePropertyNames =
    {
        "_BaseMap",
        "_MainTex",
        "_BumpMap",
        "_MetallicGlossMap",
        "_OcclusionMap",
        "_EmissionMap",
        "_SpecGlossMap",
    };

    public static async void DoExport(
        bool makeSubmeshes,
        MeshFilter[] meshFilters,
        string name,
        bool roomPackage = false)
    {
        _ = makeSubmeshes;
        _ = roomPackage;

        if (meshFilters == null || meshFilters.Length == 0)
        {
            Debug.LogWarning("GLB export skipped — no mesh filters provided.");
            ObjExporter.OnExportFinished?.Invoke();
            return;
        }

        ObjExporter.OnExportStarted?.Invoke();

        var tempMeshes = new List<Mesh>();
        var bakedTextures = new List<Texture2D>();
        var materialClones = new List<Material>();
        GameObject exportRoot = null;
        Mesh finalMesh = null;

        try
        {
            string meshName = SanitizeFileName(string.IsNullOrWhiteSpace(name) ? "Export" : name);
            Debug.Log($"GLB export: {meshFilters.Length} mesh filters → {meshName}.glb");

            var rendererFilterMap = new Dictionary<MeshRenderer, MeshFilter>();
            foreach (var filter in meshFilters)
            {
                if (filter == null)
                    continue;
                var meshRenderer = filter.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                    rendererFilterMap[meshRenderer] = filter;
            }

            var materials = new List<Material>();
            var combineInstancesByMaterial = new List<List<CombineInstance>>();
            var materialInstanceMap = new Dictionary<Material, Material>();

            int counter = 0;
            foreach (var kvp in rendererFilterMap)
            {
                ObjExporter.ProcessMaterials(
                    kvp.Key,
                    kvp.Value,
                    materials,
                    combineInstancesByMaterial,
                    materialInstanceMap);
                ObjExporter.OnMeshCombiningUpdate?.Invoke((float)(counter + 1) / (meshFilters.Length + 1));

                if (counter % 10 == 0)
                    await Task.Yield();

                counter++;
                if (!Application.isPlaying)
                    throw new Exception("App quit during task");
            }

            if (materials.Count == 0 || combineInstancesByMaterial.Count == 0)
            {
                Debug.LogWarning("GLB export skipped — no materials/meshes to combine.");
                return;
            }

            materialClones.AddRange(materials);

            var finalCombiners = new List<CombineInstance>();
            for (int i = 0; i < combineInstancesByMaterial.Count; i++)
            {
                Mesh submesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                submesh.CombineMeshes(combineInstancesByMaterial[i].ToArray(), true);
                tempMeshes.Add(submesh);

                CombineInstance ci = new CombineInstance
                {
                    mesh = submesh,
                    subMeshIndex = 0,
                    transform = Matrix4x4.identity
                };
                finalCombiners.Add(EnsureOutwardFacingNormals(ci, tempMeshes));
            }

            finalMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            finalMesh.CombineMeshes(finalCombiners.ToArray(), false);
            finalMesh.name = meshName;

            ObjExporter.OnMeshCombineSuccess?.Invoke();
            await Task.Yield();

            for (int i = 0; i < materials.Count; i++)
            {
                SanitizeMaterialForGlb(materials[i]);
                BakeReadableTextures(materials[i], bakedTextures);
            }

            exportRoot = new GameObject(meshName);
            exportRoot.AddComponent<MeshFilter>().sharedMesh = finalMesh;
            var renderer = exportRoot.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.ToArray();
            exportRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            exportRoot.transform.localScale = Vector3.one;

            // Flat layout: Documents/…/RoomName/RoomName.glb (no ObjFile nest).
            string dir = ExportPaths.ObjSceneDir;
            Directory.CreateDirectory(dir);
            string glbPath = Path.Combine(dir, $"{meshName}.glb");

            bool success = await GltfBinaryExport.ExportGameObjectToGlbAsync(exportRoot, glbPath, meshName);
            ObjExporter.OnSubMeshProcessed?.Invoke(1f);
            ObjExporter.OnMeshDataWritten?.Invoke();

            if (!success || !File.Exists(glbPath))
            {
                Debug.LogError($"GLB export failed for '{meshName}'.");
                return;
            }

            // glTFast can emit empty texture-info objects Blender rejects — repair in place.
            if (!GlbFileSanitizer.SanitizeInPlace(glbPath) || !File.Exists(glbPath))
            {
                Debug.LogError($"GLB export produced an unreadable file for '{meshName}'.");
                return;
            }

            Debug.Log($"GLB export written: {glbPath}");
            ObjExporter.ExportFinishedSuccessfully?.Invoke(glbPath);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            if (exportRoot != null)
                Object.Destroy(exportRoot);
            if (finalMesh != null)
                Object.Destroy(finalMesh);
            foreach (var m in tempMeshes)
            {
                if (m != null)
                    Object.Destroy(m);
            }
            foreach (var tex in bakedTextures)
            {
                if (tex != null)
                    Object.Destroy(tex);
            }
            foreach (var mat in materialClones)
            {
                if (mat != null)
                    Object.Destroy(mat);
            }

            ObjExporter.OnExportFinished?.Invoke();
            GC.Collect();
            Resources.UnloadUnusedAssets();
        }
    }

    /// <summary>
    /// Prevents a glTFast Lit export bug: when <c>_MetallicGlossMap</c> is assigned
    /// but <c>_METALLICSPECGLOSSMAP</c> is off, it writes <c>metallicRoughnessTexture:{}</c>
    /// with no index — invalid glTF that Blender cannot parse.
    /// </summary>
    private static void SanitizeMaterialForGlb(Material material)
    {
        if (material == null)
            return;

        bool metallicMapKeyword =
            material.IsKeywordEnabled("_METALLICSPECGLOSSMAP")
            || material.IsKeywordEnabled("_METALLICGLOSSMAP");

        if (material.HasProperty("_MetallicGlossMap") && !metallicMapKeyword)
            material.SetTexture("_MetallicGlossMap", null);

        // Avoid placeholder ORM TextureInfo from albedo-alpha smoothness when
        // the ORM bake later fails to assign an index.
        if (material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"))
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
    }

    private static void BakeReadableTextures(Material material, List<Texture2D> bakedTextures)
    {
        if (material == null)
            return;

        foreach (string prop in TexturePropertyNames)
        {
            if (!material.HasProperty(prop))
                continue;

            // Skip metallic map when the keyword is off (cleared above, but be explicit).
            if (prop == "_MetallicGlossMap"
                && !material.IsKeywordEnabled("_METALLICSPECGLOSSMAP")
                && !material.IsKeywordEnabled("_METALLICGLOSSMAP"))
                continue;

            var src = material.GetTexture(prop) as Texture2D;
            if (src == null)
                continue;

            Texture2D readable = MakeTextureReadable(src);
            readable.name = string.IsNullOrWhiteSpace(src.name) ? prop : src.name;
            material.SetTexture(prop, readable);
            bakedTextures.Add(readable);
        }
    }

    private static Texture2D MakeTextureReadable(Texture2D texture)
    {
        RenderTexture tmp = RenderTexture.GetTemporary(
            texture.width,
            texture.height,
            0,
            RenderTextureFormat.Default,
            RenderTextureReadWrite.Linear);

        Graphics.Blit(texture, tmp);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = tmp;

        Texture2D readableTex = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        readableTex.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
        readableTex.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(tmp);
        return readableTex;
    }

    private static CombineInstance EnsureOutwardFacingNormals(CombineInstance combineInstance, List<Mesh> tempMeshes)
    {
        Mesh mesh = Object.Instantiate(combineInstance.mesh);
        tempMeshes.Add(mesh);

        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int temp = triangles[i];
            triangles[i] = triangles[i + 1];
            triangles[i + 1] = temp;
        }

        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        combineInstance.mesh = mesh;
        return combineInstance;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Export" : name.Trim();
    }
}
