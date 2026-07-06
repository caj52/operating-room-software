using SplenSoft.AssetBundles;
using System.Diagnostics;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Logs scene load and transition timings to AssetPipelineDiagnostics.log.
/// </summary>
public static class SceneLoadDiagnostics
{
    private static string _previousSceneName = "(none)";
    private static readonly Stopwatch _transitionWatch = new();
    private static string _activeTransitionLabel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;

        AssetBundleManager.SceneAssetRetrievalStarted.AddListener(OnCdnSceneRetrievalStarted);
        AssetBundleManager.SceneAssetLoaded.AddListener(OnCdnSceneLoaded);

        _transitionWatch.Start();
        Log("App", $"scene diagnostics installed | platform={Application.platform}");
    }

    /// <summary>Call immediately before initiating a scene change.</summary>
    public static void MarkTransitionStart(string label)
    {
        string from = GetActiveSceneNameSafe();
        Log("Transition", $"START '{label}' | from='{from}' | sinceLastEvent={_transitionWatch.ElapsedMilliseconds}ms");
        _activeTransitionLabel = label;
        _transitionWatch.Restart();
    }

    private static void OnCdnSceneRetrievalStarted(string bundleName)
    {
        MarkTransitionStart($"CDN scene '{bundleName}'");
        Log("Transition.CDN", $"retrieval started | bundle={bundleName}");
    }

    private static void OnCdnSceneLoaded(string bundleName)
    {
        Log("Transition.CDN", $"retrieval finished | bundle={bundleName} | total={_transitionWatch.ElapsedMilliseconds}ms");
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        Log("SceneUnloaded", $"'{scene.name}' buildIndex={scene.buildIndex}");
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        long transitionMs = _transitionWatch.ElapsedMilliseconds;
        string label = _activeTransitionLabel ?? "(unspecified)";

        Log("SceneLoaded",
            $"'{scene.name}' buildIndex={scene.buildIndex} mode={mode} | " +
            $"from='{_previousSceneName}' | transition='{label}' | {transitionMs}ms | roots={scene.rootCount}");

        _previousSceneName = scene.name;
        _activeTransitionLabel = null;
        _transitionWatch.Restart();

        ScheduleSceneReadyReports(scene.name);
    }

    internal static void LogSceneReady(string sceneName, string milestone, long elapsedMs, int rootCount)
    {
        Log("SceneReady",
            $"'{sceneName}' {milestone} | {elapsedMs}ms since sceneLoaded | roots={rootCount}");
    }

    private static void ScheduleSceneReadyReports(string sceneName)
    {
        MainThreadDispatcher.Enqueue(() =>
        {
            var runnerGo = new GameObject("SceneLoadDiagnostics_Runner");
            runnerGo.hideFlags = HideFlags.HideAndDontSave;
            runnerGo.AddComponent<SceneReadyRunner>().Begin(sceneName);
        });
    }

    private static string GetActiveSceneNameSafe()
    {
        Scene active = SceneManager.GetActiveScene();
        return active.IsValid() ? active.name : "(invalid)";
    }

    private static void Log(string phase, string message)
    {
        AssetPipelineDiagnostics.Log($"SceneLoad.{phase}", message);
    }

    private sealed class SceneReadyRunner : MonoBehaviour
    {
        public void Begin(string sceneName)
        {
            StartCoroutine(Report(sceneName));
        }

        private IEnumerator Report(string sceneName)
        {
            var watch = Stopwatch.StartNew();
            yield return null;

            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.name == sceneName)
                LogSceneReady(sceneName, "afterFirstFrame", watch.ElapsedMilliseconds, active.rootCount);

            yield return null;

            active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.name == sceneName)
                LogSceneReady(sceneName, "afterSecondFrame", watch.ElapsedMilliseconds, active.rootCount);

            Destroy(gameObject);
        }
    }
}
