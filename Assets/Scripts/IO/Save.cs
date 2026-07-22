using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using TriLibCore.SFB;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toolbar save: the main Save button always saves the room (OS dialog → AppData Saved).
/// A separate Save Configuration button appears when a configurable object is selected;
/// configs are named in-app and always written to AppData Saved/Configs (where ObjectMenu loads them).
/// </summary>
public class Save : MonoBehaviour
{
    private static Save Instance { get; set; }

    [Header("Config name panel (path is fixed — name only)")]
    public GameObject savePanel;
    public Button b_Save;
    public Button b_Confirm;
    public Button[] b_Cancel;
    public TMP_InputField fileName;
    public TMP_Text header;

    // Main.unity camflyhideui — Save column only (x≈-95):
    //   collapsed: Save (-95,-30) → Settings (-95,-95) → Quotation (-95,-160)
    //   expanded:  Save → SaveConfig → Settings → Quotation (cascaded down a row)
    // Right column (Settings-row room panel / Quotation-row load) is never moved.
    // Inactive icons (screenshot / articulation) must not participate in the cascade.
    private const float SaveColumnXSlop = 8f;

    static readonly HashSet<string> SaveColumnIgnoreNames = new(StringComparer.Ordinal)
    {
        "Button_SaveConfiguration",
        "Button_OpenArticulation",
        "Button_Screenshot",
    };

    private bool _wired;
    private bool _picking;
    private UI_HoverTooltip _saveTooltip;
    private Button _configSaveButton;
    private UI_HoverTooltip _configTooltip;
    private readonly List<(RectTransform rt, Vector2 home)> _saveColumnBelow = new();
    private bool _toolbarExpanded;

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
        if (Instance == null)
            return;
        Instance.CloseConfigNamePanel();
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
        {
            b_Confirm.onClick.RemoveAllListeners();
            b_Confirm.onClick.AddListener(OnConfirmConfigName);
        }
        if (b_Cancel != null)
        {
            foreach (var b in b_Cancel)
            {
                if (b == null)
                    continue;
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(CloseConfigNamePanel);
            }
        }

