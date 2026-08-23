using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Fbx;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Runtime FBX writer via Autodesk.Fbx (Unity Formats FBX exporter is Editor-only).
/// Expects the same prepared export hierarchy as <see cref="GlbExporter"/>
/// (UVs already baked, readable textures on materials).
/// </summary>
public static class FbxRuntimeExport
{
    private const float UnitScaleFactor = 100f; // meters → centimeters (FBX default)

    public static bool ExportGameObjectToFbx(GameObject root, string fbxPath)
    {
        if (root == null || string.IsNullOrWhiteSpace(fbxPath))
            return false;

        string dir = Path.GetDirectoryName(fbxPath);
        if (string.IsNullOrEmpty(dir))
            return false;

        Directory.CreateDirectory(dir);
        string texDir = Path.Combine(dir, Path.GetFileNameWithoutExtension(fbxPath) + "_textures");
        Directory.CreateDirectory(texDir);

        var writtenTextures = new Dictionary<int, string>();

        try
        {
            using (var fbxManager = FbxManager.Create())
            {
                var ioSettings = FbxIOSettings.Create(fbxManager, Globals.IOSROOT);
                ioSettings.SetBoolProp(Globals.EXP_FBX_EMBEDDED, true);
                fbxManager.SetIOSettings(ioSettings);

                var exporter = FbxExporter.Create(fbxManager, "Exporter");
                // Binary required for embedded textures.
                int format = -1;
                if (!exporter.Initialize(fbxPath, format, fbxManager.GetIOSettings()))
                {
                    Debug.LogError($"FBX export: failed to initialize writer for '{fbxPath}'.");
                    return false;
                }

                var scene = FbxScene.Create(fbxManager, root.name);
                var info = FbxDocumentInfo.Create(fbxManager, "SceneInfo");
                info.mTitle = root.name;
                info.mAuthor = "Operating Room Software";
                info.Original_ApplicationName.Set("Operating Room Software");
                info.LastSaved_ApplicationName.Set("Operating Room Software");
                scene.SetSceneInfo(info);

                var settings = scene.GetGlobalSettings();
                settings.SetSystemUnit(FbxSystemUnit.cm);
                settings.SetAxisSystem(FbxAxisSystem.DirectX);

                FbxNode fbxRoot = scene.GetRootNode();
                int meshCount = 0;

                foreach (Transform child in root.transform)
                {
                    if (ExportNode(child.gameObject, fbxRoot, scene, texDir, writtenTextures))
                        meshCount++;
                }

                if (meshCount == 0)
                {
                    Debug.LogWarning("FBX export: no meshes under export root.");
                    scene.Destroy();
                    exporter.Destroy();
                    return false;
                }

                // Match Unity FBX Exporter: convert to Maya Y-up right-handed last.
                FbxAxisSystem.MayaYUp.DeepConvertScene(scene);

                info.Url.Destroy();
                info.LastSavedUrl.Destroy();

                bool ok = exporter.Export(scene);
                scene.Destroy();
                exporter.Destroy();

                if (!ok || !File.Exists(fbxPath))
                {
                    Debug.LogError($"FBX export: write failed for '{fbxPath}'.");
                    return false;
                }

                Debug.Log($"FBX export written: {fbxPath} ({meshCount} meshes, textures in {texDir})");
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }

    private static bool ExportNode(
        GameObject go,
        FbxNode parent,
        FbxScene scene,
        string texDir,
        Dictionary<int, string> writtenTextures)
    {
        var filter = go.GetComponent<MeshFilter>();
        var renderer = go.GetComponent<MeshRenderer>();
        if (filter == null || filter.sharedMesh == null || renderer == null)
            return false;

        Mesh mesh = filter.sharedMesh;
        if (mesh.vertexCount == 0)
            return false;

        FbxNode node = FbxNode.Create(scene, Sanitize(go.name));
        parent.AddChild(node);

        Vector3 pos = go.transform.localPosition * UnitScaleFactor;
        Vector3 rot = go.transform.localRotation.eulerAngles;
        Vector3 scl = go.transform.localScale;
        node.LclTranslation.Set(new FbxDouble3(pos.x, pos.y, pos.z));
        node.LclRotation.Set(new FbxDouble3(rot.x, rot.y, rot.z));
        node.LclScaling.Set(new FbxDouble3(scl.x, scl.y, scl.z));

        FbxMesh fbxMesh = FbxMesh.Create(scene, Sanitize(mesh.name) + "_Mesh");
        Vector3[] verts = mesh.vertices;
        fbxMesh.InitControlPoints(verts.Length);
        for (int i = 0; i < verts.Length; i++)
            fbxMesh.SetControlPointAt(ToFbx(verts[i], UnitScaleFactor), i);

        int[] tris = mesh.triangles;
        var polyVerts = new List<int>(tris.Length);
        for (int i = 0; i < tris.Length; i += 3)
        {
            fbxMesh.BeginPolygon();
            fbxMesh.AddPolygon(tris[i]);
            fbxMesh.AddPolygon(tris[i + 1]);
            fbxMesh.AddPolygon(tris[i + 2]);
            fbxMesh.EndPolygon();
            polyVerts.Add(tris[i]);
            polyVerts.Add(tris[i + 1]);
            polyVerts.Add(tris[i + 2]);
        }

        ExportNormals(fbxMesh, mesh, polyVerts);
        ExportUVs(fbxMesh, mesh, polyVerts);

        Material mat = renderer.sharedMaterial;
        FbxSurfaceLambert fbxMat = CreateMaterial(scene, mat, texDir, writtenTextures);
        node.AddMaterial(fbxMat);

        FbxLayer layer = GetOrCreateLayer(fbxMesh, 0);
        using (var matElement = FbxLayerElementMaterial.Create(fbxMesh, "Materials"))
        {
            matElement.SetMappingMode(FbxLayerElement.EMappingMode.eAllSame);
            matElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eIndexToDirect);
            matElement.GetIndexArray().Add(0);
            layer.SetMaterials(matElement);
        }

        node.SetNodeAttribute(fbxMesh);
        node.SetShadingMode(FbxNode.EShadingMode.eTextureShading);
        return true;
    }

    private static void ExportNormals(FbxMesh fbxMesh, Mesh mesh, List<int> polyVerts)
    {
        Vector3[] normals = mesh.normals;
        if (normals == null || normals.Length != mesh.vertexCount)
            return;

        FbxLayer layer = GetOrCreateLayer(fbxMesh, 0);
        using (var element = FbxLayerElementNormal.Create(fbxMesh, "Normals"))
        {
            element.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygonVertex);
            element.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
            var arr = element.GetDirectArray();
            for (int i = 0; i < polyVerts.Count; i++)
                arr.Add(ToFbx(normals[polyVerts[i]]));
            layer.SetNormals(element);
        }
    }

