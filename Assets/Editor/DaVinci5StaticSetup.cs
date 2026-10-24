#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SplenSoft.AssetBundles;
using UnityEditor;
using UnityEngine;

/// <summary>
/// da Vinci 5 floor placeables.
/// Room convention: LookRotation(floorNormal) ⇒ local +Z is up.
/// Blender sources are Z-up with wheels/base near world Z=0; FBX is exported
/// axis_up=Y, so +90° X maps model up → local +Z. Orientation is verified by
/// checking that meshes named wheel/base/foot sit at the low-Z end of the AABB.
/// Menu category/name must match existing DB style: "DaVinci", "DaVinci 5 …".
/// </summary>
public class DaVinci5StaticSetup : AssetPostprocessor
{
    const string LogPath = @"C:\Users\conno\Downloads\davinci5-preview\unity_setup_log.txt";
    /// <summary>Exact category string used by existing DaVinci menu items.</summary>
    const string Category = "DaVinci";

    // Ground-truth from Blender: these mesh name fragments are at the floor.
    static readonly string[] FloorMeshHints =
    {
        "wheel", "wheels", "footrest", "foot", "base_001", "base_", "base.", "caster", "pedals"
    };

    // These belong at the top (arms / viewer / monitors).
    static readonly string[] TopMeshHints =
    {
        "boom", "monitor", "viewer", "crane", "display_", "arm1", "arm2", "arm3", "arm4"
    };

    static readonly (string modelPath, string prefabName, string displayName)[] Items =
    {
        ("Assets/Models/DaVinci5/PatientCart.fbx", "DaVinci5PatientCart", "DaVinci 5 Patient Cart"),
        ("Assets/Models/DaVinci5/SurgeonConsole.fbx", "DaVinci5SurgeonConsole", "DaVinci 5 Surgeon Console"),
        ("Assets/Models/DaVinci5/TowerMonitor.fbx", "DaVinci5TowerMonitor", "DaVinci 5 Vision Tower"),
    };

    static readonly string[] ModelPaths = Items.Select(i => i.modelPath).ToArray();
    static readonly string[] PrefabPaths =
        Items.Select(i => "Assets/Prefabs/Selectables/" + i.prefabName + ".prefab").ToArray();

    void OnPreprocessModel()
    {
        if (assetPath.Replace('\\', '/').IndexOf("Assets/Models/DaVinci5/") < 0)
            return;

        var importer = (ModelImporter)assetImporter;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importBlendShapes = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.External;
        importer.generateSecondaryUV = false;
        importer.indexFormat = ModelImporterIndexFormat.UInt32;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.isReadable = false;
        importer.addCollider = false;
    }

