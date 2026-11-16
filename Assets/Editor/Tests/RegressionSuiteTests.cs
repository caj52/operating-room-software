using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Edit Mode checks for the contracts that keep coming back.
/// Run from Operating Room → Run regression tests.
/// </summary>
public class RegressionSuiteTests
{
    readonly List<Object> _owned = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            if (_owned[i] != null)
                Object.DestroyImmediate(_owned[i]);
        }
        _owned.Clear();
    }

    [Test]
    public void ExportFolders_StayUnderAppData_AndDoNotNest()
    {
        ExportPaths.SetParentFolder(@"C:\NotAppData\Exports");

        string parent = ExportPaths.GetParentFolder();
        Assert.AreEqual(Application.persistentDataPath, parent);
        Assert.IsFalse(ExportPaths.HasCustomParentFolder());

        string room = ExportPaths.SanitizeFolderName(ExportPaths.GetRoomExportName());
        string roomRoot = Path.Combine(parent, room);

        Assert.AreEqual(Path.Combine(roomRoot, "OBJ", "Room"), ExportPaths.ObjSceneDir);
        Assert.AreEqual(Path.Combine(roomRoot, "elevations"), ExportPaths.ElevationsDir);
        Assert.AreEqual(Path.Combine(roomRoot, "proposals"), ExportPaths.ProposalsDir);
        Assert.AreEqual(Path.Combine(roomRoot, "snapshots"), ExportPaths.SnapshotsDir);
        Assert.AreEqual(
            Path.Combine(Application.persistentDataPath, "Saved"),
            ConfigurationManager.GetSavedRoomsFolder());
        Assert.AreEqual(
            Path.Combine(Application.persistentDataPath, "Saved", "Configs"),
            ConfigurationManager.GetSavedConfigsFolder());

        string nested = Path.Combine(parent, room, room, room);
        Assert.AreEqual(parent, ExportPaths.NormalizeParentFolder(nested));
    }

    [Test]
    public void ObjectExport_GetsItsOwnFolder_NotTheRoomFolder()
    {
        string objectDir = ExportPaths.GetObjectPackageDir("Sample Name");
        string roomDir = ExportPaths.ObjSceneDir;

        StringAssert.EndsWith(Path.Combine("OBJ", "Sample_Name"), objectDir);
        StringAssert.EndsWith(Path.Combine("OBJ", "Room"), roomDir);
        Assert.AreNotEqual(roomDir, objectDir);
        Assert.AreEqual(
            Path.Combine(ExportPaths.GetExportBasePath(), "OBJ", "Object"),
            ExportPaths.GetObjectPackageDir("  "));
    }

    [Test]
    public void ObjectFileName_UsesAReadableName_NotAGuid()
    {
        Assert.AreEqual("OR_1", ExportPaths.SanitizeFolderName("OR 1"));
        Assert.IsTrue(ExportPaths.IsUsefulExportName("Patient Cart"));
        Assert.IsFalse(ExportPaths.IsUsefulExportName(new string('a', 32)));
        Assert.IsFalse(ExportPaths.IsUsefulExportName("Selectable"));
    }

    [Test]
    public void ExportAll_WritesOneObjPerBoom_ASingleObjectDoesNot()
    {
        ExportRequest room = ExportRequest.CreateDefaultsForRoom();
        ExportRequest one = ExportRequest.CreateDefaultsForSelection();

        Assert.IsTrue(room.IncludeObj);
        Assert.IsTrue(room.IncludeEachConfigurationObj);
        Assert.IsTrue(one.IncludeObj);
        Assert.IsFalse(one.IncludeEachConfigurationObj);
        Assert.AreEqual(ExportScope.Room, room.Scope);
        Assert.AreEqual(ExportScope.SelectedObject, one.Scope);
    }

    [Test]
    public void QuotePrice_UsesCostMarginAndTariff()
    {
        Assert.That(ProposalPricingResolver.SellPrice(100, 0.25, 0.10), Is.EqualTo(146.666666).Within(0.001));
        Assert.AreEqual(0, ProposalPricingResolver.SellPrice(0, 0.25, 0.10));
        Assert.AreEqual(0, ProposalPricingResolver.SellPrice(100, 0, 0.10));
        Assert.AreEqual(0, ProposalPricingResolver.SellPrice(100, 1, 0.10));
        Assert.That(ProposalPricingResolver.SellPrice(100, 0.25, -1), Is.EqualTo(133.333333).Within(0.001));
    }

    [Test]
    public void QuoteSheet_IsTheBundledV6File()
    {
        StringAssert.Contains("Quote_Request_V6", PricingBridgeStore.BundledV6FileName);
        StringAssert.Contains(
            Path.Combine("Data", "quotes"),
            PricingBridgeStore.BundledV6Path);
        Assert.IsTrue(File.Exists(PricingBridgeStore.BundledV6Path), PricingBridgeStore.BundledV6Path);
    }

    [Test]
    public void Colliders_ComeBackOnAfterLoad()
    {
        var root = new GameObject("ColliderRestore");
        Own(root);
        var mesh = new Mesh { name = "Hull" };
        Own(mesh);
        mesh.vertices = new[]
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0)
        };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 1, 1, 3, 2 };

        var meshCollider = root.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
        meshCollider.convex = true;
        meshCollider.enabled = false;

        var box = root.AddComponent<BoxCollider>();
        box.enabled = false;

        int restored = PlacementLoadOptimizer.RestoreInstanceCollidersAfterLoad(root);

        Assert.Greater(restored, 0);
        Assert.IsTrue(meshCollider.enabled);
        Assert.IsTrue(box.enabled);
    }

    [Test]
    public void Clearance_UsesAFullTurnAroundThatHole()
    {
        var host = new GameObject("Clearance");
        Own(host);
        var lines = host.AddComponent<ClearanceLinesRenderer>();

        var arm = new GameObject("Arm");
        Own(arm);
        var selectable = arm.AddComponent<Selectable>();
        arm.transform.position = new Vector3(3f, 1f, 0f);

        Vector3 hole = Vector3.zero;
        Vector3 tip = new Vector3(3f, 1f, -2f);

        float parked = Reach(lines, hole, tip, null);
        float fullTurn = Reach(lines, hole, tip, selectable);

        Assert.That(parked, Is.EqualTo(Mathf.Sqrt(13f)).Within(0.001f));
        Assert.That(fullTurn, Is.EqualTo(5f).Within(0.001f));
        Assert.Greater(fullTurn, parked);
    }

    [Test]
    public void SavedNames_MatchAcrossTheTypoAndTheCloneSuffix()
    {
        string fromSave = Measurable.DualSelectStem("ArmSegement.001");
        string fromLive = Measurable.DualSelectStem("ArmSegment(Clone)");

        Assert.AreEqual(fromSave, fromLive);
        Assert.AreEqual("ArmSegment", fromSave);
    }

    [Test]
    public void WallObjects_RotateInPlane_DoorsAndFloorsDoNot()
    {
        Selectable wall = MakeSelectable();
        wall.WallRestrictions = new List<RoomBoundaryType> { RoomBoundaryType.WallNorth };
        AllowMove(wall, Axis.X);
        UnlockRotation(wall);
        Assert.IsTrue(wall.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Z));
        Assert.IsTrue(wall.RotateAroundRendererCenter);

        Selectable door = MakeSelectable();
        door.SpecialTypes = new List<SpecialSelectableType> { SpecialSelectableType.Door };
        door.WallRestrictions = new List<RoomBoundaryType> { RoomBoundaryType.WallNorth };
        AllowMove(door, Axis.X);
        UnlockRotation(door);
        Assert.IsFalse(door.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Z));

        Selectable floor = MakeSelectable();
        floor.WallRestrictions = new List<RoomBoundaryType> { RoomBoundaryType.Floor };
        AllowMove(floor, Axis.X);
        UnlockRotation(floor);
        Assert.IsFalse(floor.IsGizmoSettingAllowed(GizmoType.Rotate, Axis.Z));

        Selectable logo = MakeSelectable();
        logo.WallRestrictions = new List<RoomBoundaryType> { RoomBoundaryType.WallSouth };
        UnlockRotation(logo);
        Assert.IsFalse(logo.RotateAroundRendererCenter);
    }

    [Test]
    public void LengthScale_DoesNotTreatCabinetsRailsOrOutletsAsTubes()
    {
        Selectable tube = MakeSelectable();
        Assert.AreEqual(LengthScaleKind.LengthTube, tube.GetLengthScaleKind());
        Assert.IsFalse(tube.SkipTubeLengthScaleIsolation());

        Selectable cabinet = MakeSelectable();
        cabinet.ScaleLevels = new List<Selectable.ScaleLevel>
        {
            new Selectable.ScaleLevel
            {
                Size = 1f,
                metadata = new[] { new Selectable.Metadata("rows", "2") }
            }
        };
        Assert.AreEqual(LengthScaleKind.RowConfigAssembly, cabinet.GetLengthScaleKind());
        Assert.IsTrue(cabinet.SkipTubeLengthScaleIsolation());

        Selectable shelves = MakeSelectable();
        shelves.SpecialTypes = new List<SpecialSelectableType> { SpecialSelectableType.ServiceHeadShelves };
        Assert.AreEqual(LengthScaleKind.SkuAxisStretch, shelves.GetLengthScaleKind());

        Selectable rail = MakeSelectable();
        rail.MetaData = new SelectableMetaData();
        rail.MetaData.Categories = new List<string> { "Service Head Rails" };
        Assert.AreEqual(LengthScaleKind.AuthoredIdentity, rail.GetLengthScaleKind());

        Selectable outlet = MakeSelectable();
        outlet.MetaData = new SelectableMetaData();
        outlet.MetaData.Categories = new List<string> { "Boom Head Low Voltage" };
        Assert.AreEqual(LengthScaleKind.AuthoredIdentity, outlet.GetLengthScaleKind());
    }

    [Test]
    public void LightToggle_SetsEmissionOnEveryMaterialSlot()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Assert.IsNotNull(shader);

        var host = new GameObject("Light");
        Own(host);
        var cup = new GameObject("Cups");
        Own(cup);
        cup.transform.SetParent(host.transform, false);
        var renderer = cup.AddComponent<MeshRenderer>();
        Material[] slots = { new Material(shader), new Material(shader) };
        Own(slots[0]);
        Own(slots[1]);
        renderer.sharedMaterials = slots;

        var factory = host.AddComponent<LightFactory>();
        var point = new GameObject("LightAttachPoint");
        Own(point);
        point.transform.SetParent(host.transform, false);
        SetField(factory, "emissiveObject", cup);
        SetField(factory, "lightAttachPoint", point.transform);
        SetField(factory, "emissionColor", Color.white);
        Invoke(factory, "Start");

        factory.SetLight(true);
        var live = (Material[])GetField(factory, "emissiveMaterials");
        Assert.AreEqual(2, live.Length);
        Assert.IsTrue(live[0].IsKeywordEnabled("_EMISSION"));
        Assert.IsTrue(live[1].IsKeywordEnabled("_EMISSION"));

        factory.SetLight(false);
        Assert.IsFalse(live[0].IsKeywordEnabled("_EMISSION"));
        Assert.IsFalse(live[1].IsKeywordEnabled("_EMISSION"));
    }

    [Test]
    public void MeasurementMaterials_AreNotTextShaders()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials" });
        Assert.Greater(guids.Length, 0);
        int checkedCount = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileName(path).StartsWith("Measurer", System.StringComparison.OrdinalIgnoreCase))
                continue;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(material.shader, path);
            string shader = material.shader.name;
            Assert.IsFalse(shader.Contains("TextMeshPro"), path);
            Assert.IsFalse(shader.Contains("SDF"), path);
            checkedCount++;
        }
        Assert.Greater(checkedCount, 0);
    }

    [Test]
    public void Catalog_EveryRowHasABundle_AndCategoriesDoNotCollide()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<SelectableAssetBundles>(
            "Assets/Resources/SelectableAssetBundles.asset");
        Assert.IsNotNull(catalog);
        var rows = new SerializedObject(catalog).FindProperty("_selectableData");
        Assert.IsNotNull(rows);
        Assert.Greater(rows.arraySize, 0);

        var collapsed = new Dictionary<string, string>();
        for (int i = 0; i < rows.arraySize; i++)
        {
            var row = rows.GetArrayElementAtIndex(i);
            string prefab = row.FindPropertyRelative("<PrefabName>k__BackingField").stringValue;
            string bundle = row.FindPropertyRelative("<AssetBundleName>k__BackingField").stringValue;
            Assert.IsFalse(string.IsNullOrWhiteSpace(prefab));
            Assert.IsFalse(string.IsNullOrWhiteSpace(bundle), prefab);

            var categories = row
                .FindPropertyRelative("<MetaData>k__BackingField")
                .FindPropertyRelative("<Categories>k__BackingField");
            for (int c = 0; c < categories.arraySize; c++)
            {
                string raw = categories.GetArrayElementAtIndex(c).stringValue;
                Assert.IsFalse(string.IsNullOrWhiteSpace(raw), prefab);
                Assert.AreEqual(raw.Trim(), raw, prefab);
                string key = LettersAndDigits(raw);
                if (collapsed.TryGetValue(key, out string existing) && existing != raw)
                    Assert.Fail("Categories \"" + existing + "\" and \"" + raw + "\" are the same menu group.");
                collapsed[key] = raw;
            }
        }
    }

    static float Reach(ClearanceLinesRenderer lines, Vector3 hole, Vector3 point, Selectable turning)
    {
        MethodInfo method = typeof(ClearanceLinesRenderer).GetMethod(
            "ReachFromHole",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        return (float)method.Invoke(lines, new object[] { hole, point, turning });
    }

    Selectable MakeSelectable()
    {
        var go = new GameObject("Selectable");
        Own(go);
        return go.AddComponent<Selectable>();
    }

    static void AllowMove(Selectable selectable, Axis axis)
    {
        if (!selectable.GizmoSettings.ContainsKey(GizmoType.Move))
            selectable.GizmoSettings[GizmoType.Move] = new Dictionary<Axis, GizmoSetting>();
        selectable.GizmoSettings[GizmoType.Move][axis] = new GizmoSetting
        {
            Axis = axis,
            GizmoType = GizmoType.Move,
            Unrestricted = true
        };
    }

    static void UnlockRotation(Selectable selectable)
    {
        Invoke(selectable, "UnlockWallInPlaneRotationIfNeeded");
    }

    static void Invoke(object target, string method)
    {
        MethodInfo info = target.GetType().GetMethod(
            method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(info, method);
        info.Invoke(target, null);
    }

    static object GetField(object target, string field)
    {
        FieldInfo info = target.GetType().GetField(
            field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(info, field);
        return info.GetValue(target);
    }

    static void SetField(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(
            field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(info, field);
        info.SetValue(target, value);
    }

    static string LettersAndDigits(string value)
    {
        var chars = new List<char>();
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
                chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }

    void Own(Object obj)
    {
        if (obj != null)
            _owned.Add(obj);
    }
}
