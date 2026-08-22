#if UNITY_EDITOR
using SplenSoft.AssetBundles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Overwrites Assets/ placeable prefabs with the GameObjects baked in TestData/CdnMirror
/// (what Play Mode has been loading). Produces self-contained prefabs so Prefab Mode
/// no longer depends on broken nested .blend imports.
/// </summary>
public static class UpdatePrefabsFromCdnMirror
{
    private const string CatalogAssetPath = "Assets/SelectableAssetBundles.asset";
    private const string MirrorFolder = "TestData/CdnMirror";
    private const string SelectablesFolder = "Assets/Prefabs/Selectables";
    private const string AutoRunFlagPath = "Temp/OR_UpdatePrefabsFromCdnMirror.flag";

    [InitializeOnLoadMethod]
    private static void AutoRunIfFlagged()
    {
        string flagPath = Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            AutoRunFlagPath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(flagPath))
            return;

        try { File.Delete(flagPath); }
        catch { /* ignore */ }

        EditorApplication.delayCall += () =>
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[CdnMirror→Prefab] Auto-run deferred — exit Play Mode, then use Tools → Operating Room → Update Prefabs From CdnMirror");
                return;
            }

            Debug.Log("[CdnMirror→Prefab] Auto-run flag detected — updating prefabs from CdnMirror…");
            int updated = Run(showProgress: true, out int skipped, out int failed);
            Debug.Log($"[CdnMirror→Prefab] DONE updated={updated} skipped={skipped} failed={failed}");
            EditorUtility.DisplayDialog(
                "Update Prefabs From CdnMirror",
                $"Updated {updated}. Skipped {skipped}. Failed {failed}.\nRestart Play Mode and open a prefab to verify.",
                "OK");
        };
    }

    [MenuItem("Tools/Operating Room/Update Prefabs From CdnMirror", false, 101)]
    private static void RunFromMenu()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Update Prefabs From CdnMirror", "Stop Play Mode first.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Update Prefabs From CdnMirror",
                "Overwrite selectable prefabs under Assets/Prefabs/Selectables with the versions from TestData/CdnMirror?\n\n" +
                "This keeps what Play Mode has been using and makes Prefab Mode editable.",
                "Update",
                "Cancel"))
        {
            return;
        }

        int updated = Run(showProgress: true, out int skipped, out int failed);
        EditorUtility.DisplayDialog(
            "Update Prefabs From CdnMirror",
            $"Updated {updated}. Skipped {skipped}. Failed {failed}.\nSee Console for details.",
            "OK");
    }

    /// <summary>Batchmode: -executeMethod UpdatePrefabsFromCdnMirror.RunBatch</summary>
    public static void RunBatch()
    {
        int updated = Run(showProgress: false, out int skipped, out int failed);
        Debug.Log($"[CdnMirror→Prefab] DONE updated={updated} skipped={skipped} failed={failed}");
        if (failed > 0)
            EditorApplication.Exit(1);
    }

    /// <summary>Batchmode retry for the two Simeon lights that had corrupt mirror files.</summary>
    public static void RunBatchSimeonLights()
    {
        var only = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gameobject_9548e43dbf80de84fa9980537b084d38", // Simeon_Light_7000
            "gameobject_9d8bfe60207068d43bd3e89a01c90394", // Simeon_Light_8000
        };
        int updated = Run(showProgress: false, out int skipped, out int failed, onlyBundleNames: only);
        Debug.Log($"[CdnMirror→Prefab] Simeon DONE updated={updated} skipped={skipped} failed={failed}");
        if (failed > 0)
            EditorApplication.Exit(1);
    }

    public static int Run(bool showProgress, out int skipped, out int failed, HashSet<string> onlyBundleNames = null)
    {
        skipped = 0;
        failed = 0;
        int updated = 0;

        string mirrorDir = Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            MirrorFolder.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(mirrorDir))
        {
            Debug.LogError($"[CdnMirror→Prefab] Missing folder: {mirrorDir}");
            failed = 1;
            return 0;
        }

        List<(string prefabName, string bundleName)> entries = GetCatalogEntries();
        if (onlyBundleNames != null)
            entries = entries.Where(e => onlyBundleNames.Contains(e.bundleName)).ToList();
        if (entries.Count == 0)
        {
            Debug.LogError("[CdnMirror→Prefab] No catalog entries.");
            failed = 1;
            return 0;
        }

        Dictionary<string, string> prefabPathByBundle = BuildPrefabPathLookup();
        AssetBundleManifest manifest = LoadManifest(mirrorDir);
        if (manifest == null)
        {
            Debug.LogError("[CdnMirror→Prefab] Could not load StandaloneWindows manifest from CdnMirror.");
            failed = 1;
            return 0;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < entries.Count; i++)
            {
                string prefabName = entries[i].prefabName;
                string bundleName = entries[i].bundleName;

                if (showProgress &&
                    EditorUtility.DisplayCancelableProgressBar(
                        "Update Prefabs From CdnMirror",
                        $"{i + 1}/{entries.Count}  {prefabName}",
                        (i + 1f) / entries.Count))
                {
                    Debug.LogWarning("[CdnMirror→Prefab] Cancelled.");
                    break;
                }

                if (!prefabPathByBundle.TryGetValue(bundleName, out string prefabPath))
                {
                    Debug.LogWarning($"[CdnMirror→Prefab] No Assets prefab for {prefabName} ({bundleName}) — skip");
                    skipped++;
                    continue;
                }

                string bundlePath = Path.Combine(mirrorDir, bundleName);
                if (!File.Exists(bundlePath))
                {
                    Debug.LogWarning($"[CdnMirror→Prefab] Missing mirror file {bundleName} — skip");
                    skipped++;
                    continue;
                }

                try
                {
                    if (WritePrefabFromMirror(mirrorDir, manifest, bundleName, prefabPath))
                    {
                        updated++;
                        Debug.Log($"[CdnMirror→Prefab] OK {prefabName} → {prefabPath}");
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Debug.LogError($"[CdnMirror→Prefab] FAIL {prefabName}: {ex.Message}\n{ex}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            if (showProgress)
                EditorUtility.ClearProgressBar();
            AssetBundle.UnloadAllAssetBundles(unloadAllObjects: true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        return updated;
    }

    private static bool WritePrefabFromMirror(
        string mirrorDir,
        AssetBundleManifest manifest,
        string bundleName,
        string prefabPath)
    {
        var loaded = new List<AssetBundle>();
        GameObject instance = null;

        try
        {
            foreach (string dep in manifest.GetAllDependencies(bundleName))
            {
                string depPath = Path.Combine(mirrorDir, dep);
                if (!File.Exists(depPath))
                {
                    Debug.LogWarning($"[CdnMirror→Prefab] Missing dependency {dep} for {bundleName}");
                    continue;
                }

                AssetBundle depBundle = AssetBundle.LoadFromFile(depPath);
                if (depBundle != null)
                    loaded.Add(depBundle);
            }

            AssetBundle main = AssetBundle.LoadFromFile(Path.Combine(mirrorDir, bundleName));
            if (main == null)
            {
                Debug.LogError($"[CdnMirror→Prefab] LoadFromFile failed: {bundleName}");
                return false;
            }

            loaded.Add(main);

            GameObject source = main.LoadAsset<GameObject>(
                main.GetAllAssetNames().FirstOrDefault(n => n.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                ?? "");

            if (source == null)
            {
                var all = main.LoadAllAssets<GameObject>();
                source = all.FirstOrDefault(g => g.transform.parent == null) ?? all.FirstOrDefault();
            }

            if (source == null)
            {
                Debug.LogError($"[CdnMirror→Prefab] No GameObject in {bundleName}");
                return false;
            }

            instance = UnityEngine.Object.Instantiate(source);
            instance.name = source.name;

            if (PrefabUtility.IsAnyPrefabInstanceRoot(instance))
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
            }

            // Persist meshes/materials/textures into Assets so Prefab Mode + Play Mode
            // (editor assets) keep real geometry — SaveAsPrefabAsset alone drops bundle meshes.
            PersistBundleAssetsToProject(instance, bundleName);
            RebindMonoScripts(instance);
            StripMissingScripts(instance);

            string absDir = Path.GetDirectoryName(Path.GetFullPath(prefabPath));
            if (!string.IsNullOrEmpty(absDir) && !Directory.Exists(absDir))
                Directory.CreateDirectory(absDir);

            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool success);
            if (!success)
            {
                Debug.LogError($"[CdnMirror→Prefab] SaveAsPrefabAsset failed: {prefabPath}");
                return false;
            }

            // Keep bundle name so GetAsset / catalog resolve this prefab by AssetBundleName.
            AssetImporter importer = AssetImporter.GetAtPath(prefabPath);
            if (importer != null)
                importer.SetAssetBundleNameAndVariant(bundleName, string.Empty);

            // Verify on the live instance (AssetDatabase checks fail inside StartAssetEditing).
            int meshFilters = instance.GetComponentsInChildren<MeshFilter>(true).Length;
            int assigned = instance.GetComponentsInChildren<MeshFilter>(true).Count(f => f.sharedMesh != null);
            if (meshFilters > 0 && assigned == 0)
            {
                Debug.LogError($"[CdnMirror→Prefab] No meshes assigned on instance before save: {prefabPath}");
                return false;
            }

            return true;
        }
        finally
        {
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance);

            for (int i = loaded.Count - 1; i >= 0; i--)
            {
                if (loaded[i] != null)
                    loaded[i].Unload(unloadAllLoadedObjects: false);
            }
        }
    }

    private static AssetBundleManifest LoadManifest(string mirrorDir)
    {
        // Windows Editor uses StandaloneWindows (build target 5) in this project's settings.
        string[] candidates =
        {
            Path.Combine(mirrorDir, "StandaloneWindows"),
            Path.Combine(mirrorDir, "StandaloneWindows64"),
        };

        foreach (string path in candidates)
        {
            if (!File.Exists(path))
                continue;

            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
                continue;

            try
            {
                AssetBundleManifest manifest = bundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                if (manifest != null)
                    return manifest;
            }
            finally
            {
                bundle.Unload(unloadAllLoadedObjects: false);
            }
        }

        return null;
    }

    private static List<(string prefabName, string bundleName)> GetCatalogEntries()
    {
        var result = new List<(string, string)>();
        var catalog = AssetDatabase.LoadAssetAtPath<SelectableAssetBundles>(CatalogAssetPath);
        if (catalog == null)
            return result;

        SerializedObject so = new SerializedObject(catalog);
        SerializedProperty list = so.FindProperty("_selectableData");
        if (list == null || !list.isArray)
            return result;

        for (int i = 0; i < list.arraySize; i++)
        {
            SerializedProperty e = list.GetArrayElementAtIndex(i);
            string name = e.FindPropertyRelative("<PrefabName>k__BackingField")?.stringValue;
            string bundle = e.FindPropertyRelative("<AssetBundleName>k__BackingField")?.stringValue;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(bundle))
                continue;
            result.Add((name, bundle));
        }

        return result
            .GroupBy(x => x.Item2, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> BuildPrefabPathLookup()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SelectablesFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer == null || string.IsNullOrEmpty(importer.assetBundleName))
                continue;
            map[importer.assetBundleName] = path;
        }

        return map;
    }

    private static bool PrefabHasAssignedMeshes(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            return false;

        var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length == 0)
            return true; // e.g. pure UI / empty placeholders

        int withMesh = filters.Count(f => f.sharedMesh != null);
        return withMesh > 0;
    }

    private static void PersistBundleAssetsToProject(GameObject root, string bundleName)
    {
        string folder = $"Assets/_MirrorExtract/{SanitizePathSegment(bundleName)}";
        if (!AssetDatabase.IsValidFolder("Assets/_MirrorExtract"))
            AssetDatabase.CreateFolder("Assets", "_MirrorExtract");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/_MirrorExtract", SanitizePathSegment(bundleName));

        var meshMap = new Dictionary<Mesh, Mesh>();
        var matMap = new Dictionary<Material, Material>();
        var texMap = new Dictionary<Texture, Texture>();

        Mesh PersistMesh(Mesh src)
        {
            if (src == null)
                return null;
            if (meshMap.TryGetValue(src, out Mesh existing))
                return existing;

            Mesh copy = UnityEngine.Object.Instantiate(src);
            copy.name = string.IsNullOrEmpty(src.name) ? "Mesh" : src.name;
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SanitizePathSegment(copy.name)}.mesh.asset");
            AssetDatabase.CreateAsset(copy, path);
            meshMap[src] = copy;
            return copy;
        }

        Texture PersistTexture(Texture src)
        {
            if (src == null)
                return null;
            if (texMap.TryGetValue(src, out Texture existing))
                return existing;

            // Already a project asset?
            string existingPath = AssetDatabase.GetAssetPath(src);
            if (!string.IsNullOrEmpty(existingPath) && existingPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                texMap[src] = src;
                return src;
            }

            if (src is Texture2D tex2d)
            {
                Texture2D copy = UnityEngine.Object.Instantiate(tex2d);
                copy.name = string.IsNullOrEmpty(tex2d.name) ? "Tex" : tex2d.name;
                string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SanitizePathSegment(copy.name)}.texture.asset");
                AssetDatabase.CreateAsset(copy, path);
                texMap[src] = copy;
                return copy;
            }

            texMap[src] = src;
            return src;
        }

        Material PersistMaterial(Material src)
        {
            if (src == null)
                return null;
            if (matMap.TryGetValue(src, out Material existing))
                return existing;

            string existingPath = AssetDatabase.GetAssetPath(src);
            if (!string.IsNullOrEmpty(existingPath) && existingPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                matMap[src] = src;
                return src;
            }

            Material copy = new Material(src)
            {
                name = string.IsNullOrEmpty(src.name) ? "Mat" : src.name
            };

            // Bundle-loaded shaders serialize as guid:000… and pink in the editor.
            // Rebind by name so CreateAsset writes a real package shader GUID.
            if (src.shader != null && !string.IsNullOrEmpty(src.shader.name))
            {
                Shader resolved = Shader.Find(src.shader.name);
                if (resolved != null)
                    copy.shader = resolved;
            }

            Shader shader = copy.shader;
            if (shader != null)
            {
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                        continue;
                    string prop = shader.GetPropertyName(i);
                    Texture t = copy.GetTexture(prop);
                    if (t != null)
                        copy.SetTexture(prop, PersistTexture(t));
                }
            }

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SanitizePathSegment(copy.name)}.mat");
            AssetDatabase.CreateAsset(copy, path);
            matMap[src] = copy;
            return copy;
        }

        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            mf.sharedMesh = PersistMesh(mf.sharedMesh);

        foreach (MeshCollider mc in root.GetComponentsInChildren<MeshCollider>(true))
            mc.sharedMesh = PersistMesh(mc.sharedMesh);

        foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.sharedMesh = PersistMesh(smr.sharedMesh);
            var mats = smr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = PersistMaterial(mats[i]);
            smr.sharedMaterials = mats;
        }

        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is SkinnedMeshRenderer)
                continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = PersistMaterial(mats[i]);
            r.sharedMaterials = mats;
        }
    }

    private static string SanitizePathSegment(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "asset";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    private static void RebindMonoScripts(GameObject root)
    {
        // Bundle instances resolve scripts in memory but SaveAsPrefabAsset writes guid:000…
        // unless we reassign each m_Script to the project MonoScript first.
        foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null)
                continue;

            MonoScript script = MonoScript.FromMonoBehaviour(mb);
            if (script == null)
                continue;

            SerializedObject so = new SerializedObject(mb);
            SerializedProperty prop = so.FindProperty("m_Script");
            if (prop == null || prop.objectReferenceValue == script)
                continue;

            prop.objectReferenceValue = script;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void StripMissingScripts(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
    }
}
#endif
