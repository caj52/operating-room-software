using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Combines filtered meshes into a prepared hierarchy and writes:
/// <list type="bullet">
/// <item><description>.glb (Blender / Enscape custom assets)</description></item>
/// <item><description>.fbx + textures (Revit / Enscape)</description></item>
/// </list>
/// Does not merge meshes by material (that destroyed UVs). Bakes material texture
/// scale/offset into mesh UVs so DCC tools match in-game tiling — critical for
/// SetUVToWorld (world-meter UVs + small tiling scales).
/// </summary>
public static class GlbExporter
{
    private static readonly string[] ColorMapProps = { "_BaseMap", "_MainTex", "_EmissionMap" };
    private static readonly string[] LinearMapProps =
    {
        "_BumpMap",
        "_MetallicGlossMap",
        "_OcclusionMap",
        "_SpecGlossMap",
    };
    private static readonly string[] AllMapProps =
    {
        "_BaseMap",
        "_MainTex",
        "_BumpMap",
        "_MetallicGlossMap",
        "_OcclusionMap",
        "_EmissionMap",
        "_SpecGlossMap",
    };

    // Room GLBs don't need 2K/4K maps; unique RGBA32 copies of those were OOM'ing BakeImages.
    private const int MaxExportTextureSize = 1024;

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

        var ownedMeshes = new List<Mesh>();
        var ownedTextures = new List<Texture2D>();
        var ownedMaterials = new List<Material>();
        var textureCache = new Dictionary<(int id, int colorSpace), Texture2D>();
        GameObject exportRoot = null;

