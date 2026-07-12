using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SplenSoft.AssetBundles;

/// <summary>
/// Attached to Button_Quotation (or finds it). Opens the proposal workspace
/// instead of the legacy Panel_PricingQuote.
/// </summary>
[DisallowMultipleComponent]
public class UI_OpenProposalWorkspaceButton : MonoBehaviour
{
    void Awake()
    {
        Wire(GetComponent<Button>());
    }

    void OnEnable()
    {
        Wire(GetComponent<Button>());
    }

    public static void Wire(Button button)
    {
        if (button == null)
            return;

        // New event object drops Inspector persistent SetActive(Panel_PricingQuote).
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(UI_ProposalWorkspace.Open);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AutoInstantiator.OnJobsFinished.RemoveListener(BindAll);
        AutoInstantiator.OnJobsFinished.AddListener(BindAll);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterSceneLoad()
    {
        BindAll();
        ScheduleRetry();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BindAll();
        ScheduleRetry();
    }

    static void BindAll()
    {
        var buttons = Object.FindObjectsByType<Button>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            if (button == null)
                continue;
            if (button.gameObject.name != "Button_Quotation")
                continue;

            if (button.GetComponent<UI_OpenProposalWorkspaceButton>() == null)
                button.gameObject.AddComponent<UI_OpenProposalWorkspaceButton>();
            else
                Wire(button);

            // If the old panel was shown by a lingering listener, hide it.
            HideLegacyPricingPanel();
        }
    }

    static void HideLegacyPricingPanel()
    {
        var transforms = Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null || t.name != "Panel_PricingQuote")
                continue;
            if (t.gameObject.activeSelf)
                t.gameObject.SetActive(false);
        }
    }

    static RetryHost _activeRetry;

    static void ScheduleRetry()
    {
        if (_activeRetry != null)
            return;

        var host = new GameObject(nameof(UI_OpenProposalWorkspaceButton) + "_Retry");
        host.hideFlags = HideFlags.HideAndDontSave;
        _activeRetry = host.AddComponent<RetryHost>();
    }

    sealed class RetryHost : MonoBehaviour
    {
        float _elapsed;
        int _ticks;

        void OnDestroy()
        {
            if (_activeRetry == this)
                _activeRetry = null;
        }

        void Update()
        {
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < 0.5f)
                return;
            _elapsed = 0f;
            BindAll();
            if (++_ticks >= 10)
                Destroy(gameObject);
        }
    }
}