        Selectable.SelectionChanged += UpdateSaveButtonChrome;
        UpdateSaveButtonChrome();
    }

    private void EnsureConfigSaveButton()
    {
        if (_configSaveButton != null || b_Save == null)
            return;

        CacheSaveColumnBelow();

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
            // Takes Settings' home slot so it lines up with the right-column row.
            rt.anchoredPosition = _saveColumnBelow.Count > 0
                ? _saveColumnBelow[0].home
                : saveRt.anchoredPosition + new Vector2(0f, -65f);
            rt.SetSiblingIndex(saveRt.GetSiblingIndex() + 1);
        }

        _configSaveButton = go.GetComponent<Button>();
        _configSaveButton.onClick.RemoveAllListeners();
        _configSaveButton.onClick.AddListener(BeginSaveConfiguration);

        ApplyConfigButtonIcon(go);

        foreach (var tmp in go.GetComponentsInChildren<TMP_Text>(true))
            tmp.gameObject.SetActive(false);

        _configTooltip = go.GetComponent<UI_HoverTooltip>()
                         ?? go.AddComponent<UI_HoverTooltip>();
        _configTooltip.SetText("Save object configuration");
    }

    /// <summary>
    /// Save-column icons below Save, top→bottom, with their scene home positions.
    /// Scene rows are not evenly spaced (65 then ~59), so we cascade each button
    /// into the next home Y instead of applying one flat offset.
    /// </summary>
    private void CacheSaveColumnBelow()
    {
        _saveColumnBelow.Clear();

        if (b_Save == null || b_Save.transform.parent == null)
            return;

        var saveRt = b_Save.transform as RectTransform;
        if (saveRt == null)
            return;

        float saveX = saveRt.anchoredPosition.x;
        float saveY = saveRt.anchoredPosition.y;
        var parent = b_Save.transform.parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || child == saveRt)
                continue;
            if (!child.gameObject.activeSelf)
                continue;
            if (SaveColumnIgnoreNames.Contains(child.gameObject.name))
                continue;
            if (Mathf.Abs(child.sizeDelta.x - saveRt.sizeDelta.x) > 1f ||
                Mathf.Abs(child.sizeDelta.y - saveRt.sizeDelta.y) > 1f)
                continue;
            if (Mathf.Abs(child.anchoredPosition.x - saveX) > SaveColumnXSlop)
                continue;
            if (child.anchoredPosition.y >= saveY - 0.5f)
                continue;

            _saveColumnBelow.Add((child, child.anchoredPosition));
        }

        _saveColumnBelow.Sort((a, b) => b.home.y.CompareTo(a.home.y));
    }

    private void ApplyConfigButtonIcon(GameObject buttonGo)
    {
        Image icon = null;
        var child = buttonGo.transform.Find("Image");
        if (child != null)
            icon = child.GetComponent<Image>();
        if (icon == null)
        {
            foreach (var img in buttonGo.GetComponentsInChildren<Image>(true))
            {
                if (img.gameObject != buttonGo)
                {
                    icon = img;
                    break;
                }
            }
        }
        if (icon == null)
            return;

        Sprite sprite = Resources.Load<Sprite>("UI/save_config_icon");
        if (sprite == null)
        {
            var tex = Resources.Load<Texture2D>("UI/save_config_icon");
            if (tex != null)
            {
                sprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                sprite.name = "SaveConfigIcon";
            }
        }

        if (sprite == null)
            return;

        icon.sprite = sprite;
        icon.preserveAspect = true;
        icon.color = Color.white;
        icon.type = Image.Type.Simple;
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

        SetSaveColumnExpanded(showConfig);
    }

    private void SetSaveColumnExpanded(bool expanded)
    {
        if (_saveColumnBelow.Count == 0)
            CacheSaveColumnBelow();

        if (_configSaveButton != null)
        {
            var configRt = _configSaveButton.transform as RectTransform;
            if (configRt != null && _saveColumnBelow.Count > 0)
                configRt.anchoredPosition = _saveColumnBelow[0].home;
        }

        if (expanded == _toolbarExpanded)
            return;

        if (!expanded)
        {
            foreach (var (rt, home) in _saveColumnBelow)
            {
                if (rt != null)
                    rt.anchoredPosition = home;
            }
            _toolbarExpanded = false;
            return;
        }

        // Cascade each button into the next row's home Y so left/right columns
        // stay lined up. Last button extends by the previous row gap.
        // homes: Settings(-95), Quotation(-160)
        // after: Settings→-160, Quotation→-225
        for (int i = 0; i < _saveColumnBelow.Count; i++)
        {
            var (rt, home) = _saveColumnBelow[i];
            if (rt == null)
                continue;

            float newY;
            if (i + 1 < _saveColumnBelow.Count)
                newY = _saveColumnBelow[i + 1].home.y;
            else if (_saveColumnBelow.Count >= 2)
            {
                var prev = _saveColumnBelow[i - 1].home.y;
                float gap = prev - home.y; // positive distance upward neighbor → this
                newY = home.y - gap;
            }
            else
                newY = home.y - 65f;

            rt.anchoredPosition = new Vector2(home.x, newY);
        }

        _toolbarExpanded = true;
    }

    private void BeginSaveRoom()
    {
        string folder = ConfigurationManager.GetSavedRoomsFolder();
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

        _picking = true;
        try
        {
            StandaloneFileBrowser.SaveFilePanelAsync(
                "Save Room",
                folder,
                GetDefaultRoomFileName(),
                new[] { new ExtensionFilter("Room Save", "json") },
                OnRoomSavePathPicked);
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

    /// <summary>
    /// Name-only prompt — configs always land in <see cref="ConfigurationManager.GetSavedConfigsFolder"/>.
    /// </summary>
    private void BeginSaveConfiguration()
    {
        if (!ExportRequest.SelectionIsConfigurable())
        {
            UI_DialogPrompt.Open(
                "Select a boom or configurable object first to save a configuration.",
                new ButtonAction("OK"));
            return;
        }

        if (savePanel == null || fileName == null)
        {
            TrySaveConfiguration(GetDefaultConfigFileName());
            return;
        }

        if (header != null)
        {
            header.text = "Save Configuration";
            header.color = Color.white;
        }

        fileName.text = GetDefaultConfigFileName();

        if (FreeLookCam.Instance != null)
            FreeLookCam.Instance.isLocked = true;

        savePanel.SetActive(true);
    }

    private void OnConfirmConfigName()
    {
        if (fileName == null || string.IsNullOrWhiteSpace(fileName.text))
        {
            if (header != null)
            {
                header.text = "Please enter a name";
                header.color = Color.red;
            }
            return;
        }

        TrySaveConfiguration(fileName.text.Trim());
    }

    private void CloseConfigNamePanel()
    {
        if (savePanel != null)
            savePanel.SetActive(false);
        if (fileName != null)
            fileName.text = "";
        if (FreeLookCam.Instance != null)
            FreeLookCam.Instance.isLocked = false;
    }

    private void TrySaveConfiguration(string rawName)
    {
        if (ConfigurationManager.Instance == null)
        {
            UI_DialogPrompt.Open("Save system is unavailable.", new ButtonAction("OK"));
            return;
        }

        string safe = ConfigurationManager.Instance.ReplaceInvalidChars(rawName.Replace(' ', '_'));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "Configuration";

        string folder = ConfigurationManager.GetSavedConfigsFolder();
        try
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }
        catch (Exception e)
        {
            Debug.LogError($"Could not create configs folder: {e}");
            UI_DialogPrompt.Open(
                "Could not create the configurations folder.",
                new ButtonAction("OK"));
            return;
        }

        string path = Path.Combine(folder, safe.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? safe
            : safe + ".json");

        if (File.Exists(path))
        {
            if (savePanel != null)
                savePanel.SetActive(false);
            PromptOverwriteConfiguration(safe);
            return;
        }

        CompleteSaveConfiguration(safe);
    }

    private void PromptOverwriteConfiguration(string name)
    {
        string nice = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;

        UI_DialogPrompt.Open(
            $"A configuration named “{nice.Replace('_', ' ')}” already exists.",
            new ButtonAction("Overwrite", () =>
            {
                UI_DialogPrompt.Close();
                CompleteSaveConfiguration(name);
            }),
            new ButtonAction("Rename", () =>
            {
                UI_DialogPrompt.Close();
                if (savePanel != null)
                    savePanel.SetActive(true);
                if (FreeLookCam.Instance != null)
                    FreeLookCam.Instance.isLocked = true;
            }));
    }

    private void CompleteSaveConfiguration(string name)
    {
        CloseConfigNamePanel();

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
            ConfigurationManager.Instance.SaveConfiguration(name);
            string nice = Path.GetFileNameWithoutExtension(name).Replace('_', ' ');
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

    private void OnRoomSavePathPicked(ItemWithStream item)
    {
        _picking = false;

        if (item == null || string.IsNullOrWhiteSpace(item.Name))
            return;

        // Dialog is name-only — always write under AppData Saved so the load UI
        // can reopen the file after a cold start.
        string pickedName = Path.GetFileName(item.Name.Trim());
        if (string.IsNullOrWhiteSpace(pickedName))
            return;
        if (!pickedName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            pickedName += ".json";

        string path = Path.Combine(ConfigurationManager.GetSavedRoomsFolder(), pickedName);
        StartCoroutine(SaveRoomToPathCoroutine(path));
    }

    private IEnumerator SaveRoomToPathCoroutine(string path)
    {
        if (ConfigurationManager.Instance == null)
        {
            UI_DialogPrompt.Open("Save system is unavailable.", new ButtonAction("OK"));
            yield break;
        }

        Debug.Log($"[SaveRoom] UI picked path=\"{path}\" loadingActive={Loading.LoadingActive}");
        ConfigurationManager.Instance.SaveRoomToPath(path, showSuccessDialog: false);

        // Success = file on disk. Do not require LoadingActive to clear — leftover or
        // post-write tokens (attachment restore, etc.) used to false-fail a good save.
        float timeout = 20f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (File.Exists(path))
            {
                try
                {
                    if (new FileInfo(path).Length > 0)
                        break;
                }
                catch
                {
                    // File may still be mid-write.
                }
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Best-effort: let the overlay dismiss before the success prompt.
        float settle = 0f;
        while (settle < 2f && Loading.LoadingActive)
        {
            settle += Time.unscaledDeltaTime;
            yield return null;
        }

        bool fileOk = false;
        long bytes = 0;
        try
        {
            if (File.Exists(path))
            {
                bytes = new FileInfo(path).Length;
                fileOk = bytes > 0;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveRoom] Could not stat saved file: {e}");
        }

        Debug.Log(
            $"[SaveRoom] UI wait finished elapsed={elapsed:0.0}s timeout={elapsed >= timeout} " +
            $"fileOk={fileOk} bytes={bytes} loadingActive={Loading.LoadingActive}");

        string nice = Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
        if (fileOk)
        {
            UI_DialogPrompt.Open(
                $"Room “{nice}” saved.",
                new ButtonAction("Done"));
        }
        else
        {
            Debug.LogError(
                $"[SaveRoom] UI save did not finish cleanly — fileOk={fileOk} bytes={bytes} " +
                $"loadingActive={Loading.LoadingActive}");
            UI_DialogPrompt.Open(
                "Room save did not finish.\nCheck the log for [SaveRoom] lines.",
                new ButtonAction("OK"));
        }
    }

}
