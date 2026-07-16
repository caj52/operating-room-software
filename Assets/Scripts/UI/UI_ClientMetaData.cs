using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UI_ClientMetaData : MonoBehaviour
{
    private const string PrefsSalesRepName = "SalesProposal.SalesRepName";
    private const string PrefsSalesRepEmail = "SalesProposal.SalesRepEmail";

    private static UI_ClientMetaData Instance { get; set; }

    public static UnityEvent OnClosed { get; set; } = new();

    /// <summary>True when the last close persisted different values than when Open ran.</summary>
    public static bool LastCloseHadChanges { get; private set; }

    private string _fingerprintOnOpen = "";

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

    [SerializeField] private TMP_InputField InputFieldSalesRepName;
    [SerializeField] private TMP_InputField InputFieldSalesRepEmail;

    /// <summary>
    /// Sales rep is the one Carlyn-marked manual field — stored per machine, editable here.
    /// </summary>
    public static string SalesRepName
    {
        get
        {
            // Prefer the live field when it exists (including empty = intentionally cleared).
            if (Instance?.InputFieldSalesRepName != null)
                return ReadField(Instance.InputFieldSalesRepName);
            return PlayerPrefs.GetString(PrefsSalesRepName, "");
        }
        set
        {
            string v = value?.Trim() ?? "";
            PlayerPrefs.SetString(PrefsSalesRepName, v);
            PlayerPrefs.Save();
            if (Instance?.InputFieldSalesRepName != null)
                Instance.InputFieldSalesRepName.text = v;
        }
    }

    public static string SalesRepEmail
    {
        get
        {
            if (Instance?.InputFieldSalesRepEmail != null)
                return ReadField(Instance.InputFieldSalesRepEmail);
            return PlayerPrefs.GetString(PrefsSalesRepEmail, "");
        }
        set
        {
            string v = value?.Trim() ?? "";
            PlayerPrefs.SetString(PrefsSalesRepEmail, v);
            PlayerPrefs.Save();
            if (Instance?.InputFieldSalesRepEmail != null)
                Instance.InputFieldSalesRepEmail.text = v;
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

    private bool _userRequestedOpen;

    private void Awake()
    {
        // Survive scene changes once — destroy duplicate scene copies so the
        // Client Data panel does not reappear on every launch/reload.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureSalesRepFields();
        LoadSalesRepIntoFields();
        _userRequestedOpen = false;
        gameObject.SetActive(false);
    }

    private void Start()
    {
        // Prefab may start active; force closed unless Open() was requested.
        if (!_userRequestedOpen && gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (!_userRequestedOpen)
        {
            // Prevent auto-popup when a scene reactivates a DDOL/prefab instance.
            gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        // Ignore disable during boot / duplicate cull — only treat real user closes.
        if (!_userRequestedOpen && string.IsNullOrEmpty(_fingerprintOnOpen))
            return;

        PersistSalesRepFromFields();
        string after = BuildFingerprint();
        LastCloseHadChanges = !string.Equals(after, _fingerprintOnOpen, StringComparison.Ordinal);
        OnClosed?.Invoke();
    }

    public static bool IsOpen => Instance != null && Instance.gameObject.activeSelf;

    public static void Open()
    {
        if (Instance == null)
        {
            Debug.LogWarning("UI_ClientMetaData is not in the scene.");
            return;
        }

        Instance.EnsureSalesRepFields();
        Instance.LoadSalesRepIntoFields();
        Instance.EnsureVisibleCaretsOnAllFields();
        Instance._fingerprintOnOpen = Instance.BuildFingerprint();
        LastCloseHadChanges = false;
        Instance._userRequestedOpen = true;
        Instance.gameObject.SetActive(true);
    }

    public static void Close()
    {
        if (Instance == null || !Instance.gameObject.activeSelf)
            return;
        // Keep _userRequestedOpen true through OnDisable so persist/OnClosed still run,
        // then clear it after disable.
        Instance.gameObject.SetActive(false);
        Instance._userRequestedOpen = false;
    }

    private void LoadSalesRepIntoFields()
    {
        if (InputFieldSalesRepName != null)
            InputFieldSalesRepName.text = PlayerPrefs.GetString(PrefsSalesRepName, "");

        if (InputFieldSalesRepEmail != null)
            InputFieldSalesRepEmail.text = PlayerPrefs.GetString(PrefsSalesRepEmail, "");
    }

    private void PersistSalesRepFromFields()
    {
        if (InputFieldSalesRepName != null)
            PlayerPrefs.SetString(PrefsSalesRepName, InputFieldSalesRepName.text?.Trim() ?? "");
        if (InputFieldSalesRepEmail != null)
            PlayerPrefs.SetString(PrefsSalesRepEmail, InputFieldSalesRepEmail.text?.Trim() ?? "");
        PlayerPrefs.Save();
    }

    string BuildFingerprint()
    {
        return string.Join("\u001f",
            ReadField(InputFieldAccountName),
            ReadField(InputFieldAccountAddressLine1),
            ReadField(InputFieldAccountAddressLine2),
            ReadField(InputFieldProjectName),
            ReadField(InputFieldProjectNumber),
            ReadField(InputFieldOrderReferenceNumber),
            ReadField(InputFieldSalesRepName),
            ReadField(InputFieldSalesRepEmail));
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

    /// <summary>
    /// Adds sales-rep inputs under Client Metadata when the prefab doesn't have them yet.
    /// </summary>
    private void EnsureSalesRepFields()
    {
        if (InputFieldSalesRepName != null && InputFieldSalesRepEmail != null)
            return;

        var template = InputFieldProjectName ?? InputFieldAccountName;
        if (template == null)
            return;

        Transform parent = template.transform.parent;
        if (parent == null)
            return;

        Transform cancel = parent.Find("Button_Cancel");

        if (InputFieldSalesRepName == null)
        {
            InputFieldSalesRepName = CloneInputField(
                template, parent, cancel, "InputField_SalesRepName", "Sales Rep Name");
        }

        if (InputFieldSalesRepEmail == null)
        {
            InputFieldSalesRepEmail = CloneInputField(
                template, parent, cancel, "InputField_SalesRepEmail", "Sales Rep Email");
        }
    }

    private static TMP_InputField CloneInputField(
        TMP_InputField template,
        Transform parent,
        Transform insertBefore,
        string objectName,
        string placeholder)
    {
        // Avoid duplicating if a previous session already created these.
        var existing = parent.Find(objectName);
        if (existing != null)
        {
            var existingField = existing.GetComponent<TMP_InputField>();
            if (existingField != null)
                return existingField;
        }

        var go = Instantiate(template.gameObject, parent);
        go.name = objectName;
        go.SetActive(true);

        var field = go.GetComponent<TMP_InputField>();
        if (field != null)
        {
            field.text = "";
            if (field.placeholder is TMP_Text ph)
                ph.text = placeholder;
            EnsureVisibleCaret(field);
        }

        // Keep cloned rows the same compact height as the template, even if
        // the parent layout doesn't drive child heights.
        var srcRt = template.transform as RectTransform;
        var dstRt = go.transform as RectTransform;
        if (srcRt != null && dstRt != null)
        {
            dstRt.sizeDelta = srcRt.sizeDelta;
            float h = Mathf.Max(28f, srcRt.rect.height > 1f ? srcRt.rect.height : srcRt.sizeDelta.y);
            var le = go.GetComponent<UnityEngine.UI.LayoutElement>()
                     ?? go.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredHeight = h;
            le.minHeight = h;
            le.flexibleHeight = 0f;
        }

        if (insertBefore != null)
            go.transform.SetSiblingIndex(insertBefore.GetSiblingIndex());
        else
            go.transform.SetAsLastSibling();

        return field;
    }
}
