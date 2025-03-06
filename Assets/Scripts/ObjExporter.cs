using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;
using System.Threading.Tasks;
using UnityEngine.Events;
using System.Linq;
using RTG;
public static class ObjExporter
{
    public static UnityEvent<string> ExportFinishedSuccessfully { get; } = new();
    public static UnityEvent OnExportStarted { get; } = new();
    public static UnityEvent<float> OnSubMeshProcessed { get; } = new();
    public static UnityEvent<float> OnMeshCombiningUpdate { get; } = new();
    public static UnityEvent OnMeshCombineSuccess { get; } = new();
    public static UnityEvent OnMeshDataWritten { get; } = new();
    public static UnityEvent OnExportFinished { get; } = new();
    public static int count =1;
    public static async void DoExport(bool makeSubmeshes, MeshFilter[] meshFilters, string name)
    {
        ObjExporterScript.Start();
        OnExportStarted?.Invoke();

        try
        {
            string meshName = name;
            Debug.Log($"Found {meshFilters.Length} mesh filters");

            Dictionary<MeshRenderer, MeshFilter> rendererFilterMap = new();
            foreach (var filter in meshFilters)
            {
                var meshRenderer = filter.GetComponent<MeshRenderer>();

                if (meshRenderer != null)
                    rendererFilterMap[meshRenderer] = filter;
            }


            List<Material> materials = new();
            List<List<CombineInstance>> combineInstancesByMaterial = new();
            Dictionary<Material, Material> materialInstanceMap = new ();

            foreach (var kvp in rendererFilterMap)
            {
                var renderer = kvp.Key;
                var filter = kvp.Value;
   
    
                ProcessMaterials(renderer, filter, materials, combineInstancesByMaterial,materialInstanceMap);

                OnMeshCombiningUpdate?.Invoke((float)rendererFilterMap.Count / (meshFilters.Length + 1));
                await Task.Yield();
                if (!Application.isPlaying)
                    throw new Exception("App quit during task");
            }

            List<CombineInstance> finalCombiners = new();
            for (int i = 0; i < combineInstancesByMaterial.Count; i++)
            {
                Mesh submesh = new();
                submesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                submesh.CombineMeshes(combineInstancesByMaterial[i].ToArray(), true);

                CombineInstance ci = new()
                {
                    mesh = submesh,
                    subMeshIndex = 0,
                    transform = Matrix4x4.identity
                };
                ;
                finalCombiners.Add(
                EnsureOutwardFacingNormals(ci));
            }

            Mesh finalMesh = new();
            finalMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;





            finalMesh.CombineMeshes(finalCombiners.ToArray(), false);


            //finalMesh.RecalculateNormals();
            //finalMesh.RecalculateTangents();
            //finalMesh.RecalculateBounds();

            //finalMesh.Optimize();

            OnMeshCombineSuccess?.Invoke();
            await Task.Yield();

            ObjExportData data = new();
           
            data.Obj.Append($"#{meshName}.obj\n# {System.DateTime.Now.ToLongDateString()}\n# {System.DateTime.Now.ToLongTimeString()}\n#-------\n\n");
            string id = Guid.NewGuid().ToString();
            data.Obj.Append($"mtllib {id}.mtl\n\n");

            var obj = new GameObject("CombinedMesh", typeof(MeshFilter));
            obj.GetComponent<MeshFilter>().sharedMesh = finalMesh;




            Transform t = obj.transform;
            t.position = Vector3.zero;

            var task = ProcessTransform(t, makeSubmeshes, materials, data);
            await task;

            foreach (var material in materials)
            {
                AddMaterialToMtl(data, material);
            }

            data.Bake();
            string path = "";
            //string path = Path.Combine(Application.persistentDataPath , "obj", id);
            if (meshName.Equals("Scene")) { 
             path = Path.Combine(FullRoomSave.GetRoomPath(), "ObjFile");
            }
            else
            {
                path = Path.Combine(FullRoomSave.GetRoomPath(), "ObjFile",meshName);

            }
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, $"{meshName}.obj"), data.ObjString);
            File.WriteAllText(Path.Combine(path, $"{meshName}.mtl"), data.MtlString);

            count++;

            foreach (var item in data.Textures)
            {
                byte[] imageByteArray = Convert.FromBase64String(item.TextureBase64);
                File.WriteAllBytes(Path.Combine(path, $"{item.Name}.png"), imageByteArray);
            }

            ExportFinishedSuccessfully?.Invoke(path);

