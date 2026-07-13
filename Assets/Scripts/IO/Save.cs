using System;
using System.Collections;
using System.IO;
using TMPro;
using TriLibCore.SFB;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toolbar save: the main Save button always saves the room.
/// A separate Save Configuration button appears under it when a configurable
/// object (boom / scalable assembly) is selected.
/// </summary>
public class Save : MonoBehaviour
{
    private static Save Instance { get; set; }

    [Header("Legacy UI (unused — kept for scene references)")]
    public GameObject savePanel;
    public Button b_Save;
    public Button b_Confirm;
    public Button[] b_Cancel;
    public TMP_InputField fileName;
    public TMP_Text header;

    private bool _wired;
    private bool _picking;
    private bool _savingConfig;
    private UI_HoverTooltip _saveTooltip;
    private Button _configSaveButton;
    private UI_HoverTooltip _configTooltip;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        WireUi();
        if (savePanel != null)
            savePanel.SetActive(false);
    }

    private void OnDestroy()
    {
        Selectable.SelectionChanged -= UpdateSaveButtonChrome;
    }

    public static void Close()
    {
        if (Instance != null && Instance.savePanel != null)
            Instance.savePanel.SetActive(false);
    }

    /// <summary>Opens the OS save dialog so the room gets a save name (e.g. before export).</summary>
    public static void OpenSaveRoomPrompt()
    {
        if (Instance == null)
        {
            UI_DialogPrompt.Open(
                "Save UI is missing from the scene.\nSave the room from the toolbar, then export again.",
                new ButtonAction("OK"));
            return;
        }

        Instance.BeginSaveRoom();
    }

    private void WireUi()
    {
        if (_wired)
            return;
        _wired = true;

        if (b_Save != null)
        {
            b_Save.onClick.RemoveAllListeners();
            b_Save.onClick.AddListener(BeginSaveRoom);
            _saveTooltip = b_Save.GetComponent<UI_HoverTooltip>()
                           ?? b_Save.gameObject.AddComponent<UI_HoverTooltip>();
            _saveTooltip.SetText("Save room");
        }

        EnsureConfigSaveButton();

        if (b_Confirm != null)
            b_Confirm.onClick.RemoveAllListeners();
        if (b_Cancel != null)
        {
            foreach (var b in b_Cancel)
            {
                if (b != null)
                    b.onClick.RemoveAllListeners();
            }
        }

        Selectable.SelectionChanged += UpdateSaveButtonChrome;
        UpdateSaveButtonChrome();
    }

    private void EnsureConfigSaveButton()
    {
        if (_configSaveButton != null || b_Save == null)
            return;

        var go = Instantiate(b_Save.gameObject, b_Save.transform.parent);
        go.name = "Button_SaveConfiguration";
        go.SetActive(false);

        var saveRt = b_Save.transform as RectTransform;
        var rt = go.transform as RectTransform;
        if (saveRt != null && rt != null)
        {
            rt.anchorMin = saveRt.anchorMin;
            rt.anchorMax = saveRt.anchorMax;
            rt.pivot = saveRt.pivot;
            rt.sizeDelta = saveRt.sizeDelta;
            // Directly under the room Save button.
            rt.anchoredPosition = saveRt.anchoredPosition + new Vector2(0f, -(saveRt.sizeDelta.y + 6f));
            rt.SetSiblingIndex(saveRt.GetSiblingIndex() + 1);
        }

        _configSaveButton = go.GetComponent<Button>();
        _configSaveButton.onClick.RemoveAllListeners();
        _configSaveButton.onClick.AddListener(BeginSaveConfiguration);

        ApplyConfigButtonIcon(go);

        // Hide any text label so it stays icon-only like Save.
        foreach (var tmp in go.GetComponentsInChildren<TMP_Text>(true))
            tmp.gameObject.SetActive(false);

        _configTooltip = go.GetComponent<UI_HoverTooltip>()
                         ?? go.AddComponent<UI_HoverTooltip>();
        _configTooltip.SetText("Save object configuration");
    }

    private static void ApplyConfigButtonIcon(GameObject buttonGo)
    {
        var img = buttonGo.GetComponent<Image>();
        if (img == null)
            return;

        var tex = Resources.Load<Texture2D>("UI/save_config_icon");
        if (tex == null)
        {
            // Fallback: keep the save icon and stamp a small object badge.
            var badge = new GameObject("ObjectBadge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(buttonGo.transform, false);
            var brt = badge.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(1f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(1f, 0f);
            brt.sizeDelta = new Vector2(18f, 18f);
            brt.anchoredPosition = new Vector2(-2f, 2f);
            badge.GetComponent<Image>().color = new Color(0.91f, 0.47f, 0.13f, 1f);
            return;
        }

        var sprite = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);
        sprite.name = "SaveConfigIcon";
        img.sprite = sprite;
        img.preserveAspect = true;
    }

    private void UpdateSaveButtonChrome()
    {
        if (_saveTooltip != null)
            _saveTooltip.SetText("Save room");

        if (b_Save != null)
        {
            var label = b_Save.GetComponentInChildren<TMP_Text>(true);
            if (label != null && label.gameObject.activeSelf && !string.IsNullOrWhiteSpace(label.text))
                label.text = "Save room…";
        }

        bool showConfig = ExportRequest.SelectionIsConfigurable();
        if (_configSaveButton != null)
            _configSaveButton.gameObject.SetActive(showConfig);
    }

    private void BeginSaveRoom()
    {
        BeginOsSave(
            title: "Save Room",
            folder: ConfigurationManager.GetSavedRoomsFolder(),
            defaultName: GetDefaultRoomFileName(),
            savingConfig: false);
    }

    private void BeginSaveConfiguration()
    {
        if (!ExportRequest.SelectionIsConfigurable())
        {
            UI_DialogPrompt.Open(
                "Select a boom or configurable object first to save a configuration.",
                new ButtonAction("OK"));
            return;
        }

        BeginOsSave(
            title: "Save Configuration",
            folder: ConfigurationManager.GetSavedConfigsFolder(),
            defaultName: GetDefaultConfigFileName(),
            savingConfig: true);
    }

    private void BeginOsSave(string title, string folder, string defaultName, bool savingConfig)
    {
        if (_picking)
            return;

        try
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }
        catch (Exception e)
        {
            Debug.LogError($"Could not create saves folder: {e}");
            UI_DialogPrompt.Open(
                "Could not create the saves folder.",
                new ButtonAction("OK"));
            return;
        }

        _savingConfig = savingConfig;
        _picking = true;
        try
        {
            StandaloneFileBrowser.SaveFilePanelAsync(
                title,
                folder,
                defaultName,
                new[] { new ExtensionFilter(savingConfig ? "Configuration" : "Room Save", "json") },
                OnSavePathPicked);
        }
        catch (Exception e)
        {
            _picking = false;
            Debug.LogError($"Failed to open save dialog: {e}");
            UI_DialogPrompt.Open(
                "Could not open the system save dialog.",
                new ButtonAction("OK"));
        }
    }

    private static string GetDefaultRoomFileName()
    {
        if (!ExportPaths.HasSavedRoomName())
            return "Untitled_Room";

        string name = ConfigurationManager.GetCurrentRoomSaveName().Replace(' ', '_');
        if (ConfigurationManager.Instance != null)
            name = ConfigurationManager.Instance.ReplaceInvalidChars(name);
        return name;
    }

    private static string GetDefaultConfigFileName()
    {
        if (!ExportRequest.HasSelection())
            return "Configuration";

        return ExportPaths.GetSelectableExportName(
            Selectable.SelectedSelectables[0], "Configuration");
    }

    private void OnSavePathPicked(ItemWithStream item)
    {
        _picking = false;

        if (item == null || string.IsNullOrWhiteSpace(item.Name))
            return;

        string path = item.Name.Trim();
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            path += ".json";

        if (_savingConfig)
            SaveConfigurationToPath(path);
        else
            StartCoroutine(SaveRoomToPathCoroutine(path));
    }

    private IEnumerator SaveRoomToPathCoroutine(string path)
    {
        if (ConfigurationManager.Instance == null)
        {
            UI_DialogPrompt.Open("Save system is unavailable.", new ButtonAction("OK"));
            yield break;
        }

        ConfigurationManager.Instance.SaveRoomToPath(path, showSuccessDialog: false);

        float timeout = 12f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (File.Exists(path) && !Loading.LoadingActive)
                break;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        string nice = Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
        UI_DialogPrompt.Open(
            $"Room “{nice}” saved.",
            new ButtonAction("Done"));
    }

    private void SaveConfigurationToPath(string path)
    {
        if (ConfigurationManager.Instance == null)
        {
            UI_DialogPrompt.Open("Save system is unavailable.", new ButtonAction("OK"));
            return;
        }

        if (!ExportRequest.SelectionIsConfigurable())
        {
            UI_DialogPrompt.Open(
                "Select a boom or configurable object first to save a configuration.",
                new ButtonAction("OK"));
            return;
        }

        try
        {
            ConfigurationManager.Instance.SaveConfigurationToPath(path);
            string nice = Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
            UI_DialogPrompt.Open(
                $"Configuration “{nice}” saved.\nIt will appear in the object menu.",
                new ButtonAction("Done"));
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save configuration: {e}");
            UI_DialogPrompt.Open(
                "Could not save the configuration.\nCheck the console for details.",
                new ButtonAction("OK"));
        }
    }
}
