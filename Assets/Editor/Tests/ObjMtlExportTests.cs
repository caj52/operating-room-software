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
}