            Object.Destroy(finalMesh);
            Object.Destroy(obj);
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


  

    public static void ProcessMaterials(MeshRenderer renderer, MeshFilter filter, List<Material> materials, List<List<CombineInstance>> combineInstancesByMaterial, Dictionary<Material, Material> materialInstanceMap)
    {

     
        Material[] sharedMaterials = renderer.sharedMaterials;

        for (int j = 0; j < sharedMaterials.Length; j++)
        {
            Material sharedMat = sharedMaterials[j];

            // Check if a new instance already exists for this shared material
            if (!materialInstanceMap.TryGetValue(sharedMat, out Material mat))
            {
                mat = new Material(sharedMat);
                string newName = GenerateMaterialName(mat);
                mat.name = newName;

                materialInstanceMap[sharedMat] = mat;

                if (materials.Contains(mat))
                {
                    Debug.LogWarning($"Material already exists but adding again: {mat.name}");
                }
                else
                {
                    Debug.Log($"Adding new material: {mat.name}");
                    materials.Add(mat);
                    combineInstancesByMaterial.Add(new List<CombineInstance>());
                }
            }
            else
            {
                Debug.Log($"Material already exists: {mat.name}, ID: {mat.GetInstanceID()}");
            }

            // Get the index of the material in the materials list
            int materialIndex = materials.IndexOf(mat);

            // Create a CombineInstance and assign it to the appropriate list
            CombineInstance instance = new()
            {
                mesh = filter.sharedMesh,
                transform = filter.transform.localToWorldMatrix,
                subMeshIndex = j
            };
            combineInstancesByMaterial[materialIndex].Add(instance);
        }
    }
    private static CombineInstance EnsureOutwardFacingNormals(CombineInstance combineInstance)
    {

        // Create a copy of the mesh to avoid modifying shared instances
        Mesh mesh = Object.Instantiate(combineInstance.mesh);

        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        // Transform vertices using the combineInstance's matrix (for calculation only)
        Vector3[] transformedVertices = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            transformedVertices[i] = combineInstance.transform.MultiplyPoint3x4(vertices[i]);
        }

        // Calculate the mesh's center in world space
        Vector3 meshCenter = Vector3.zero;
        foreach (var vertex in transformedVertices)
            meshCenter += vertex;
        meshCenter /= transformedVertices.Length;

        // Ensure each triangle faces outward
        for (int i = 0; i < triangles.Length; i += 3)
        {
            // Swap the order of the triangle's vertices
            int temp = triangles[i];
            triangles[i] = triangles[i + 1];
            triangles[i + 1] = temp;
        }

        // Assign updated triangle array back to the mesh
        mesh.triangles = triangles;

        // Recalculate normals, tangents, and bounds
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        // Assign the updated mesh back to the combine instance
        combineInstance.mesh = mesh;


