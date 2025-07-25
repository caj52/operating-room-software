// Optimized version of ObjExporter.cs with streaming and low-RAM support
// Author: ChatGPT (for Syed Anwar Faheem)
// Date: 2025-07-11

using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using UnityEngine.Events;
using System.Linq;

public static class ObjExporter
{
    public static UnityEvent<string> ExportFinishedSuccessfully { get; } = new();
    public static UnityEvent OnExportStarted { get; } = new();
    public static UnityEvent<float> OnSubMeshProcessed { get; } = new();
    public static UnityEvent<float> OnMeshCombiningUpdate { get; } = new();
    public static UnityEvent OnMeshCombineSuccess { get; } = new();
    public static UnityEvent OnExportFinished { get; } = new();
    public static int count = 1;

    public static async void DoExport(bool makeSubmeshes, MeshFilter[] meshFilters, string name)
    {
        ObjExporterScript.Start();
        OnExportStarted?.Invoke();

        try
        {
            string meshName = name;
            Dictionary<MeshRenderer, MeshFilter> rendererFilterMap = new();
            foreach (var filter in meshFilters)
            {
                var meshRenderer = filter.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                    rendererFilterMap[meshRenderer] = filter;
            }

            List<Material> materials = new();
            List<List<CombineInstance>> combineInstancesByMaterial = new();
            Dictionary<Material, Material> materialInstanceMap = new();

            int counter = 0;
            foreach (var kvp in rendererFilterMap)
            {
                ProcessMaterials(kvp.Key, kvp.Value, materials, combineInstancesByMaterial, materialInstanceMap);
                OnMeshCombiningUpdate?.Invoke((float)(counter + 1) / (meshFilters.Length + 1));
                if (counter % 10 == 0) await Task.Yield();
                counter++;
            }

            List<CombineInstance> finalCombiners = new();
            List<Mesh> tempSubmeshes = new();

            for (int i = 0; i < combineInstancesByMaterial.Count; i++)
            {
                Mesh submesh = new() { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                submesh.CombineMeshes(combineInstancesByMaterial[i].ToArray(), true);
                CombineInstance ci = new() { mesh = submesh, subMeshIndex = 0, transform = Matrix4x4.identity };
                finalCombiners.Add(ci);
                tempSubmeshes.Add(submesh);
            }

            Mesh finalMesh = new() { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            finalMesh.CombineMeshes(finalCombiners.ToArray(), false);
            OnMeshCombineSuccess?.Invoke();
            await Task.Yield();

            string path = meshName.Equals("Scene")
                ? Path.Combine(FullRoomSave.GetRoomPath(), "ObjFile")
                : Path.Combine(FullRoomSave.GetRoomPath(), "ObjFile", meshName);
            Directory.CreateDirectory(path);

            string objPath = Path.Combine(path, $"{meshName}.obj");
            string mtlPath = Path.Combine(path, $"{meshName}.mtl");

            using FileStream objStream = new(objPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using StreamWriter objWriter = new(objStream, Encoding.UTF8, bufferSize: 65536);
            using FileStream mtlStream = new(mtlPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using StreamWriter mtlWriter = new(mtlStream, Encoding.UTF8, bufferSize: 65536);

            await ExportMeshAsync(finalMesh, makeSubmeshes, materials, objWriter, mtlWriter);

            foreach (var material in materials)
                AddMaterialToMtl(material, mtlWriter);

            objWriter.Flush();
            mtlWriter.Flush();

            foreach (var tex in materials.SelectMany(m => new[] { m.mainTexture, m.GetTexture("_BumpMap") }).OfType<Texture2D>())
            {
                Texture2D readable = ObjExporterScript.MakeTextureReadable(tex);
                byte[] png = readable.EncodeToPNG();
                await File.WriteAllBytesAsync(Path.Combine(path, tex.name + ".png"), png);
            }

            ExportFinishedSuccessfully?.Invoke(path);
            foreach (var m in tempSubmeshes) UnityEngine.Object.Destroy(m);
            UnityEngine.Object.Destroy(finalMesh);

            GC.Collect();
            Resources.UnloadUnusedAssets();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            OnExportFinished?.Invoke();
            ObjExporterScript.End();
        }
    }

    // Extracted from ExportMeshAsync inside ObjExporter.cs
    // With simulated progress during vertex/normal/UV export

    private static async Task ExportMeshAsync(Mesh mesh, bool makeSubmeshes, List<Material> materials, StreamWriter objWriter, StreamWriter mtlWriter)
    {
        await objWriter.WriteLineAsync("# Exported OBJ");
        await objWriter.WriteLineAsync($"mtllib Path.GetFileName(mtlWriter.BaseStream is FileStream fs ? fs.Name : \"material.mtl\")\n");

        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var uvs = mesh.uv;
        int vertOffset = 1;

        for (int i = 0; i < vertices.Length; i++)
        {
            await objWriter.WriteLineAsync($"v {vertices[i].x} {vertices[i].y} {-vertices[i].z}");
            if (i % 100000 == 0)
            {
                Debug.Log($"Exporting vertices: {i}/{vertices.Length}");
                OnSubMeshProcessed?.Invoke(i / (float)vertices.Length * 0.1f); // show first 10% load
            }
        }

        for (int i = 0; i < normals.Length; i++)
        {
            await objWriter.WriteLineAsync($"vn {-normals[i].x} {-normals[i].y} {normals[i].z}");
            if (i % 100000 == 0)
            {
                Debug.Log($"Exporting normals: {i}/{normals.Length}");
                OnSubMeshProcessed?.Invoke(0.1f + i / (float)normals.Length * 0.1f); // 10-20%
            }
        }

        for (int i = 0; i < uvs.Length; i++)
        {
            await objWriter.WriteLineAsync($"vt {uvs[i].x} {uvs[i].y}");
            if (i % 100000 == 0)
            {
                Debug.Log($"Exporting UVs: {i}/{uvs.Length}");
                OnSubMeshProcessed?.Invoke(0.2f + i / (float)uvs.Length * 0.1f); // 20-30%
            }
        }

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            await objWriter.WriteLineAsync($"usemtl {materials[s].name}");
            int[] tris = mesh.GetTriangles(s);
            Debug.Log($"Exporting submesh {s + 1}/{mesh.subMeshCount}, triangles: {tris.Length / 3}");

            for (int i = 0; i < tris.Length; i += 3)
            {
                int i0 = tris[i] + vertOffset;
                int i1 = tris[i + 1] + vertOffset;
                int i2 = tris[i + 2] + vertOffset;
                await objWriter.WriteLineAsync($"f {i0}/{i0}/{i0} {i1}/{i1}/{i1} {i2}/{i2}/{i2}");
            }

            OnSubMeshProcessed?.Invoke(0.3f + (float)(s + 1) / mesh.subMeshCount * 0.7f); // 30-100%
            await Task.Yield();
        }
    }

    private static void AddMaterialToMtl(Material material, StreamWriter writer)
    {
        writer.WriteLine($"newmtl {material.name}");
        writer.WriteLine($"Ns {(material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") * 100f : 50f)}");
        Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        writer.WriteLine($"Ka {color.r:F6} {color.g:F6} {color.b:F6}");
        writer.WriteLine($"Kd {color.r:F6} {color.g:F6} {color.b:F6}");
        writer.WriteLine($"d {color.a:F6}");
        writer.WriteLine($"Tr {1f - color.a:F6}");
        writer.WriteLine("Ks 0.500000 0.500000 0.500000");
        writer.WriteLine("illum 2");

        if (material.mainTexture != null)
        {
            string textureName = material.mainTexture.name;
            writer.WriteLine($"map_Kd {textureName}.png");
        }

        if (material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") is Texture bumpMap)
        {
            string bumpMapName = bumpMap.name;
            float bumpScale = material.HasProperty("_BumpScale") ? material.GetFloat("_BumpScale") : 1.0f;
            writer.WriteLine($"map_bump -bm {bumpScale} {bumpMapName}.png");
        }

        writer.WriteLine();
    }


    private static void ProcessMaterials(MeshRenderer renderer, MeshFilter filter, List<Material> materials, List<List<CombineInstance>> combineInstancesByMaterial, Dictionary<Material, Material> materialInstanceMap)
    {
        Material[] sharedMaterials = renderer.sharedMaterials;
        for (int j = 0; j < sharedMaterials.Length; j++)
        {
            Material sharedMat = sharedMaterials[j];
            if (!materialInstanceMap.TryGetValue(sharedMat, out Material mat))
            {
                mat = new Material(sharedMat);
                materialInstanceMap[sharedMat] = mat;
                mat.name = ObjExportTexture.GenerateMaterialName(mat);
                materials.Add(mat);
                combineInstancesByMaterial.Add(new List<CombineInstance>());
            }

            int index = materials.IndexOf(mat);
            combineInstancesByMaterial[index].Add(new CombineInstance
            {
                mesh = filter.sharedMesh,
                transform = filter.transform.localToWorldMatrix,
                subMeshIndex = j
            });
        }
    }
}

public static class ObjExporterScript
{
    public static void Start() { }
    public static void End() { }

    public static Texture2D MakeTextureReadable(Texture2D texture)
    {
        RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0);
        Graphics.Blit(texture, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readable.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return readable;
    }
}

[Serializable]
public class ObjExportTexture
{
    public ObjExportTexture(string name, string base64)
    {
        Name = name;
        TextureBase64 = base64;
    }
    public string Name;
    public string TextureBase64;

    public static string GenerateMaterialName(Material material)
    {
        string type = material.name;
        string texture = material.mainTexture != null ? material.mainTexture.name : "NoTexture";
        string color = material.HasProperty("_Color") ? ColorToHex(material.GetColor("_Color")) : "FFFFFF";
        string tint = material.HasProperty("_TintColor") ? ColorToHex(material.GetColor("_TintColor")) : "NoTint";
        string fade = material.HasProperty("_Fade") ? material.GetFloat("_Fade").ToString("F2") : "NoFade";
        string heightType = material.HasProperty("_HeightType") ? material.GetFloat("_HeightType").ToString("F2") : "NoHeightType";
        string reflectionTexture = material.HasProperty("_ReflectionTex") ? material.GetTexture("_ReflectionTex").name : "NoReflection";
        string roughness = material.HasProperty("_Glossiness") ? (1.0f - material.GetFloat("_Glossiness")).ToString("F2") : "NoRoughness";
        string metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic").ToString("F2") : "NoMetallic";
        string specular = material.HasProperty("_SpecColor") ? ColorToHex(material.GetColor("_SpecColor")) : "NoSpecular";

        return $"{type}_{texture}_{color}_{tint}_{fade}_{heightType}_{reflectionTexture}_{roughness}_{metallic}_{specular}";
    }

    private static string ColorToHex(Color color)
    {
        return $"{(int)(color.r * 255):X2}{(int)(color.g * 255):X2}{(int)(color.b * 255):X2}";
    }

}
