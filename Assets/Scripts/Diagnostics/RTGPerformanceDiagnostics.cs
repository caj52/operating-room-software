using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using RTG;

/// <summary>
/// Samples RTGApp module Update costs and related scene stats to AssetPipelineDiagnostics.log.
/// After room load, captures ~5s of frames then writes a summary for before/after comparisons.
/// </summary>
public static class RTGPerformanceDiagnostics
{
    public static bool Enabled { get; set; } = false;

    /// <summary>
    /// Set for A/B runs. UnityColliders = run 2. null = use prefab value.
    /// </summary>
    public static ScenePhysicsMode? PhysicsModeOverride { get; set; }

    private const int SampleFrames = 300;
    private const int WarmupFrames = 30;

    private static readonly Dictionary<string, List<long>> _samples = new();
    private static readonly Stopwatch _sw = new();
    private static string _activeSample;
    private static int _frameCounter;
    private static bool _sampling;
    private static string _sessionLabel = "manual";
    private static long _physicsModeAtStart;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!Enabled)
            return;

        var probe = new GameObject(nameof(RTGPerformanceProbe));
        probe.AddComponent<RTGPerformanceProbe>();
        UnityEngine.Object.DontDestroyOnLoad(probe);

        ConfigurationManager.OnRoomLoadComplete.AddListener(OnRoomLoadComplete);
        AssetPipelineDiagnostics.Log("RTG.Benchmark", "enabled — will sample 300 frames after room load");
    }

    public static void BeginSample(string name)
    {
        if (!_sampling)
            return;

        _activeSample = name;
        _sw.Restart();
    }

    public static void EndSample()
    {
        if (!_sampling || _activeSample == null)
            return;

        _sw.Stop();
        if (!_samples.TryGetValue(_activeSample, out List<long> list))
        {
            list = new List<long>(SampleFrames);
            _samples[_activeSample] = list;
        }

        list.Add(_sw.ElapsedTicks);
        _activeSample = null;
    }

    public static void NotifyFrameEnd()
    {
        if (!_sampling)
            return;

        _frameCounter++;
        if (_frameCounter <= WarmupFrames)
            return;

        if (_frameCounter == WarmupFrames + 1)
        {
            _physicsModeAtStart = RTScene.Get != null ? (long)RTScene.Get.Settings.PhysicsMode : _physicsModeAtStart;
            AssetPipelineDiagnostics.Log("RTG.Benchmark", $"sampling started label={_sessionLabel} physicsMode={_physicsModeAtStart}");
        }

        if (_frameCounter >= WarmupFrames + SampleFrames)
        {
            FinishAndLog();
            return;
        }

        if (_frameCounter == WarmupFrames + SampleFrames / 2)
            AssetPipelineDiagnostics.Log("RTG.Benchmark", $"halfway label={_sessionLabel} frame={_frameCounter - WarmupFrames}/{SampleFrames}");
    }

    public static void StartSampling(string label)
    {
        if (!Enabled)
            return;

        _sessionLabel = label;
        _frameCounter = 0;
        _samples.Clear();
        _sampling = true;
        _physicsModeAtStart = RTScene.Get != null ? (long)RTScene.Get.Settings.PhysicsMode : -1;

        int selectableCount = UnityEngine.Object.FindObjectsOfType<Selectable>(true).Length;
        int goCount = CountSceneGameObjects();
        AssetPipelineDiagnostics.Log("RTG.Benchmark",
            $"arm label={label} physicsMode={_physicsModeAtStart} selectables={selectableCount} gameObjects={goCount}");
    }

    private static void OnRoomLoadComplete()
    {
        if (!Enabled || SceneManager.GetActiveScene().name == "ObjectEditor")
            return;

        StartSampling("post_room_load");
    }

    private static void FinishAndLog()
    {
        _sampling = false;

        AssetPipelineDiagnostics.Log("RTG.Benchmark", $"summary label={_sessionLabel} physicsMode={_physicsModeAtStart} frames={SampleFrames}");

        long totalAvgTicks = 0;
        foreach (var kv in _samples)
        {
            long avg = AverageTicks(kv.Value);
            long max = MaxTicks(kv.Value);
            long p95 = PercentileTicks(kv.Value, 0.95);
            totalAvgTicks += avg;

            AssetPipelineDiagnostics.Log("RTG.Benchmark",
                $"  {kv.Key} avgMs={TicksToMs(avg):F3} p95Ms={TicksToMs(p95):F3} maxMs={TicksToMs(max):F3}");
        }

        AssetPipelineDiagnostics.Log("RTG.Benchmark",
            $"  RTGApp.Update(total) avgMs={TicksToMs(totalAvgTicks):F3}");
    }

    private static int CountSceneGameObjects()
    {
        int count = 0;
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (GameObject root in roots)
            count += root.transform.childCount + 1;
        return count;
    }

    private static long AverageTicks(List<long> ticks)
    {
        if (ticks.Count == 0) return 0;
        long sum = 0;
        foreach (long t in ticks) sum += t;
        return sum / ticks.Count;
    }

    private static long MaxTicks(List<long> ticks)
    {
        long max = 0;
        foreach (long t in ticks)
            if (t > max) max = t;
        return max;
    }

    private static long PercentileTicks(List<long> ticks, double p)
    {
        if (ticks.Count == 0) return 0;
        var sorted = new List<long>(ticks);
        sorted.Sort();
        int idx = Mathf.Clamp(Mathf.RoundToInt((float)((sorted.Count - 1) * p)), 0, sorted.Count - 1);
        return sorted[idx];
    }

    private static double TicksToMs(long ticks)
        => ticks * 1000.0 / Stopwatch.Frequency;

    private sealed class RTGPerformanceProbe : MonoBehaviour
    {
        private void Start()
        {
            StartCoroutine(ApplyPhysicsModeWhenReady());
        }

        private IEnumerator ApplyPhysicsModeWhenReady()
        {
            while (RTScene.Get == null)
                yield return null;

            if (PhysicsModeOverride.HasValue)
                SetPhysicsMode(PhysicsModeOverride.Value);

            AssetPipelineDiagnostics.Log("RTG.Benchmark",
                $"runtime physicsMode={(long)RTScene.Get.Settings.PhysicsMode} override={PhysicsModeOverride?.ToString() ?? "none"}");
        }

        private void LateUpdate()
        {
            RTGPerformanceDiagnostics.NotifyFrameEnd();
        }

        private void OnDestroy()
        {
            RTGPerformanceDiagnostics.LogIfIncomplete();
        }
    }

    public static void LogIfIncomplete()
    {
        if (!_sampling || _frameCounter <= WarmupFrames)
            return;

        int sampled = _frameCounter - WarmupFrames;
        AssetPipelineDiagnostics.Log("RTG.Benchmark",
            $"INCOMPLETE label={_sessionLabel} physicsMode={_physicsModeAtStart} sampled={sampled}/{SampleFrames} (play session ended early)");
        FinishAndLog();
    }

    private static void SetPhysicsMode(ScenePhysicsMode mode)
    {
        FieldInfo field = typeof(SceneSettings).GetField("_physicsMode",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null)
            return;

        field.SetValue(RTScene.Get.Settings, mode);
    }
}