        return combineInstance;
    }


    private static void CorrectNormalsAndUVs(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector3[] normals = mesh.normals;
        Vector2[] uv = mesh.uv;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int idx0 = triangles[i];
            int idx1 = triangles[i + 1];
            int idx2 = triangles[i + 2];

            Vector3 v0 = vertices[idx0];
            Vector3 v1 = vertices[idx1];
            Vector3 v2 = vertices[idx2];

            Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

            Vector3 averageNormal = (normals[idx0] + normals[idx1] + normals[idx2]) / 3f;
            if (Vector3.Dot(faceNormal, averageNormal) < 0)
            {
                // Flip winding order
                triangles[i] = idx0;
                triangles[i + 1] = idx2;
                triangles[i + 2] = idx1;
            }
        }

        // Apply changes to mesh
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds(); // Ensure the bounding box is correct
    }



    private static async Task<ObjExportData> ProcessTransform(Transform t, bool makeSubmeshes, List<Material> materials, ObjExportData data)
    {
        data ??= new ObjExportData();

        if (t.TryGetComponent<MeshFilter>(out var mf))
        {
            data.Obj.Append("#" + t.name + "\n#-------" + "\n");
            if (makeSubmeshes)
                data.Obj.Append("g ").Append(t.name).Append("\n");

            var task = ObjExporterScript.MeshToString(mf, t, materials, data);
            await task;

            data.Obj.Append(task.Result);
        }

        return data;
    }

    private static void AddMaterialToMtl(ObjExportData data, Material material)
    {
        // Generate material name using the specified convention
        string materialName = material.name;
        // data.Mtl.Clear();
        data.Mtl.Append($"newmtl {materialName}\n");
        Debug.Log(data.Mtl.ToString());
        // Shininess
        if (material.HasProperty("_Glossiness"))
            data.Mtl.Append($"Ns {material.GetFloat("_Glossiness") * 100.0f}\n");
        else
            data.Mtl.Append("Ns 50.000000\n");

        // Ambient and diffuse colors
        Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        data.Mtl.Append($"Ka {color.r:F6} {color.g:F6} {color.b:F6}\n");
        data.Mtl.Append($"Kd {color.r:F6} {color.g:F6} {color.b:F6}\n");

        // Transparency
        float alpha = material.HasProperty("_Color") ? material.GetColor("_Color").a : 1.0f;
        data.Mtl.Append($"d {alpha:F6}\n");
        data.Mtl.Append($"Tr {1 - alpha:F6}\n");

        // Specular reflection
        data.Mtl.Append("Ks 0.500000 0.500000 0.500000\n");

        // Illumination model
        data.Mtl.Append("illum 2\n");

        // Add main texture
        if (material.mainTexture != null)
        {
            string textureName = material.mainTexture.name;
            data.Mtl.Append($"map_Kd {textureName}.png\n");
            data.AddTexture(material.mainTexture, textureName);
        }

        // Add normal map (if available)
        if (material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") is Texture bumpMap)
        {
            string bumpMapName = bumpMap.name;
            float bumpScale = material.HasProperty("_BumpScale") ? material.GetFloat("_BumpScale") : 1.0f;
            data.Mtl.Append($"map_bump -bm {bumpScale} {bumpMapName}.png\n");
            data.AddTexture(bumpMap, bumpMapName);
        }

        data.Mtl.Append("\n");
    }

    /// <summary>
    /// Generates a material name using the specified convention.
    /// </summary>
    private static string GenerateMaterialName(Material material)
    {
        // Extract type from the material's original name
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

    /// <summary>
    /// Converts a Color to a hexadecimal string.
    /// </summary>
    private static string ColorToHex(Color color)
    {
        return $"{(int)(color.r * 255):X2}{(int)(color.g * 255):X2}{(int)(color.b * 255):X2}";
    }

}

public class ObjExporterScript
{
    private static int _startIndex = 0;

    public static void Start()
    {
        _startIndex = 0;
    }

    public static void End()
    {
        _startIndex = 0;
    }

    private static void HandleTexture(
    string materialPropertyName,
    Material mat,
    string options,
    ObjExportData data,
    params string[] mapNames)
    {
        if (!mat.HasProperty(materialPropertyName))
            return;

        Texture2D tex = (Texture2D)mat.GetTexture(materialPropertyName);
        if (tex != null)
        {
            options ??= string.Empty;
            string texString = Convert.ToBase64String(tex.EncodeToPNG());
            data.Textures.Add(new ObjExportTexture(tex.name, texString));
            foreach (var item in mapNames)
            {
                Vector2 o = mat.GetTextureOffset(materialPropertyName);
                Vector2 s = mat.GetTextureScale(materialPropertyName);
                Add(data.Mtl, $"{item} -o {o.x} {o.y} -s {s.x} {s.y} {options} {tex.name}.png");
            }
        }
    }

    private static void Add(StringBuilder sb, string text)
    {
        sb.Append(text).Append("\n");
    }

    public static async Task<ObjExportData> MeshToString(
    MeshFilter mf,
    Transform t,
    List<Material> materials,
    ObjExportData data)
    {
        Quaternion rotation = t.localRotation;

        int numVertices = 0;
        Mesh m = mf.sharedMesh;
        if (!m)
        {
            string message = "Mesh not found. Fatal error";
            UI_DialogPrompt.Open(message);
            throw new Exception(message);
        }

        foreach (Vector3 vv in m.vertices)
        {
            Vector3 v = t.TransformPoint(vv);
            numVertices++;
            data.Obj.Append(string.Format("v {0} {1} {2}\n", v.x, v.y, -v.z));
        }

        data.Obj.Append("\n");

        foreach (Vector3 nn in m.normals)
        {
            Vector3 v = rotation * nn;
            data.Obj.Append(string.Format("vn {0} {1} {2}\n", -v.x, -v.y, v.z));
        }

        data.Obj.Append("\n");

        foreach (Vector3 v in m.uv)
        {
            data.Obj.Append(string.Format("vt {0} {1}\n", v.x, v.y));
        }

        for (int subMesh = 0; subMesh < m.subMeshCount; subMesh++)
        {
            data.Obj.Append("\n");

            int submeshIndex = subMesh;
            if (materials[submeshIndex] != null)
            {
                var mat = materials[submeshIndex];

                string name = mat.name;

                data.Obj.Append("usemtl ").Append(name).Append("\n");
                data.Mtl.Append($"newmtl {name}").Append("\n");

                var color = mat.color;
                data.Mtl.Append($"Ka {color.r} {color.g} {color.b}").Append("\n");
                data.Mtl.Append($"Kd {color.r} {color.g} {color.b}").Append("\n");
                data.Mtl.Append($"d {color.a}").Append("\n");
                data.Mtl.Append($"Tr {1 - color.a}").Append("\n");

                //Ke/map_Ke
                //if (mat.globalIlluminationFlags != 
                //MaterialGlobalIlluminationFlags.EmissiveIsBlack)
                //{
                //    if (mat.HasProperty("_EmissionColor"))
                //    {
                //        Color e = mat.GetColor("_EmissionColor");
                //        Add(data.Mtl, $"Ke {e.r} {e.g} {e.b}");
                //    }

                //    if (mat.HasProperty("_EmissionMap"))
                //    {
                //        HandleTexture("_EmissionMap", mat, null, data, "map_Ke");
                //    }
                //}

                if (mat.HasProperty("_Metallic"))
                {
                    Add(data.Mtl, $"Pm {mat.GetFloat("_Metallic")}");

                }
                HandleTexture("_MetallicGlossMap", mat, null, data, "map_Pm");
                HandleTexture("_BaseMap", mat, null, data, "map_Ka", "map_Kd");

                bool hasBumpScale = false;
                float scale = 0;
                if (mat.HasProperty("_BumpScale"))
                {
                    scale = mat.GetFloat("_BumpScale");
                    hasBumpScale = true;
                }
                string options = hasBumpScale ? $"-bm {scale}" : null;
                HandleTexture("_BumpMap", mat, options, data, "map_bump", "bump");
            }
            else
            {
                data.Obj.Append("usemtl ").Append("Empty").Append("\n");
                data.Mtl.Append($"newmtl Empty").Append("\n");
                data.Mtl.Append($"Ka 1 1 1").Append("\n");
                data.Mtl.Append($"Kd 1 1 1").Append("\n");
            }

            int[] triangles = m.GetTriangles(subMesh);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                data.Obj.Append(string.Format("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n",
                    triangles[i] + 1 + _startIndex, triangles[i + 1] + 1 + _startIndex, triangles[i + 2] + 1 + _startIndex));
            }

            ObjExporter.OnSubMeshProcessed?.Invoke((float)subMesh / (m.subMeshCount + 1));

            await Task.Yield();
            if (!Application.isPlaying)
                throw new Exception("App quit during task");
        }

        _startIndex += numVertices;
        return data;
    }
}

