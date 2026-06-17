using SplenSoft.AssetBundles;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Player builds load UI prefabs from CDN asset bundles. After the Unity 6 upgrade,
/// those bundles can deserialize TMP font/material references as null, which breaks
/// text rendering and spams MaterialReference NullReferenceExceptions every canvas update.
/// </summary>
public static class TMP_RuntimeFontRepair
{
    static TMP_FontAsset _defaultFont;

    static TMP_FontAsset DefaultFont
    {
        get
        {
            if (_defaultFont != null)
                return _defaultFont;

            _defaultFont = TMP_Settings.defaultFontAsset;
            if (_defaultFont == null)
                _defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

            return _defaultFont;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RepairOnStartup() => RepairAll();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterCallbacks()
    {
        AutoInstantiator.OnJobsFinished.AddListener(RepairAll);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RepairAll();
        ScheduleDelayedRepair();
    }

    public static void RepairAll()
    {
        if (DefaultFont == null || DefaultFont.material == null)
            return;

        var texts = Object.FindObjectsByType<TMP_Text>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < texts.Length; i++)
            Repair(texts[i]);
    }

    public static void Repair(TMP_Text text)
    {
        if (text == null || DefaultFont == null || DefaultFont.material == null)
            return;

        bool needsRepair =
            text.font == null ||
            text.font.atlasTexture == null ||
            text.font.material == null ||
            text.fontSharedMaterial == null;

        if (!needsRepair)
            return;

        text.font = DefaultFont;
        text.fontSharedMaterial = DefaultFont.material;
        text.SetAllDirty();
    }

    static void ScheduleDelayedRepair()
    {
        var host = new GameObject(nameof(TMP_RuntimeFontRepair));
        host.hideFlags = HideFlags.HideAndDontSave;
        host.AddComponent<DelayedRepairHost>();
    }

    sealed class DelayedRepairHost : MonoBehaviour
    {
        void Start()
        {
            RepairAll();
            Destroy(gameObject);
        }
    }
}
