using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// EditMode contract for 3D object export: OBJ + MTL (+ PNG maps) only.
/// </summary>
public class ObjMtlExportTests
{
    string _dir;
    GameObject _root;
    Texture2D _ownedTex;
    Material _ownedMat;
    readonly List<Object> _owned = new();

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ors_obj_mtl_tests", System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _root = new GameObject("ObjMtlExportTestRoot");
    }

    [TearDown]
    public void TearDown()
    {
        if (_root != null)
            Object.DestroyImmediate(_root);
        if (_ownedTex != null)
            Object.DestroyImmediate(_ownedTex);
        if (_ownedMat != null)
            Object.DestroyImmediate(_ownedMat);
        for (int i = 0; i < _owned.Count; i++)
        {
            if (_owned[i] != null)
                Object.DestroyImmediate(_owned[i]);
        }
        _owned.Clear();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Test]
    public void TryExport_WritesObjAndMtl_NotGlbOrFbx()
    {
        MeshFilter[] filters = CreateUnitCube("CubeExport");

        bool ok = ObjExporter.TryExportObjMtl(true, filters, "CubeExport", _dir);

        Assert.IsTrue(ok);
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "CubeExport.obj")));
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "CubeExport.mtl")));
        Assert.IsEmpty(Directory.GetFiles(_dir, "*.glb"));
        Assert.IsEmpty(Directory.GetFiles(_dir, "*.fbx"));
        Assert.IsEmpty(Directory.GetFiles(_dir, "*.gltf"));
    }

    [Test]
    public void ObjFile_ReferencesMatchingMtl_AndHasGeometry()
    {
        MeshFilter[] filters = CreateUnitCube("CubeExport");

        Assert.IsTrue(ObjExporter.TryExportObjMtl(true, filters, "CubeExport", _dir));

        string obj = File.ReadAllText(Path.Combine(_dir, "CubeExport.obj"));
        StringAssert.Contains("mtllib CubeExport.mtl", obj);
        StringAssert.Contains("usemtl ", obj);
        StringAssert.Contains("\nv ", obj);
        StringAssert.Contains("\nf ", obj);
    }

    [Test]
    public void MtlFile_HasNewmtl_AndMapKdWhenTextured()
    {
        MeshFilter[] filters = CreateTexturedCube("TexCube");

        Assert.IsTrue(ObjExporter.TryExportObjMtl(true, filters, "TexCube", _dir));

        string mtl = File.ReadAllText(Path.Combine(_dir, "TexCube.mtl"));
        StringAssert.Contains("newmtl ", mtl);
        StringAssert.Contains("map_Kd TestAlbedo.png", mtl);
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "TestAlbedo.png")));
        Assert.IsEmpty(Directory.GetFiles(_dir, "*.glb"));
        Assert.IsEmpty(Directory.GetFiles(_dir, "*.fbx"));
    }

    [Test]
    public void TryExport_EmptyFilters_ReturnsFalse_WritesNothing()
    {
        bool ok = ObjExporter.TryExportObjMtl(true, System.Array.Empty<MeshFilter>(), "Empty", _dir);

        Assert.IsFalse(ok);
        Assert.IsEmpty(Directory.GetFiles(_dir));
    }

    [Test]
    public void ObjectExportPipeline_CallsObjExporter_NotGlbExporter()
    {
        string srcPath = Path.Combine(Application.dataPath, "Scripts", "UI", "UI_ObjExportOptions.cs");
        string src = File.ReadAllText(srcPath);

        StringAssert.Contains("ObjExporter.DoExport", src);
        Assert.IsFalse(
            src.Contains("GlbExporter.DoExport"),
            "Object/room 3D export must not write GLB/FBX.");
    }

    MeshFilter[] CreateUnitCube(string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(_root.transform, false);
        return new[] { go.GetComponent<MeshFilter>() };
    }

    MeshFilter[] CreateTexturedCube(string name)
    {
        MeshFilter[] filters = CreateUnitCube(name);
        var renderer = filters[0].GetComponent<MeshRenderer>();

        _ownedTex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = "TestAlbedo"
        };
        _ownedTex.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
        _ownedTex.Apply();

        Shader shader = renderer.sharedMaterial != null
            ? renderer.sharedMaterial.shader
            : Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        _ownedMat = new Material(shader) { name = "TestExportMat" };
        if (_ownedMat.HasProperty("_BaseMap"))
            _ownedMat.SetTexture("_BaseMap", _ownedTex);
        if (_ownedMat.HasProperty("_MainTex"))
            _ownedMat.SetTexture("_MainTex", _ownedTex);
        if (_ownedMat.HasProperty("_BaseColor"))
            _ownedMat.SetColor("_BaseColor", Color.white);
        if (_ownedMat.HasProperty("_Color"))
            _ownedMat.SetColor("_Color", Color.white);
        renderer.sharedMaterial = _ownedMat;
        return filters;
    }

    [TestCase(1f, 0f, 0f, "Body")]
    [TestCase(0f, 0f, 0f, "Black")]
    [TestCase(1f, 1f, 1f, "Ash")]
    public void UntexturedMaterial_WritesSolidColorPng_AndKeepsTheMaterialName(float r, float g, float b, string materialName)
    {
        MeshFilter[] filters = CreateUpFacingTriangle("Tri");
        var mat = CreateExportMaterial(materialName, new Color(r, g, b, 1f), null);
        filters[0].GetComponent<MeshRenderer>().sharedMaterial = mat;
        Assert.IsNull(mat.mainTexture);

        Assert.IsTrue(ObjExporter.TryExportObjMtl(true, filters, "Flat", _dir));

        string mtl = File.ReadAllText(Path.Combine(_dir, "Flat.mtl"));
        string obj = File.ReadAllText(Path.Combine(_dir, "Flat.obj"));
        string mapName = materialName + "_Color.png";
        StringAssert.Contains("newmtl " + materialName + "\n", mtl);
        StringAssert.Contains("usemtl " + materialName + "\n", obj);
        StringAssert.Contains("map_Kd " + mapName, mtl);
        StringAssert.Contains(string.Format(CultureInfo.InvariantCulture, "Kd {0:F6} {1:F6} {2:F6}", r, g, b), mtl);
        Assert.IsFalse(mtl.Contains("NoTexture"));
        Assert.IsFalse(obj.Contains("NoTexture"));

        string pngPath = Path.Combine(_dir, mapName);
        Assert.IsTrue(File.Exists(pngPath));
        var readback = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            Assert.IsTrue(readback.LoadImage(File.ReadAllBytes(pngPath)));
            Color pixel = readback.GetPixel(0, 0);
            Assert.That(pixel.r, Is.EqualTo(r).Within(0.02f));
            Assert.That(pixel.g, Is.EqualTo(g).Within(0.02f));
            Assert.That(pixel.b, Is.EqualTo(b).Within(0.02f));
        }
        finally
        {
            Object.DestroyImmediate(readback);
        }
    }

    [Test]
    public void TexturedMaterial_ExportsThatTexture_NotAColorStandIn()
    {
        MeshFilter[] filters = CreateUpFacingTriangle("Tri");
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "LogoMark" };
        Own(tex);
        tex.SetPixels32(new[]
        {
            new Color32(10, 20, 30, 255),
            new Color32(40, 50, 60, 255),
            new Color32(70, 80, 90, 255),
            new Color32(100, 110, 120, 255)
        });
        tex.Apply(false, false);

        var mat = CreateExportMaterial("Logo", Color.white, tex);
        filters[0].GetComponent<MeshRenderer>().sharedMaterial = mat;

        Assert.IsTrue(ObjExporter.TryExportObjMtl(true, filters, "LogoExport", _dir));

        string mtl = File.ReadAllText(Path.Combine(_dir, "LogoExport.mtl"));
        StringAssert.Contains("newmtl Logo\n", mtl);
        StringAssert.Contains("map_Kd LogoMark.png", mtl);
        Assert.IsFalse(mtl.Contains("_Color.png"));
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "LogoMark.png")));
        Assert.IsFalse(File.Exists(Path.Combine(_dir, "Logo_Color.png")));
    }

    [Test]
    public void UntexturedMaterial_WithOcclusion_BakesShadingIntoTheColorPng()
    {
        MeshFilter[] filters = CreateUpFacingTriangle("Tri");
        var ao = new Texture2D(2, 1, TextureFormat.RGBA32, false) { name = "Shade" };
        Own(ao);
        ao.SetPixels32(new[]
        {
            new Color32(0, 0, 0, 255),
            new Color32(0, 255, 0, 255)
        });
        ao.Apply(false, false);

        var mat = CreateExportMaterial("Light", Color.white, null);
        mat.SetTexture("_OcclusionMap", ao);
        mat.SetFloat("_OcclusionStrength", 1f);
        mat.EnableKeyword("_OCCLUSIONMAP");
        filters[0].GetComponent<MeshRenderer>().sharedMaterial = mat;

        Assert.IsTrue(ObjExporter.TryExportObjMtl(true, filters, "Shaded", _dir));

        string mtl = File.ReadAllText(Path.Combine(_dir, "Shaded.mtl"));
        StringAssert.Contains("map_Kd Light_Color.png", mtl);
        Assert.IsFalse(mtl.Contains("Shade.png"));

        var readback = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            Assert.IsTrue(readback.LoadImage(File.ReadAllBytes(Path.Combine(_dir, "Light_Color.png"))));
            Assert.That(readback.width, Is.EqualTo(2));
            Color dark = readback.GetPixel(0, 0);
            Color lit = readback.GetPixel(1, 0);
            Assert.That(dark.r, Is.LessThan(0.05f));
            Assert.That(lit.r, Is.GreaterThan(0.95f));
        }
        finally
        {
            Object.DestroyImmediate(readback);
        }
    }

    [Test]
    public void TexturedMaterial_WithOcclusion_ExportsTheColorPictureUnchanged()
    {
        MeshFilter[] filters = CreateUpFacingTriangle("Tri");
        var albedo = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "RealColor" };
        Own(albedo);
        albedo.SetPixel(0, 0, Color.red);
        albedo.Apply(false, false);

        var ao = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Shade" };
        Own(ao);
        ao.SetPixel(0, 0, Color.black);
        ao.Apply(false, false);

        var mat = CreateExportMaterial("Painted", Color.white, albedo);
        mat.SetTexture("_OcclusionMap", ao);
        mat.SetFloat("_OcclusionStrength", 1f);
        mat.EnableKeyword("_OCCLUSIONMAP");
        filters[0].GetComponent<MeshRenderer>().sharedMaterial = mat;

        Assert.IsTrue(ObjExporter.TryExportObjMtl(true, filters, "Painted", _dir));

        string mtl = File.ReadAllText(Path.Combine(_dir, "Painted.mtl"));
        StringAssert.Contains("map_Kd RealColor.png", mtl);
        Assert.IsFalse(mtl.Contains("Painted_Color.png"));
        Assert.IsFalse(File.Exists(Path.Combine(_dir, "Shade.png")));

        var readback = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            Assert.IsTrue(readback.LoadImage(File.ReadAllBytes(Path.Combine(_dir, "RealColor.png"))));
            Color pixel = readback.GetPixel(0, 0);
            Assert.That(pixel.r, Is.GreaterThan(0.95f));
            Assert.That(pixel.g, Is.LessThan(0.05f));
        }
        finally
        {
            Object.DestroyImmediate(readback);
        }
    }

    [Test]
    public void MaterialName_SpacesBecomeSingleToken()
    {
        MeshFilter[] filters = CreateUpFacingTriangle("Tri");
        filters[0].GetComponent<MeshRenderer>().sharedMaterial =
            CreateExportMaterial("White Plastic", Color.white, null);

        Assert.IsTrue(ObjExporter.TryExportObjMtl(true, filters, "Named", _dir));

        string mtl = File.ReadAllText(Path.Combine(_dir, "Named.mtl"));
        string obj = File.ReadAllText(Path.Combine(_dir, "Named.obj"));
        StringAssert.Contains("newmtl White_Plastic\n", mtl);
        StringAssert.Contains("usemtl White_Plastic\n", obj);
        StringAssert.Contains("map_Kd White_Plastic_Color.png", mtl);
    }

    [Test]
    public void SameMaterialName_SecondGetsNumericSuffix()
    {
        MeshFilter[] a = CreateUpFacingTriangle("A");
        MeshFilter[] b = CreateUpFacingTriangle("B");
        a[0].GetComponent<MeshRenderer>().sharedMaterial = CreateExportMaterial("Body", Color.red, null);
        b[0].GetComponent<MeshRenderer>().sharedMaterial = CreateExportMaterial("Body", Color.black, null);

        Assert.IsTrue(ObjExporter.TryExportObjMtl(true, new[] { a[0], b[0] }, "Dup", _dir));

        string mtl = File.ReadAllText(Path.Combine(_dir, "Dup.mtl"));
        StringAssert.Contains("newmtl Body\n", mtl);
        StringAssert.Contains("newmtl Body_2\n", mtl);
        StringAssert.Contains("map_Kd Body_Color.png", mtl);
        StringAssert.Contains("map_Kd Body_2_Color.png", mtl);
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "Body_Color.png")));
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "Body_2_Color.png")));
    }

    [Test]
    public void Export_PutsUnityUpOnPositiveZ_KeepsAuthoredNormals_AndFacesUp()
    {
        MeshFilter[] filters = CreateUpFacingTriangle("Tri");
        filters[0].GetComponent<MeshRenderer>().sharedMaterial =
            CreateExportMaterial("Body", Color.white, null);

        Assert.IsTrue(ObjExporter.TryExportObjMtl(true, filters, "Axis", _dir));

        string obj = File.ReadAllText(Path.Combine(_dir, "Axis.obj"));
        List<Vector3> verts = ParseVectors(obj, "v ");
        List<Vector3> normals = ParseVectors(obj, "vn ");
        List<int[]> faces = ParseFaces(obj);

        Assert.AreEqual(3, verts.Count);
        Assert.AreEqual(3, normals.Count);
        Assert.AreEqual(1, faces.Count);

        for (int i = 0; i < verts.Count; i++)
            Assert.That(verts[i].z, Is.EqualTo(5f).Within(0.0001f), "Unity's up axis must land on Z.");

        AssertContainsPoint(verts, new Vector3(0f, 0f, 5f));
        AssertContainsPoint(verts, new Vector3(0f, 1f, 5f));
        AssertContainsPoint(verts, new Vector3(1f, 0f, 5f));

        for (int i = 0; i < normals.Count; i++)
        {
            Assert.That(normals[i].x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(normals[i].y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(normals[i].z, Is.EqualTo(0f).Within(0.0001f));
        }

        int[] face = faces[0];
        Vector3 edge1 = verts[face[1]] - verts[face[0]];
        Vector3 edge2 = verts[face[2]] - verts[face[0]];
        Vector3 faceNormal = Vector3.Cross(edge1, edge2).normalized;
        Assert.That(faceNormal.z, Is.GreaterThan(0.9f));
        Assert.That(Mathf.Abs(faceNormal.x), Is.LessThan(0.1f));
        Assert.That(Mathf.Abs(faceNormal.y), Is.LessThan(0.1f));
    }

    void Own(Object obj)
    {
        if (obj != null)
            _owned.Add(obj);
    }

    MeshFilter[] CreateUpFacingTriangle(string name)
    {
        var mesh = new Mesh { name = name + "Mesh" };
        Own(mesh);
        mesh.vertices = new[]
        {
            new Vector3(0f, 5f, 0f),
            new Vector3(0f, 5f, 1f),
            new Vector3(1f, 5f, 0f)
        };
        mesh.normals = new[]
        {
            new Vector3(1f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(1f, 0f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.RecalculateBounds();

        var go = new GameObject(name);
        go.transform.SetParent(_root.transform, false);
        var filter = go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();
        filter.sharedMesh = mesh;
        return new[] { filter };
    }

    Material CreateExportMaterial(string name, Color color, Texture2D texture)
    {
        Shader shader = Shader.Find("Hidden/OrsExportTestLit");
        Assert.IsNotNull(shader, "Hidden/OrsExportTestLit is missing.");
        var mat = new Material(shader) { name = name };
        Own(mat);
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color", color);
        mat.SetTexture("_BaseMap", texture);
        mat.SetTexture("_MainTex", texture);
        return mat;
    }

    static List<Vector3> ParseVectors(string obj, string prefix)
    {
        var list = new List<Vector3>();
        foreach (string raw in obj.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith(prefix, System.StringComparison.Ordinal))
                continue;
            string[] parts = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            list.Add(new Vector3(
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture),
                float.Parse(parts[3], CultureInfo.InvariantCulture)));
        }
        return list;
    }

    static List<int[]> ParseFaces(string obj)
    {
        var faces = new List<int[]>();
        foreach (string raw in obj.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("f ", System.StringComparison.Ordinal))
                continue;
            string[] parts = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var indices = new int[parts.Length - 1];
            for (int i = 1; i < parts.Length; i++)
                indices[i - 1] = int.Parse(parts[i].Split('/')[0], CultureInfo.InvariantCulture) - 1;
            faces.Add(indices);
        }
        return faces;
    }

    static void AssertContainsPoint(List<Vector3> verts, Vector3 expected)
    {
        for (int i = 0; i < verts.Count; i++)
        {
            if (Mathf.Abs(verts[i].x - expected.x) < 0.0001f
                && Mathf.Abs(verts[i].y - expected.y) < 0.0001f
                && Mathf.Abs(verts[i].z - expected.z) < 0.0001f)
                return;
        }

        Assert.Fail("Missing vertex " + expected);
    }
}