    public static void Reimport()
    {
        foreach (var path in ModelPaths)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                continue;
            }
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("DaVinci5 models reimported");
    }

    [MenuItem("Tools/DaVinci5/Fix Placeables (orient + names + pack)")]
    public static void Fix()
    {
        var lines = new List<string>();
        AssetDatabase.Refresh();
        EnsureTexturesPresent(lines);
        Reimport();
        VerifyImportedMaterials(lines);
        RecreatePrefabs(lines);
        ClearLocalMetaDataCache(lines);
        PackDaVinci5Bundles(lines);
        File.WriteAllLines(LogPath, lines);
        Debug.Log("DaVinci5 Fix done\n" + string.Join("\n", lines));
    }

    /// <summary>
    /// Restore the verified floor orientation (+90 X, wheels at z≈0) and rebake
    /// convex hulls. Does not touch names, categories, or GUID/bundle identity.
    /// </summary>
    [MenuItem("Tools/DaVinci5/Restore Orientation (keep colliders/names)")]
    public static void RestoreOrientationKeepColliders()
    {
        var lines = new List<string>();
        EnsureSourceMeshesReadable(lines);

        foreach (var item in Items)
        {
            string prefabPath = "Assets/Prefabs/Selectables/" + item.prefabName + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform model = FindModelChild(root.transform);
                if (model == null)
                {
                    lines.Add("FAIL no model child on " + item.prefabName);
                    continue;
                }

                // Nested FBX PrefabInstances drop transform overrides on big hierarchies
                // (PatientCart). Unpack so +90 X seating is real local transform data.
                if (PrefabUtility.IsAnyPrefabInstanceRoot(model.gameObject))
                {
                    PrefabUtility.UnpackPrefabInstance(
                        model.gameObject,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                    lines.Add(item.prefabName + " unpacked nested FBX");
                }

                // Known-good orientation from Blender wheel/base verification.
                ApplySeat(model, Quaternion.Euler(90f, 0f, 0f), out Bounds bounds);

                // Confirm wheels still sit low after restore.
                CollectHintBounds(model, out var floorCenters, out var topCenters);
                float floorAvg = floorCenters.Count > 0 ? floorCenters.Average(c => c.z) : -1f;
                float topAvg = topCenters.Count > 0 ? topCenters.Average(c => c.z) : -1f;
                bool ok = floorCenters.Count == 0
                    || (floorAvg < topAvg && Mathf.Abs(bounds.min.z) < 0.08f);
                lines.Add($"{item.prefabName} restore rot=(90,0,0) size={bounds.size} " +
                          $"floorAvgZ={floorAvg:F3} topAvgZ={topAvg:F3} verified={ok}");

                Mesh hull = BakeConvexHullAsset(root.transform, item.prefabName, lines);
                var mc = root.GetComponent<MeshCollider>();
                if (mc == null)
                    mc = root.AddComponent<MeshCollider>();
                mc.sharedMesh = hull;
                mc.convex = true;
                var box = root.GetComponent<BoxCollider>();
                if (box != null)
                    UnityEngine.Object.DestroyImmediate(box);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        PackDaVinci5Bundles(lines);
        File.WriteAllLines(LogPath, lines);
        Debug.Log("DaVinci5 orientation restored\n" + string.Join("\n", lines));
    }

    static Transform FindModelChild(Transform root)
    {
        foreach (Transform child in root)
        {
            if (child.name == "Measurables")
                continue;
            if (child.GetComponent<Measurable>() != null)
                continue;
            // Nested FBX root or unpacked model container.
            if (child.GetComponentInChildren<MeshFilter>(true) != null)
                return child;
        }
        return null;
    }

    public static void Create()
    {
        var lines = new List<string>();
        RecreatePrefabs(lines);
        File.WriteAllLines(LogPath, lines);
        Debug.Log("DaVinci5 static prefabs created\n" + string.Join("\n", lines));
    }

    static void EnsureTexturesPresent(List<string> lines)
    {
        var copies = new (string srcDir, string[] dests)[]
        {
            (@"C:\Users\conno\Downloads\davinci5-preview\Surgical Patient Cart Viz\textures",
                new[] { "Assets/Models/DaVinci5/PatientCart.fbm", "Assets/Models/DaVinci5/PatientCart.fbx.tmp.fbm" }),
            (@"C:\Users\conno\Downloads\davinci5-preview\Surgical Console Viz\textures",
                new[] { "Assets/Models/DaVinci5/SurgeonConsole.fbm", "Assets/Models/DaVinci5/SurgeonConsole.fbx.tmp.fbm" }),
            (@"C:\Users\conno\Downloads\davinci5-preview\Surgical Tower Monitor Viz\textures",
                new[] { "Assets/Models/DaVinci5/TowerMonitor.fbm", "Assets/Models/DaVinci5/TowerMonitor.fbx.tmp.fbm" }),
        };

        foreach (var (srcDir, dests) in copies)
        {
            if (!Directory.Exists(srcDir))
            {
                lines.Add("WARN missing texture source " + srcDir);
                continue;
            }
            foreach (var dest in dests)
            {
                Directory.CreateDirectory(dest);
                foreach (var file in Directory.GetFiles(srcDir))
                {
                    string target = Path.Combine(dest, Path.GetFileName(file));
                    File.Copy(file, target, true);
                }
                lines.Add("textures → " + dest + " (" + Directory.GetFiles(dest).Length + " files)");
            }
        }
    }

    static void VerifyImportedMaterials(List<string> lines)
    {
        string[] requiredDisplayMats =
        {
            "Assets/Models/DaVinci5/Materials/patient_cart_helm_display3.mat",
            "Assets/Models/DaVinci5/Materials/video_in_out2.mat",
            "Assets/Models/DaVinci5/Materials/tower_imaging_system.mat",
            "Assets/Models/DaVinci5/Materials/tower_video_input_output.mat",
            "Assets/Models/DaVinci5/Materials/laser_warning_label.mat",
            "Assets/Models/DaVinci5/Materials/circular_vent.mat",
        };

        foreach (var path in requiredDisplayMats)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                lines.Add("FAIL missing material " + path);
                continue;
            }
            var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture;
            lines.Add(tex != null
                ? "OK mat " + Path.GetFileName(path) + " base=" + tex.name
                : "FAIL mat " + Path.GetFileName(path) + " has no base texture");
        }

        foreach (var modelPath in ModelPaths)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                lines.Add("FAIL missing model " + modelPath);
                continue;
            }
            int renderers = 0, nullMats = 0, pinkish = 0;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                renderers++;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) { nullMats++; continue; }
                    if (m.shader == null || m.shader.name.Contains("Error") || m.shader.name.Contains("Hidden/InternalError"))
                        pinkish++;
                }
            }
            lines.Add($"{modelPath} renderers={renderers} nullMats={nullMats} errorShaders={pinkish}");
        }
    }

    static void RecreatePrefabs(List<string> lines)
    {
        foreach (var item in Items)
            CreateOne(item.modelPath, item.prefabName, item.displayName, lines);

        var catalog = AssetDatabase.LoadAssetAtPath<SelectableAssetBundles>(
            "Assets/Resources/SelectableAssetBundles.asset");
        if (catalog != null)
        {
            catalog.OnPreprocessAssetBundle();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            lines.Add("catalog refreshed");

            // Hard-verify catalog YAML rows match what Object Menu seeds to the DB.
            string catalogYaml = File.ReadAllText("Assets/Resources/SelectableAssetBundles.asset");
            foreach (var item in Items)
            {
                bool hasPrefab = catalogYaml.Contains("PrefabName>k__BackingField: " + item.prefabName);
                bool hasName = catalogYaml.Contains("Name>k__BackingField: " + item.displayName);
                bool hasCat = hasPrefab && catalogYaml.Contains("- " + Category);
                // tighter: find the block for this prefab
                int idx = catalogYaml.IndexOf("PrefabName>k__BackingField: " + item.prefabName, StringComparison.Ordinal);
                string block = idx >= 0 && idx + 800 < catalogYaml.Length
                    ? catalogYaml.Substring(idx, 800)
                    : (idx >= 0 ? catalogYaml.Substring(idx) : "");
                hasName = block.Contains("Name>k__BackingField: " + item.displayName);
                hasCat = block.Contains("- " + Category);
                lines.Add($"catalog {item.prefabName}: present={hasPrefab} name={(hasName ? "OK" : "FAIL")} cat={(hasCat ? "OK DaVinci" : "FAIL")}");
            }
        }
    }

    static void CreateOne(string modelPath, string prefabName, string displayName, List<string> lines)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            lines.Add("MISSING " + modelPath);
            return;
        }

        int layer = LayerMask.NameToLayer("Selectable");
        var root = new GameObject(prefabName);
        if (layer >= 0)
            root.layer = layer;

        var selectable = root.AddComponent<Selectable>();
        root.AddComponent<TrackedObject>();
        root.AddComponent<KeepRelativePosition>();

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.transform.SetParent(root.transform, false);
        instance.transform.localScale = Vector3.one;

        // Unpack so seating/rotation survive SaveAsPrefabAsset on large FBX hierarchies.
        PrefabUtility.UnpackPrefabInstance(
            instance,
            PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction);

        var orient = OrientUsingFloorMeshes(instance.transform, lines, prefabName);
        var bounds = orient.bounds;

        // Low-poly convex hull (same pattern as SurgicalRobot) — not a box, not the
        // full visual mesh (that stalls place-click when PhysX cooks it).
        Mesh hull = BakeConvexHullAsset(root.transform, prefabName, lines);
        if (hull == null)
        {
            lines.Add("FAIL " + prefabName + " no hull — aborting prefab write");
            UnityEngine.Object.DestroyImmediate(root);
            return;
        }
        var meshCollider = root.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = hull;
        meshCollider.convex = true;

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (layer >= 0)
                t.gameObject.layer = layer;
        }

        var measurableRoot = new GameObject("Measurables");
        measurableRoot.transform.SetParent(root.transform, false);
        if (layer >= 0)
            measurableRoot.layer = layer;
        float[] yaws = { 0f, 90f, 180f, 270f };
        for (int i = 0; i < yaws.Length; i++)
        {
            var mGo = new GameObject("Measurement (" + i + ")");
            mGo.transform.SetParent(measurableRoot.transform, false);
            mGo.transform.localRotation = Quaternion.Euler(0f, yaws[i], 0f);
            if (layer >= 0)
                mGo.layer = layer;
            var measurable = mGo.AddComponent<Measurable>();
            var mSo = new SerializedObject(measurable);
            var typesProp = mSo.FindProperty("<MeasurementTypes>k__BackingField");
            typesProp.ClearArray();
            typesProp.InsertArrayElementAtIndex(0);
            typesProp.GetArrayElementAtIndex(0).enumValueIndex = (int)MeasurementType.Walls;
            mSo.FindProperty("<ForwardOnly>k__BackingField").boolValue = true;
            mSo.ApplyModifiedPropertiesWithoutUndo();
        }

        selectable.MetaData = new SelectableMetaData
        {
            Name = displayName,
            Enabled = true,
            IsStandalone = true,
            IsSubSelectable = false,
            Categories = new List<string> { Category },
            KeyWords = new List<string>
            {
                "DaVinci", "da vinci", "davinci", "robot", "surgical", "intuitive", "da Vinci 5"
            }
        };
        selectable.WallRestrictions = new List<RoomBoundaryType> { RoomBoundaryType.Floor };
        selectable.GizmoSettingsList = new List<GizmoSetting>
        {
            new GizmoSetting { Axis = Axis.X, GizmoType = GizmoType.Move, Unrestricted = true },
            new GizmoSetting { Axis = Axis.Y, GizmoType = GizmoType.Move, Unrestricted = true },
            new GizmoSetting { Axis = Axis.Z, GizmoType = GizmoType.Rotate, Unrestricted = true }
        };
        selectable.canBeDuplicated = true;

        string prefabPath = "Assets/Prefabs/Selectables/" + prefabName + ".prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        if (!AssetBundleManager.TryGetAssetBundleName(prefab, out string bundleName))
        {
            lines.Add("NO BUNDLE " + prefabName);
            return;
        }

        var so = new SerializedObject(prefab.GetComponent<Selectable>());
        so.FindProperty("<GUID>k__BackingField").stringValue = bundleName;
        var measProp = so.FindProperty("<Measurables>k__BackingField");
        if (measProp != null)
        {
            var prefabMeasurables = prefab.GetComponentsInChildren<Measurable>(true);
            measProp.ClearArray();
            for (int i = 0; i < prefabMeasurables.Length; i++)
            {
                measProp.InsertArrayElementAtIndex(i);
                measProp.GetArrayElementAtIndex(i).objectReferenceValue = prefabMeasurables[i];
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(prefab);
        AssetDatabase.SaveAssets();

        lines.Add($"{prefabName} name=\"{displayName}\" cat={Category} " +
                  $"rot={orient.rotationEuler} size={bounds.size} " +
                  $"floorMeshes={orient.floorHits} topMeshes={orient.topHits} " +
                  $"orient={(orient.verified ? "VERIFIED" : "FAIL")} guid={bundleName}");
    }

    /// <summary>
    /// Choose a rotation where named floor meshes (wheels/base) sit at low Z and
    /// boom/monitor/viewer meshes sit at high Z — matching Blender ground truth.
    /// </summary>
    static (Bounds bounds, Vector3 rotationEuler, bool verified, int floorHits, int topHits)
        OrientUsingFloorMeshes(Transform instance, List<string> lines, string label)
    {
        // Prefer +90 X (Y-up FBX → +Z). Include common snaps as candidates only.
        Quaternion[] candidates =
        {
            Quaternion.Euler(90f, 0f, 0f),
            Quaternion.Euler(90f, 90f, 0f),
            Quaternion.Euler(90f, 180f, 0f),
            Quaternion.Euler(90f, 270f, 0f),
            Quaternion.Euler(-90f, 0f, 0f),
            Quaternion.Euler(-90f, 180f, 0f),
            Quaternion.Euler(0f, 0f, 0f),
            Quaternion.Euler(180f, 0f, 0f),
            Quaternion.Euler(0f, 180f, 180f),
        };

        float bestScore = float.NegativeInfinity;
        Quaternion bestRot = candidates[0];
        Bounds bestBounds = default;
        int bestFloor = 0, bestTop = 0;
        string bestDetail = "";

        foreach (var rot in candidates)
        {
            ApplySeat(instance, rot, out Bounds bounds);

            CollectHintBounds(instance, out var floorCenters, out var topCenters);
            if (floorCenters.Count == 0)
            {
                // No named wheels — fall back to height-along-Z only.
                float heightOnly = bounds.size.z;
                if (heightOnly > bestScore)
                {
                    bestScore = heightOnly;
                    bestRot = rot;
                    bestBounds = bounds;
                    bestFloor = 0;
                    bestTop = 0;
                    bestDetail = "no-floor-hints heightZ=" + bounds.size.z;
                }
                continue;
            }

            float floorAvgZ = floorCenters.Average(c => c.z);
            float topAvgZ = topCenters.Count > 0 ? topCenters.Average(c => c.z) : bounds.max.z;
            float height = bounds.size.z;

            // Floor meshes must be near z=0; top meshes near max z; top above floor.
            float floorPenalty = Mathf.Abs(floorAvgZ - bounds.min.z);
            float topReward = topAvgZ - floorAvgZ;
            float seatPenalty = Mathf.Abs(bounds.min.z);

            float candidateScore = topReward * 10f - floorPenalty * 50f - seatPenalty * 20f + height;
            if (floorAvgZ > topAvgZ)
                candidateScore -= 1000f; // upside down

            if (candidateScore > bestScore)
            {
                bestScore = candidateScore;
                bestRot = rot;
                bestBounds = bounds;
                bestFloor = floorCenters.Count;
                bestTop = topCenters.Count;
                bestDetail = $"floorAvgZ={floorAvgZ:F3} topAvgZ={topAvgZ:F3} minZ={bounds.min.z:F3} score={candidateScore:F1}";
            }
        }

        ApplySeat(instance, bestRot, out bestBounds);
        CollectHintBounds(instance, out var finalFloor, out var finalTop);
        float finalFloorAvg = finalFloor.Count > 0 ? finalFloor.Average(c => c.z) : -1f;
        float finalTopAvg = finalTop.Count > 0 ? finalTop.Average(c => c.z) : -1f;
        bool verified = finalFloor.Count > 0
                        && finalFloorAvg < finalTopAvg
                        && finalFloorAvg < bestBounds.min.z + bestBounds.size.z * 0.35f
                        && Mathf.Abs(bestBounds.min.z) < 0.08f;

        lines.Add($"{label} orient pick euler={bestRot.eulerAngles} {bestDetail} verified={verified}");
        if (!verified)
            lines.Add($"FAIL {label} orientation not verified against wheel/base meshes");

        return (bestBounds, bestRot.eulerAngles, verified, bestFloor, bestTop);
    }

    static void ApplySeat(Transform instance, Quaternion rot, out Bounds bounds)
    {
        instance.localRotation = rot;
        instance.localPosition = Vector3.zero;
        instance.localScale = Vector3.one;
        bounds = Encapsulate(instance);
        float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxDim > 30f)
        {
            instance.localScale = Vector3.one * 0.01f;
            bounds = Encapsulate(instance);
        }
        instance.position -= new Vector3(bounds.center.x, bounds.center.y, bounds.min.z);
        bounds = Encapsulate(instance);
    }

    static void CollectHintBounds(Transform instance, out List<Vector3> floorCenters, out List<Vector3> topCenters)
    {
        floorCenters = new List<Vector3>();
        topCenters = new List<Vector3>();
        foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.gameObject.name.ToLowerInvariant();
            // Skip armature widgets
            if (n.StartsWith("wgt"))
                continue;
            if (FloorMeshHints.Any(h => n.Contains(h)))
                floorCenters.Add(r.bounds.center);
            if (TopMeshHints.Any(h => n.Contains(h)))
                topCenters.Add(r.bounds.center);
        }
    }

    static void ClearLocalMetaDataCache(List<string> lines)
    {
        // Wrong seed ("Da Vinci") may already be cached from an earlier play session.
        string[] roots =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "..", "LocalLow"),
            Application.persistentDataPath,
        };

        var guids = new List<string>();
        foreach (var prefabPath in PrefabPaths)
        {
            var importer = AssetImporter.GetAtPath(prefabPath);
            if (importer != null && !string.IsNullOrEmpty(importer.assetBundleName))
                guids.Add(importer.assetBundleName);
        }

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var cacheDir in Directory.GetDirectories(root, "DatabaseCache", SearchOption.AllDirectories))
            {
                foreach (var guid in guids)
                {
                    foreach (var file in Directory.GetFiles(cacheDir, "*" + guid + "*", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                        lines.Add("cleared cache " + file);
                    }
                }
            }
        }

        // Also scrub PlayerPrefs last-modified keys if present (editor batch has its own prefs).
        foreach (var guid in guids)
        {
            string key = "lastModified_" + guid;
            if (PlayerPrefs.HasKey(key))
            {
                PlayerPrefs.DeleteKey(key);
                lines.Add("cleared PlayerPrefs " + key);
            }
        }
        PlayerPrefs.Save();
    }

    static void PackDaVinci5Bundles(List<string> lines)
    {
        foreach (var prefabPath in PrefabPaths)
        {
            var importer = AssetImporter.GetAtPath(prefabPath);
            if (importer == null || string.IsNullOrEmpty(importer.assetBundleName))
            {
                lines.Add("NO BUNDLE NAME " + prefabPath);
                continue;
            }
            lines.Add("pack " + importer.assetBundleName);
        }

        string built = AssetBundleManager.BuildLocalAssetBundles(
            BuildTarget.StandaloneWindows64, forceRebuild: false);
        lines.Add("built packs at " + built);

        string dir = AssetBundleManager.GetBuiltBundlesDirectory(BuildTarget.StandaloneWindows64);
        foreach (var prefabPath in PrefabPaths)
        {
            var importer = AssetImporter.GetAtPath(prefabPath);
            if (importer == null) continue;
            string bundleFile = Path.Combine(dir, importer.assetBundleName);
            lines.Add(File.Exists(bundleFile)
                ? "OK " + importer.assetBundleName + " bytes=" + new FileInfo(bundleFile).Length
                : "MISSING FILE " + bundleFile);
        }
    }

    static Bounds Encapsulate(Transform instance)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
            return new Bounds(instance.position, Vector3.one);
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    /// <summary>
    /// Sample visual mesh verts → QuickHull → small convex mesh asset for MeshCollider.
    /// Kept well under the PlacementLoadOptimizer 100k-vert cook threshold.
    /// </summary>
    static Mesh BakeConvexHullAsset(Transform root, string prefabName, List<string> lines)
    {
        EnsureSourceMeshesReadable(lines);

        var points = new List<Vector3>(4096);
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh src = mf.sharedMesh;
            if (src == null)
                continue;

            Vector3[] verts;
            try
            {
                verts = src.vertices;
            }
            catch (Exception e)
            {
                lines.Add($"WARN {prefabName} cannot read verts on {mf.name}: {e.Message}");
                continue;
            }

            if (verts == null || verts.Length == 0)
                continue;

            Matrix4x4 localToRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            int step = Mathf.Max(1, verts.Length / 64);
            for (int i = 0; i < verts.Length; i += step)
                points.Add(localToRoot.MultiplyPoint3x4(verts[i]));
        }

        // Always include AABB corners so a sparse sample still covers extents.
        var worldBounds = Encapsulate(root);
        for (int ix = 0; ix < 2; ix++)
        for (int iy = 0; iy < 2; iy++)
        for (int iz = 0; iz < 2; iz++)
        {
            Vector3 corner = new Vector3(
                ix == 0 ? worldBounds.min.x : worldBounds.max.x,
                iy == 0 ? worldBounds.min.y : worldBounds.max.y,
                iz == 0 ? worldBounds.min.z : worldBounds.max.z);
            points.Add(root.InverseTransformPoint(corner));
        }

        if (points.Count > 1200)
        {
            // Uniform thinning keeps hull stable without cooking every vert.
            int keep = 1200;
            float stride = points.Count / (float)keep;
            var thinned = new List<Vector3>(keep);
            for (int i = 0; i < keep; i++)
                thinned.Add(points[Mathf.Min(points.Count - 1, Mathf.FloorToInt(i * stride))]);
            points = thinned;
        }

        if (points.Count < 4)
        {
            lines.Add("FAIL " + prefabName + " not enough points for convex hull");
            return null;
        }

        SimpleConvexHull hull;
        try
        {
            hull = new SimpleConvexHull(points);
        }
        catch (Exception e)
        {
            lines.Add("FAIL " + prefabName + " hull build: " + e.Message);
            return null;
        }

        var mesh = new Mesh { name = prefabName + "_convex" };
        var hullVerts = new List<Vector3>(hull.vertices.Count);
        for (int i = 0; i < hull.vertices.Count; i++)
            hullVerts.Add(hull.vertices[i].position);

        var tris = new List<int>(hull.triangles.Count * 3);
        for (int i = 0; i < hull.triangles.Count; i++)
        {
            var t = hull.triangles[i];
            tris.Add(t.x);
            tris.Add(t.y);
            tris.Add(t.z);
        }

        mesh.SetVertices(hullVerts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        const string dir = "Assets/Models/DaVinci5/Hulls";
        Directory.CreateDirectory(dir);
        string path = dir + "/" + prefabName + "_convex.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        AssetDatabase.SaveAssets();

        lines.Add($"{prefabName} convexHull verts={hullVerts.Count} tris={hull.triangles.Count} path={path}");
        return AssetDatabase.LoadAssetAtPath<Mesh>(path);
    }

    static void EnsureSourceMeshesReadable(List<string> lines)
    {
        bool dirty = false;
        foreach (var path in ModelPaths)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                continue;
            if (importer.isReadable)
                continue;
            importer.isReadable = true;
            importer.SaveAndReimport();
            dirty = true;
            lines.Add("set isReadable " + path);
        }
        if (dirty)
            AssetDatabase.SaveAssets();
    }
}
#endif
