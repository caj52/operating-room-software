using SplenSoft.AssetBundles;
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Captures the selectable catalog, asset-bundle, menu, and room-load pipeline
/// to TestData/AssetPipelineDiagnostics.log (editor) and persistentDataPath.
/// </summary>
public static class AssetPipelineDiagnostics
{
    private static readonly object _lock = new();
    private static string _sessionId;
    private static bool _subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        _sessionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        SubscribeToAssetBundleManager();
        LogStartupConfig();
    }

    public static void Log(string phase, string message)
    {
        string line = $"[{DateTime.UtcNow:HH:mm:ss.fff}] [{phase}] {message}";
        Debug.Log($"[AssetPipeline] {line}");

        lock (_lock)
        {
            try
            {
                foreach (string path in GetLogPaths())
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetPipeline] Failed to write log: {ex.Message}");
            }
        }
    }

    public static void LogSelectableData(string phase, SelectableData data, string extra = null)
    {
        if (data == null)
        {
            Log(phase, "SelectableData is null" + (extra != null ? $" | {extra}" : ""));
            return;
        }

        Log(phase,
            $"PrefabName={data.PrefabName} SaveLoadGuid={data.SaveLoadGuid} " +
            $"AssetBundleName={data.AssetBundleName}" +
            (extra != null ? $" | {extra}" : ""));
    }

    public static void LogPrefabSnapshot(string phase, GameObject go, string label)
    {
        if (go == null)
        {
            Log(phase, $"{label}: prefab is NULL");
            return;
        }

        var sb = new StringBuilder();
        sb.Append($"{label}: name='{go.name}' active={go.activeSelf} children={go.transform.childCount}");

        var meshFilters = go.GetComponentsInChildren<MeshFilter>(true);
        var meshRenderers = go.GetComponentsInChildren<MeshRenderer>(true);
        var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int enabledRenderers = meshRenderers.Count(r => r != null && r.enabled);

        sb.Append($" | MeshFilter={meshFilters.Length} MeshRenderer={meshRenderers.Length}(enabled={enabledRenderers}) Skinned={skinned.Length}");

        if (meshFilters.Length > 0)
        {
            var meshNames = meshFilters
                .Where(mf => mf != null && mf.sharedMesh != null)
                .Select(mf => mf.sharedMesh.name)
                .Distinct()
                .Take(5);
            sb.Append($" | meshes=[{string.Join(", ", meshNames)}]");
        }

        if (meshRenderers.Length > 0)
        {
            var matNames = meshRenderers
                .Where(mr => mr != null && mr.sharedMaterial != null)
                .Select(mr => mr.sharedMaterial.name)
                .Distinct()
                .Take(5);
            sb.Append($" | materials=[{string.Join(", ", matNames)}]");
        }

        if (go.transform.childCount > 0)
        {
            var childNames = Enumerable.Range(0, go.transform.childCount)
                .Select(i => go.transform.GetChild(i).name)
                .Take(8);
            sb.Append($" | childNames=[{string.Join(", ", childNames)}]");
        }

#if UNITY_EDITOR
        string assetPath = AssetDatabase.GetAssetPath(go);
        if (!string.IsNullOrEmpty(assetPath))
            sb.Append($" | editorPath={assetPath}");

        var prefabType = PrefabUtility.GetPrefabAssetType(go);
        var prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(go);
        sb.Append($" | prefabAssetType={prefabType}");
        if (prefabSource != null)
            sb.Append($" | variantSource={prefabSource.name}");
#endif

        if (meshRenderers.Length == 0 && skinned.Length == 0)
            sb.Append(" | WARNING: no renderers — likely invisible or placeholder mesh");

        Log(phase, sb.ToString());
    }

    private static void SubscribeToAssetBundleManager()
    {
        if (_subscribed) return;
        _subscribed = true;

        AssetBundleManager.DiagnosticLog = (phase, msg) => Log(phase, msg);
        AutoInstantiator.DiagnosticLog = (phase, msg) => Log(phase, msg);

        AssetBundleManager.AssetRetrievalStarted.AddListener(name =>
            Log("ABM.AssetRetrievalStarted", name));

        AssetBundleManager.AssetLoaded.AddListener(name =>
            Log("ABM.AssetLoaded", name));

        AssetBundleManager.AssetBundleDownloadStarted.AddListener(name =>
            Log("ABM.BundleDownloadStarted", name));

        AssetBundleManager.AssetBundleDownloadFinished.AddListener(name =>
            Log("ABM.BundleDownloadFinished", name));
    }

    private static void LogStartupConfig()
    {
        var settings = AssetBundleManagerSettings.Get();
        Log("Session", $"id={_sessionId} unity={Application.unityVersion} platform={Application.platform} isEditor={Application.isEditor} isPlaying={Application.isPlaying}");
        Log("Config", $"UseEditorAssetsIfAble={settings.UseEditorAssetsIfAble} KeepLocalCopy={settings.KeepLocalCopy} Version={settings.Version} Environment={settings.ActiveEnvironmentId}");

        if (settings.BuildTargetsByPlatform.TryGetValue(Application.platform, out int buildTarget))
        {
            string targetName = settings.BuildTargetNames.TryGetValue(buildTarget, out string n) ? n : "?";
            Log("Config", $"Platform {Application.platform} -> buildTarget={buildTarget} ({targetName})");
        }
        else
        {
            Log("Config", $"WARNING: Platform {Application.platform} has no BuildTargetsByPlatform entry");
        }

        Log("Config", $"Log paths: {string.Join(" ; ", GetLogPaths())}");
    }

    private static string[] GetLogPaths()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string testDataLog = Path.Combine(projectRoot, "TestData", "AssetPipelineDiagnostics.log");
        string persistentLog = Path.Combine(Application.persistentDataPath, "AssetPipelineDiagnostics.log");
        return new[] { testDataLog, persistentLog };
    }
}