        try
        {
            string meshName = SanitizeFileName(string.IsNullOrWhiteSpace(name) ? "Export" : name);
            RefreshWorldSpaceUvs(meshFilters);

            exportRoot = new GameObject(meshName);
            exportRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            exportRoot.transform.localScale = Vector3.one;

            int exported = 0;
            for (int i = 0; i < meshFilters.Length; i++)
            {
                if (TryAddExportNode(
                    meshFilters[i],
                    exportRoot.transform,
                    ownedMeshes,
                    ownedTextures,
                    ownedMaterials,
                    textureCache))
                {
                    exported++;
                }

                if (i % 10 == 0)
                {
                    ObjExporter.OnMeshCombiningUpdate?.Invoke((float)(i + 1) / (meshFilters.Length + 1));
                    await Task.Yield();
                    if (!Application.isPlaying)
                        throw new Exception("App quit during task");
                }
            }

            if (exported == 0)
            {
                Debug.LogWarning("GLB export skipped — no valid meshes.");
                return;
            }

            Debug.Log($"GLB export: {exported}/{meshFilters.Length} meshes, {textureCache.Count} unique textures → {meshName}.glb");
            ObjExporter.OnMeshCombineSuccess?.Invoke();
            GC.Collect();
            await Resources.UnloadUnusedAssets();
            await Task.Yield();

            string dir = ExportPaths.ObjSceneDir;
            Directory.CreateDirectory(dir);
            string glbPath = Path.Combine(dir, $"{meshName}.glb");
            string fbxPath = Path.Combine(dir, $"{meshName}.fbx");

            bool glbOk = await GltfBinaryExport.ExportGameObjectToGlbAsync(exportRoot, glbPath, meshName);
            if (glbOk && File.Exists(glbPath))
            {
                if (!GlbFileSanitizer.SanitizeInPlace(glbPath) || !File.Exists(glbPath))
                {
                    Debug.LogError($"GLB export produced an unreadable file for '{meshName}'.");
                    glbOk = false;
                }
                else
                {
                    Debug.Log($"GLB export written: {glbPath}");
                }
            }
            else
            {
                Debug.LogError($"GLB export failed for '{meshName}'.");
                glbOk = false;
            }

            bool fbxOk = FbxRuntimeExport.ExportGameObjectToFbx(exportRoot, fbxPath);
            if (!fbxOk)
                Debug.LogError($"FBX export failed for '{meshName}'.");

            ObjExporter.OnSubMeshProcessed?.Invoke(1f);
            ObjExporter.OnMeshDataWritten?.Invoke();

            if (!glbOk && !fbxOk)
                return;

            // Prefer revealing the folder via GLB path; fall back to FBX.
            string revealPath = glbOk ? glbPath : fbxPath;
            Debug.Log($"3D export done — GLB:{(glbOk ? "ok" : "fail")} FBX:{(fbxOk ? "ok" : "fail")}");
            ObjExporter.ExportFinishedSuccessfully?.Invoke(revealPath);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            if (exportRoot != null)
                Object.Destroy(exportRoot);
            foreach (var m in ownedMeshes)
            {
                if (m != null)
                    Object.Destroy(m);
            }
            foreach (var tex in ownedTextures)
            {
                if (tex != null)
                    Object.Destroy(tex);
            }
            foreach (var mat in ownedMaterials)
            {
                if (mat != null)
                    Object.Destroy(mat);
            }

            ObjExporter.OnExportFinished?.Invoke();
            GC.Collect();
            Resources.UnloadUnusedAssets();
        }
    }

    private static void RefreshWorldSpaceUvs(MeshFilter[] meshFilters)
    {
        var refreshed = new HashSet<SetUVToWorld>();
        foreach (var filter in meshFilters)
        {
            if (filter == null)
                continue;

            foreach (var uv in filter.GetComponentsInParent<SetUVToWorld>(true))
            {
                if (uv == null || !refreshed.Add(uv))
                    continue;
                try
                {
                    uv.RefreshUVs();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"GLB export: RefreshUVs failed on {uv.name}: {e.Message}");
                }
            }
        }
    }

    private static bool TryAddExportNode(
        MeshFilter filter,
        Transform exportRoot,
        List<Mesh> ownedMeshes,
        List<Texture2D> ownedTextures,
        List<Material> ownedMaterials,
        Dictionary<(int id, int colorSpace), Texture2D> textureCache)
    {
        if (filter == null || filter.sharedMesh == null)
            return false;

        var renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null || !renderer.enabled)
            return false;

        Mesh src = filter.sharedMesh;
        if (src.vertexCount == 0 || src.subMeshCount == 0)
            return false;

        Material[] shared = renderer.sharedMaterials;
        MaterialPropertyBlock mpb = null;
        if (renderer.HasPropertyBlock())
        {
            mpb = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(mpb);
        }

        // One node per submesh so each material's tiling can be baked into UV0 safely.
        int added = 0;
        int subCount = src.subMeshCount;
        for (int s = 0; s < subCount; s++)
        {
            int[] tris = src.GetTriangles(s);
            if (tris == null || tris.Length == 0)
                continue;

            Mesh subMesh = ExtractSubMesh(src, s);
            if (subMesh == null || subMesh.vertexCount == 0)
                continue;

            ownedMeshes.Add(subMesh);

            Material srcMat = s < shared.Length ? shared[s] : null;
            Material mat;
            if (srcMat == null)
            {
                mat = CreateFallbackMaterial(ownedMaterials);
            }
            else
            {
                mat = new Material(srcMat) { name = SanitizeFileName(srcMat.name) };
                if (mpb != null)
                    ApplyPropertyBlock(mat, mpb);

                if (TryGetAlbedoScaleOffset(mat, mpb, out Vector2 scale, out Vector2 offset)
                    && (scale != Vector2.one || offset != Vector2.zero))
                {
                    BakeUvTransform(subMesh, scale, offset);
                }

                ResetAllTextureTransforms(mat);
                SanitizeMaterialForGlb(mat);
                BakeReadableTextures(mat, ownedTextures, textureCache);
                ownedMaterials.Add(mat);
            }

            string baseName = SanitizeFileName(filter.gameObject.name);
            var node = new GameObject(subCount > 1 ? $"{baseName}_{s}" : baseName);
            node.transform.SetParent(exportRoot, false);
            node.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
            node.transform.localScale = filter.transform.lossyScale;

            node.AddComponent<MeshFilter>().sharedMesh = subMesh;
            var mr = node.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            added++;
        }

        return added > 0;
    }

    /// <summary>
    /// Builds a standalone mesh for one submesh (welded to used vertices only).
    /// </summary>
    private static Mesh ExtractSubMesh(Mesh source, int subMeshIndex)
    {
        int[] srcTris = source.GetTriangles(subMeshIndex);
        if (srcTris.Length == 0)
            return null;

        var remap = new Dictionary<int, int>();
        var newTris = new int[srcTris.Length];
        for (int i = 0; i < srcTris.Length; i++)
        {
            int old = srcTris[i];
            if (!remap.TryGetValue(old, out int neu))
            {
                neu = remap.Count;
                remap[old] = neu;
            }
            newTris[i] = neu;
        }

        int vCount = remap.Count;
        var oldToNew = new int[vCount];
        foreach (var kvp in remap)
            oldToNew[kvp.Value] = kvp.Key;

        Vector3[] srcVerts = source.vertices;
        Vector3[] srcNorms = source.normals;
        Vector4[] srcTans = source.tangents;
        Vector2[] srcUv = source.uv;
        Color[] srcColors = source.colors;

        var verts = new Vector3[vCount];
        var norms = srcNorms != null && srcNorms.Length == source.vertexCount ? new Vector3[vCount] : null;
        var tans = srcTans != null && srcTans.Length == source.vertexCount ? new Vector4[vCount] : null;
        var uvs = srcUv != null && srcUv.Length == source.vertexCount ? new Vector2[vCount] : null;
        var colors = srcColors != null && srcColors.Length == source.vertexCount ? new Color[vCount] : null;

        for (int i = 0; i < vCount; i++)
        {
            int old = oldToNew[i];
            verts[i] = srcVerts[old];
            if (norms != null)
                norms[i] = srcNorms[old];
            if (tans != null)
                tans[i] = srcTans[old];
            if (uvs != null)
                uvs[i] = srcUv[old];
            if (colors != null)
                colors[i] = srcColors[old];
        }

        var mesh = new Mesh
        {
            indexFormat = vCount > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16,
            name = $"{source.name}_sub{subMeshIndex}"
        };
        mesh.SetVertices(verts);
        if (norms != null)
            mesh.SetNormals(norms);
        if (tans != null)
            mesh.SetTangents(tans);
        if (uvs != null)
            mesh.SetUVs(0, uvs);
        if (colors != null)
            mesh.SetColors(colors);
        mesh.SetTriangles(newTris, 0, true);
        mesh.RecalculateBounds();
        if (norms == null)
            mesh.RecalculateNormals();
        if (tans == null)
            mesh.RecalculateTangents();
        return mesh;
    }

    private static bool TryGetAlbedoScaleOffset(
        Material mat,
        MaterialPropertyBlock mpb,
        out Vector2 scale,
        out Vector2 offset)
    {
        scale = Vector2.one;
        offset = Vector2.zero;
        if (mat == null)
            return false;

        string prop = null;
        if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null)
            prop = "_BaseMap";
        else if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null)
            prop = "_MainTex";
        else if (mat.mainTexture != null)
            prop = mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";

        if (prop == null || !mat.HasProperty(prop))
            return false;

        scale = mat.GetTextureScale(prop);
        offset = mat.GetTextureOffset(prop);

        // Property blocks rarely override ST; if they expose vector ST, prefer it.
        if (mpb != null)
        {
            int id = Shader.PropertyToID(prop + "_ST");
            if (mpb.HasVector(id))
            {
                Vector4 st = mpb.GetVector(id);
                scale = new Vector2(st.x, st.y);
                offset = new Vector2(st.z, st.w);
            }
        }

        return true;
    }

    private static void BakeUvTransform(Mesh mesh, Vector2 scale, Vector2 offset)
    {
        Vector2[] uvs = mesh.uv;
        if (uvs == null || uvs.Length == 0)
            return;

        for (int i = 0; i < uvs.Length; i++)
        {
            uvs[i] = new Vector2(
                uvs[i].x * scale.x + offset.x,
                uvs[i].y * scale.y + offset.y);
        }

        mesh.uv = uvs;
    }

    private static void ResetAllTextureTransforms(Material material)
    {
        foreach (string prop in AllMapProps)
        {
            if (!material.HasProperty(prop))
                continue;
            material.SetTextureScale(prop, Vector2.one);
            material.SetTextureOffset(prop, Vector2.zero);
        }
    }

    private static void ApplyPropertyBlock(Material material, MaterialPropertyBlock mpb)
    {
        foreach (string prop in AllMapProps)
        {
            if (!material.HasProperty(prop))
                continue;
            int id = Shader.PropertyToID(prop);
            Texture tex = mpb.GetTexture(id);
            if (tex != null)
                material.SetTexture(prop, tex);
        }

        if (material.HasProperty("_BaseColor") && mpb.HasColor(Shader.PropertyToID("_BaseColor")))
            material.SetColor("_BaseColor", mpb.GetColor("_BaseColor"));
        if (material.HasProperty("_Color") && mpb.HasColor(Shader.PropertyToID("_Color")))
            material.SetColor("_Color", mpb.GetColor("_Color"));
    }

    /// <summary>
    /// Prevents glTFast from writing empty metallicRoughnessTexture objects.
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

        if (material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"))
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");

        // Transparent logos / decals: keep surface transparent keywords for glTFast alpha mode.
        if (material.HasProperty("_Cull") && Mathf.Approximately(material.GetFloat("_Cull"), 0f))
        {
            // Cull Off → glTF doubleSided (glTFast reads render face / cull where available)
        }
    }

    private static void BakeReadableTextures(
        Material material,
        List<Texture2D> bakedTextures,
        Dictionary<(int id, int colorSpace), Texture2D> textureCache)
    {
        if (material == null)
            return;

        foreach (string prop in ColorMapProps)
            ReplaceWithReadable(material, prop, RenderTextureReadWrite.sRGB, bakedTextures, textureCache);

        foreach (string prop in LinearMapProps)
        {
            if (prop == "_MetallicGlossMap"
                && !material.IsKeywordEnabled("_METALLICSPECGLOSSMAP")
                && !material.IsKeywordEnabled("_METALLICGLOSSMAP"))
                continue;

            ReplaceWithReadable(material, prop, RenderTextureReadWrite.Linear, bakedTextures, textureCache);
        }
    }

    private static void ReplaceWithReadable(
        Material material,
        string prop,
        RenderTextureReadWrite colorSpace,
        List<Texture2D> bakedTextures,
        Dictionary<(int id, int colorSpace), Texture2D> textureCache)
    {
        if (!material.HasProperty(prop))
            return;

        var src = material.GetTexture(prop) as Texture2D;
        if (src == null)
            return;

        var key = (src.GetInstanceID(), (int)colorSpace);
        if (!textureCache.TryGetValue(key, out Texture2D readable) || readable == null)
        {
            readable = MakeTextureReadable(src, colorSpace);
            readable.name = string.IsNullOrWhiteSpace(src.name) ? prop : src.name;
            textureCache[key] = readable;
            bakedTextures.Add(readable);
        }

        material.SetTexture(prop, readable);
    }

    private static Texture2D MakeTextureReadable(Texture2D texture, RenderTextureReadWrite colorSpace)
    {
        GetExportSize(texture.width, texture.height, out int width, out int height);

        RenderTexture tmp = RenderTexture.GetTemporary(
            width,
            height,
            0,
            RenderTextureFormat.ARGB32,
            colorSpace);

        Graphics.Blit(texture, tmp);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = tmp;

        Texture2D readableTex = new Texture2D(
            width,
            height,
            TextureFormat.RGBA32,
            false,
            colorSpace == RenderTextureReadWrite.Linear);
        readableTex.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
        readableTex.Apply(false, false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(tmp);
        return readableTex;
    }

    private static void GetExportSize(int srcWidth, int srcHeight, out int width, out int height)
    {
        width = Mathf.Max(1, srcWidth);
        height = Mathf.Max(1, srcHeight);
        int maxDim = Mathf.Max(width, height);
        if (maxDim <= MaxExportTextureSize)
            return;

        float scale = MaxExportTextureSize / (float)maxDim;
        width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
        height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
    }

    private static Material CreateFallbackMaterial(List<Material> ownedMaterials)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
        {
            name = "MissingMaterial",
            color = Color.magenta
        };
        ownedMaterials.Add(mat);
        return mat;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Export";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