[Serializable]
public class ObjExportData
{
    public StringBuilder Obj { get; set; } = new();
    public StringBuilder Mtl { get; set; } = new();
    public string ObjString { get; set; }
    public string MtlString { get; set; }
    public string RoomName { get; set; }
    public List<ObjExportTexture> Textures { get; set; } = new();

    /// <summary>
    /// Serializes string builders so the class can be properly serialized to JSON
    /// </summary>
    public void Bake()
    {
        ObjString = Obj.ToString();
        MtlString = Mtl.ToString();
        Debug.Log("data bake " + MtlString);
    }

    /// <summary>
    /// Adds a texture to the export data, converting it to a base64-encoded PNG
    /// </summary>
    public void AddTexture(Texture texture, string textureName)
    {
        if (texture == null) return;

        // Cast texture to Texture2D
        Texture2D texture2D = texture as Texture2D;
        if (texture2D == null)
        {
            Debug.LogWarning($"Texture '{textureName}' is not a Texture2D and cannot be exported.");
            return;
        }

        // Encode Texture2D to PNG
        byte[] textureBytes = texture2D.EncodeToPNG();
        if (textureBytes == null)
        {
            Debug.LogWarning($"Failed to encode texture '{textureName}' to PNG.");
            return;
        }

        // Convert to base64
        string textureBase64 = Convert.ToBase64String(textureBytes);

        // Add to the texture list
        Textures.Add(new ObjExportTexture(textureName, textureBase64));
    }
}

[Serializable]
public class ObjExportTexture
{
    public ObjExportTexture(string name, string textureBase64)
    {
        Name = name;
        TextureBase64 = textureBase64;
    }

    public string Name { get; set; }
    public string TextureBase64 { get; set; }
}