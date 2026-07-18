using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Client Data overlay. Spawned by AutoInstantiator (often active). Only <see cref="Open"/>
/// may show it; scene changes always hide it.
/// Sales rep is PlayerPrefs-only — edit via the proposal Sales Rep hotspot, not this form.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class UI_ClientMetaData : MonoBehaviour
{
    private const string PrefsSalesRepName = "SalesProposal.SalesRepName";
    private const string PrefsSalesRepPhone = "SalesProposal.SalesRepPhone";
    private const string PrefsSalesRepEmail = "SalesProposal.SalesRepEmail";

    private static UI_ClientMetaData Instance { get; set; }

    public static UnityEvent OnClosed { get; set; } = new();

    /// <summary>True when the last close persisted different values than when Open ran.</summary>
    public static bool LastCloseHadChanges { get; private set; }

    private string _fingerprintOnOpen = "";
    private bool _open;

    public static string AccountName => ReadField(Instance?.InputFieldAccountName);
    [field: SerializeField]
    private TMP_InputField InputFieldAccountName
    { get; set; }

    public static string AccountAddressLine1 => ReadField(Instance?.InputFieldAccountAddressLine1);
    [field: SerializeField]
    private TMP_InputField InputFieldAccountAddressLine1
    { get; set; }

    public static string AccountAddressLine2 => ReadField(Instance?.InputFieldAccountAddressLine2);
    [field: SerializeField]
    private TMP_InputField InputFieldAccountAddressLine2
    { get; set; }

    public static string ProjectName => ReadField(Instance?.InputFieldProjectName);
    [field: SerializeField]
    private TMP_InputField InputFieldProjectName
    { get; set; }

    public static string ProjectNumber => ReadField(Instance?.InputFieldProjectNumber);
    [field: SerializeField]
    private TMP_InputField InputFieldProjectNumber
    { get; set; }

    public static string OrderReferenceNumber => ReadField(Instance?.InputFieldOrderReferenceNumber);
    [field: SerializeField]
    private TMP_InputField InputFieldOrderReferenceNumber
    { get; set; }

    public static string SalesRepName
    {
        get => PlayerPrefs.GetString(PrefsSalesRepName, "");
        set
        {
            PlayerPrefs.SetString(PrefsSalesRepName, value?.Trim() ?? "");
            PlayerPrefs.Save();
        }
    }

    public static string SalesRepPhone
    {
        get => PlayerPrefs.GetString(PrefsSalesRepPhone, "");
        set
        {
            PlayerPrefs.SetString(PrefsSalesRepPhone, value?.Trim() ?? "");
            PlayerPrefs.Save();
        }
    }

    public static string SalesRepEmail
    {
        get => PlayerPrefs.GetString(PrefsSalesRepEmail, "");
        set
        {
            PlayerPrefs.SetString(PrefsSalesRepEmail, value?.Trim() ?? "");
            PlayerPrefs.Save();
        }
    }

    private static string ReadField(TMP_InputField field)
    {
        if (field == null || string.IsNullOrWhiteSpace(field.text))
            return "";
        string t = field.text.Trim();
        if (t.Equals("N/A", System.StringComparison.OrdinalIgnoreCase))
            return "";
        return t;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        OnClosed = new UnityEvent();
        LastCloseHadChanges = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureSceneHook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Close();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _open = false;

        // Hide first — setup below must never leave this canvas visible by default.
        gameObject.SetActive(false);
        DontDestroyOnLoad(gameObject);
        RemoveLegacySalesRepFields();
    }

    private void OnDisable()
    {
        if (!_open)
            return;

        string after = BuildFingerprint();
        LastCloseHadChanges = !string.Equals(after, _fingerprintOnOpen, StringComparison.Ordinal);
        _open = false;
        OnClosed?.Invoke();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static bool IsOpen => Instance != null && Instance._open && Instance.gameObject.activeSelf;

    public static void Open()
    {
        if (Instance == null)
        {
            Debug.LogWarning("UI_ClientMetaData is not in the scene.");
            return;
        }

        Instance.RemoveLegacySalesRepFields();
        Instance.EnsureVisibleCaretsOnAllFields();
        Instance._fingerprintOnOpen = Instance.BuildFingerprint();
        LastCloseHadChanges = false;
        Instance._open = true;
        Instance.gameObject.SetActive(true);
    }

    public static void Close()
    {
        if (Instance == null)
            return;

        // OnDisable fires OnClosed while _open is still true.
        if (Instance.gameObject.activeSelf)
            Instance.gameObject.SetActive(false);
        else
            Instance._open = false;
    }

    string BuildFingerprint()
    {
        return string.Join("\u001f",
            ReadField(InputFieldAccountName),
            ReadField(InputFieldAccountAddressLine1),
            ReadField(InputFieldAccountAddressLine2),
            ReadField(InputFieldProjectName),
            ReadField(InputFieldProjectNumber),
            ReadField(InputFieldOrderReferenceNumber));
    }

    /// <summary>
    /// Older builds cloned Sales Rep inputs into this panel. Strip them if present.
    /// </summary>
    void RemoveLegacySalesRepFields()
    {
        string[] names =
        {
            "InputField_SalesRepName",
            "InputField_SalesRepPhone",
            "InputField_SalesRepEmail",
        };

        Transform parent = InputFieldProjectName != null
            ? InputFieldProjectName.transform.parent
            : transform;

        foreach (string name in names)
        {
            var t = parent != null ? parent.Find(name) : null;
            if (t == null)
                t = transform.Find(name);
            if (t != null)
                Destroy(t.gameObject);
        }
    }

    void EnsureVisibleCaretsOnAllFields()
    {
        foreach (var field in GetComponentsInChildren<TMP_InputField>(true))
            EnsureVisibleCaret(field);
    }

    static void EnsureVisibleCaret(TMP_InputField field)
    {
        if (field == null)
            return;

        field.customCaretColor = true;
        field.caretColor = new Color(0.12f, 0.13f, 0.15f, 1f);
        field.caretWidth = Mathf.Max(2, field.caretWidth);
        field.caretBlinkRate = 0.85f;
        field.selectionColor = new Color(0.35f, 0.55f, 0.95f, 0.35f);

        // Avoid stacking listeners across Open() calls.
        field.onSelect.RemoveListener(OnFieldSelected);
        field.onSelect.AddListener(OnFieldSelected);
    }

    static void OnFieldSelected(string _)
    {
        // Listener signature matches TMP onSelect; find the focused field and nudge caret.
        var es = EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null)
            return;
        var field = es.currentSelectedGameObject.GetComponent<TMP_InputField>();
        if (field == null)
            return;

        EnsureVisibleCaret(field);
        if (!field.isFocused)
            field.ActivateInputField();
        field.ForceLabelUpdate();
    }
}
