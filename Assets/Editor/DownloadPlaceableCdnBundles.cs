#if UNITY_EDITOR
using SplenSoft.AssetBundles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Editor-only: download every placeable prefab bundle plus all CDN dependencies
/// listed in the platform manifest. Saves everything to TestData/CdnMirror/.
/// </summary>
public static class DownloadPlaceableCdnBundles
{
    private const string CatalogAssetPath = "Assets/SelectableAssetBundles.asset";
    private const string OutputFolder = "TestData/CdnMirror";

    [MenuItem("Tools/Operating Room/Download Placeable CDN Bundles")]
    private static void DownloadFromMenu()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Download Placeable CDN Bundles", "Stop Play Mode first.", "OK");
            return;
        }

        List<string> placeables = GetPlaceableBundleNamesFromCatalog();
        if (placeables.Count == 0)
        {
            EditorUtility.DisplayDialog("Download Placeable CDN Bundles", "No placeable bundles found in catalog.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog(
            "Download Placeable CDN Bundles",
            $"Download {placeables.Count} placeable prefab bundles plus their CDN dependencies to {OutputFolder}/ ?\n\n" +
            "(Files already in that folder are skipped.)",
            "Download",
            "Cancel"))
        {
            return;
        }

        try
        {
            var settings = AssetBundleManagerSettings.Get();
            string baseUrl = settings.GetAssetBundleURL();
            string outputDir = GetOutputDir();
            Directory.CreateDirectory(outputDir);

            List<string> bundleNames = GetAllBundlesToDownload(
                settings, baseUrl, outputDir, placeables, out string manifestName);

            int depsOnly = bundleNames.Count - placeables.Count;
            Debug.Log(
                $"[CdnMirror] {placeables.Count} placeables + {depsOnly} dependency bundles " +
                $"(manifest: {manifestName})");

            int ok = 0;
            int failed = 0;
            int skipped = 0;

            for (int i = 0; i < bundleNames.Count; i++)
            {
                string bundleName = bundleNames[i];
                float progress = (i + 1f) / bundleNames.Count;

                if (EditorUtility.DisplayCancelableProgressBar(
                    "Downloading placeable CDN bundles",
                    $"{i + 1}/{bundleNames.Count}  {bundleName}",
                    progress))
                {
                    Debug.LogWarning("[CdnMirror] Cancelled.");
                    break;
                }

                string destPath = Path.Combine(outputDir, bundleName);
                if (File.Exists(destPath))
                {
                    skipped++;
                    continue;
                }

                if (DownloadFile(baseUrl + UnityWebRequest.EscapeURL(bundleName), destPath))
                    ok++;
                else
                    failed++;
            }

            string msg =
                $"Folder: {outputDir}\n\n" +
                $"Placeables: {placeables.Count}\n" +
                $"Dependencies: {depsOnly}\n" +
                $"Total bundles: {bundleNames.Count}\n" +
                $"New downloads: {ok}\n" +
                $"Already had: {skipped}";

            if (failed > 0)
                msg += $"\nFailed: {failed} (see Console)";

            Debug.Log($"[CdnMirror] Done.\n{msg}");
            EditorUtility.DisplayDialog("Download Complete", msg, "OK");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Download Failed", ex.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static List<string> GetPlaceableBundleNamesFromCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<SelectableAssetBundles>(CatalogAssetPath);
        if (catalog == null)
            throw new FileNotFoundException($"Could not find {CatalogAssetPath}");

        var names = new List<string>();
        var so = new SerializedObject(catalog);
        SerializedProperty arr = so.FindProperty("_selectableData");
        if (arr == null || !arr.isArray)
            throw new InvalidOperationException("Could not read catalog entries.");

        for (int i = 0; i < arr.arraySize; i++)
        {
            string bundleName = arr.GetArrayElementAtIndex(i)
                .FindPropertyRelative("<AssetBundleName>k__BackingField")?.stringValue;

            if (!string.IsNullOrWhiteSpace(bundleName) &&
                bundleName.StartsWith("gameobject_", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(bundleName);
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
    }

    private static List<string> GetAllBundlesToDownload(
        AssetBundleManagerSettings settings,
        string baseUrl,
        string outputDir,
        List<string> placeables,
        out string manifestBundleName)
    {
        manifestBundleName = GetManifestBundleName(settings);
        string manifestPath = Path.Combine(outputDir, manifestBundleName);

        if (!File.Exists(manifestPath) &&
            !DownloadFile(baseUrl + UnityWebRequest.EscapeURL(manifestBundleName), manifestPath))
        {
            throw new InvalidOperationException($"Could not download manifest bundle {manifestBundleName}.");
        }

        AssetBundle manifestBundle = AssetBundle.LoadFromFile(manifestPath);
        if (manifestBundle == null)
            throw new InvalidOperationException($"Could not open manifest file at {manifestPath}.");

        try
        {
            AssetBundleManifest manifest = manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            if (manifest == null)
                throw new InvalidOperationException("Manifest file did not contain AssetBundleManifest.");

            return GetDependencyBundleNames(manifest, placeables);
        }
        finally
        {
            manifestBundle.Unload(unloadAllLoadedObjects: true);
        }
    }

    private static List<string> GetDependencyBundleNames(
        AssetBundleManifest manifest,
        IReadOnlyList<string> placeables)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);

        foreach (string placeable in placeables)
        {
            all.Add(placeable);
            foreach (string dep in manifest.GetAllDependencies(placeable))
                all.Add(dep);
        }

        return all.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    private static string GetOutputDir()
    {
        return Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            OutputFolder.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetManifestBundleName(AssetBundleManagerSettings settings)
    {
        if (!settings.BuildTargetsByPlatform.TryGetValue(Application.platform, out int buildTarget))
            throw new InvalidOperationException(
                $"Platform {Application.platform} is not configured in Asset Bundle Manager settings.");

        if (!settings.BuildTargetNames.TryGetValue(buildTarget, out string manifestName))
            throw new InvalidOperationException(
                $"Build target {buildTarget} has no name in settings.");

        return manifestName;
    }

    private static bool DownloadFile(string url, string destPath)
    {
        using var request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerFile(destPath);

        var op = request.SendWebRequest();
        while (!op.isDone)
        {
            if (EditorUtility.DisplayCancelableProgressBar(
                "Downloading placeable CDN bundles",
                Path.GetFileName(destPath),
                op.progress))
            {
                request.Abort();
                if (File.Exists(destPath))
                    File.Delete(destPath);
                throw new OperationCanceledException("Cancelled.");
            }
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[CdnMirror] Failed {Path.GetFileName(destPath)}: {request.error}");
            if (File.Exists(destPath))
                File.Delete(destPath);
            return false;
        }

        Debug.Log($"[CdnMirror] Saved {Path.GetFileName(destPath)}");
        return true;
    }
}
#endif
