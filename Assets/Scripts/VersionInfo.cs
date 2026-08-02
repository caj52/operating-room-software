using System.Collections;
using System.Text.RegularExpressions;
using SplenSoft.AssetBundles;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Sets this object's label to the current product version.
/// </summary>
public class VersionInfo : MonoBehaviour
{
    private void Awake() => Apply();
    private void OnEnable() => Apply();
    private void Start() => Apply();

    private void Apply()
    {
        string versionText = AppVersion.DisplayLabel;

        if (TryGetComponent(out Text text))
            text.text = versionText;
        if (TryGetComponent(out TextMeshProUGUI textMesh))
            textMesh.text = versionText;
    }
}

/// <summary>
/// Product version helpers. Keep Player Settings bundleVersion equal to <see cref="Number"/>.
/// </summary>
public static class AppVersion
{
    public const string Number = "1.5.4";

    public static string DisplayLabel => "OR Software V " + Number;
}

/// <summary>
/// CDN / scene UI often bakes an old "OR Software V x.y.z" string.
/// Re-writes every matching label after UI finishes loading.
/// </summary>
public class UI_VersionLabelSync : MonoBehaviour
{
    private static readonly Regex VersionLabel = new(
        @"OR\s*Software\s*V?\s*[\d.]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static UI_VersionLabelSync _runner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterCallbacks()
    {
        SceneManager.sceneLoaded += (_, __) => Schedule();
        try
        {
            AutoInstantiator.OnJobsFinished.AddListener(Schedule);
        }
        catch
        {
            // AutoInstantiator may not exist in every scene.
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => Schedule();

    private static void Schedule()
    {
        EnsureRunner();
        _runner.StartCoroutine(_runner.SyncSoon());
    }

    private static void EnsureRunner()
    {
        if (_runner != null)
            return;

        var go = new GameObject(nameof(UI_VersionLabelSync));
        Object.DontDestroyOnLoad(go);
        _runner = go.AddComponent<UI_VersionLabelSync>();
    }

    private IEnumerator SyncSoon()
    {
        SyncAll();
        yield return null;
        SyncAll();
        yield return new WaitForSecondsRealtime(0.5f);
        SyncAll();
        yield return new WaitForSecondsRealtime(1.5f);
        SyncAll();
    }

    public static void SyncAll()
    {
        string label = AppVersion.DisplayLabel;

        var tmps = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < tmps.Length; i++)
        {
            var tmp = tmps[i];
            if (tmp == null || string.IsNullOrEmpty(tmp.text))
                continue;
            if (!VersionLabel.IsMatch(tmp.text))
                continue;
            tmp.text = label;
        }

        var texts = Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < texts.Length; i++)
        {
            var t = texts[i];
            if (t == null || string.IsNullOrEmpty(t.text))
                continue;
            if (!VersionLabel.IsMatch(t.text))
                continue;
            t.text = label;
        }
    }
}