    private static void ExportUVs(FbxMesh fbxMesh, Mesh mesh, List<int> polyVerts)
    {
        Vector2[] uvs = mesh.uv;
        if (uvs == null || uvs.Length != mesh.vertexCount)
            return;

        FbxLayer layer = GetOrCreateLayer(fbxMesh, 0);
        using (var element = FbxLayerElementUV.Create(fbxMesh, "UV0"))
        {
            element.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygonVertex);
            element.SetReferenceMode(FbxLayerElement.EReferenceMode.eIndexToDirect);

            var direct = element.GetDirectArray();
            for (int i = 0; i < uvs.Length; i++)
                direct.Add(new FbxVector2(uvs[i].x, uvs[i].y));

            var indices = element.GetIndexArray();
            indices.SetCount(polyVerts.Count);
            for (int i = 0; i < polyVerts.Count; i++)
                indices.SetAt(i, polyVerts[i]);

            layer.SetUVs(element, FbxLayerElement.EType.eTextureDiffuse);
        }
    }

    private static FbxSurfaceLambert CreateMaterial(
        FbxScene scene,
        Material unityMat,
        string texDir,
        Dictionary<int, string> writtenTextures)
    {
        string name = unityMat != null ? Sanitize(unityMat.name) : "Material";
        var fbxMat = FbxSurfaceLambert.Create(scene, name);

        Color color = Color.white;
        if (unityMat != null)
        {
            if (unityMat.HasProperty("_BaseColor"))
                color = unityMat.GetColor("_BaseColor");
            else if (unityMat.HasProperty("_Color"))
                color = unityMat.GetColor("_Color");
            else
                color = unityMat.color;
        }

        fbxMat.Diffuse.Set(new FbxDouble3(color.r, color.g, color.b));
        fbxMat.DiffuseFactor.Set(1.0);
        fbxMat.Ambient.Set(new FbxDouble3(color.r * 0.2f, color.g * 0.2f, color.b * 0.2f));
        fbxMat.Emissive.Set(new FbxDouble3(0, 0, 0));

        float alpha = Mathf.Clamp01(color.a);
        fbxMat.TransparencyFactor.Set(1.0 - alpha);
        fbxMat.TransparentColor.Set(new FbxDouble3(1, 1, 1));

        if (unityMat != null)
        {
            Texture2D tex = GetAlbedoTexture(unityMat);
            if (tex != null)
            {
                string path = EnsureTextureOnDisk(tex, texDir, writtenTextures);
                if (!string.IsNullOrEmpty(path))
                    AttachDiffuseTexture(fbxMat, path);
            }
        }

        return fbxMat;
    }

    private static Texture2D GetAlbedoTexture(Material mat)
    {
        if (mat.HasProperty("_BaseMap"))
        {
            var t = mat.GetTexture("_BaseMap") as Texture2D;
            if (t != null)
                return t;
        }

        if (mat.HasProperty("_MainTex"))
        {
            var t = mat.GetTexture("_MainTex") as Texture2D;
            if (t != null)
                return t;
        }

        return mat.mainTexture as Texture2D;
    }

    private static string EnsureTextureOnDisk(
        Texture2D tex,
        string texDir,
        Dictionary<int, string> writtenTextures)
    {
        int id = tex.GetInstanceID();
        if (writtenTextures.TryGetValue(id, out string existing))
            return existing;

        string fileName = Sanitize(string.IsNullOrWhiteSpace(tex.name) ? $"tex_{id}" : tex.name) + ".png";
        string path = Path.Combine(texDir, fileName);

        try
        {
            // Prefer already-readable export copies from GlbExporter.
            byte[] png;
            if (tex.isReadable)
            {
                png = tex.EncodeToPNG();
            }
            else
            {
                RenderTexture tmp = RenderTexture.GetTemporary(
                    tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, tmp);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = tmp;
                var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                copy.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(tmp);
                png = copy.EncodeToPNG();
                Object.Destroy(copy);
            }

            if (png == null || png.Length == 0)
                return null;

            File.WriteAllBytes(path, png);
            writtenTextures[id] = path;
            return path;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"FBX export: could not write texture '{tex.name}': {e.Message}");
            return null;
        }
    }

    private static void AttachDiffuseTexture(FbxSurfaceLambert fbxMat, string absolutePath)
    {
        var prop = fbxMat.FindProperty(FbxSurfaceMaterial.sDiffuse);
        if (prop == null || !prop.IsValid())
            return;

        var fbxTex = FbxFileTexture.Create(fbxMat, Path.GetFileNameWithoutExtension(absolutePath));
        fbxTex.SetFileName(absolutePath);
        fbxTex.SetTextureUse(FbxTexture.ETextureUse.eStandard);
        fbxTex.SetMappingType(FbxTexture.EMappingType.eUV);
        fbxTex.SetScale(1.0, 1.0);
        fbxTex.SetTranslation(0.0, 0.0);
        fbxTex.SetWrapMode(FbxTexture.EWrapMode.eRepeat, FbxTexture.EWrapMode.eRepeat);
        fbxTex.ConnectDstProperty(prop);
    }

    private static FbxLayer GetOrCreateLayer(FbxMesh mesh, int index)
    {
        while (mesh.GetLayerCount() <= index)
            mesh.CreateLayer();
        return mesh.GetLayer(index);
    }

    private static FbxVector4 ToFbx(Vector3 v, float scale = 1f)
    {
        return new FbxVector4(v.x * scale, v.y * scale, v.z * scale);
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Object";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
