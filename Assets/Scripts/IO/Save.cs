using System;
using System.Collections;
using System.IO;
using TMPro;
using TriLibCore.SFB;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Save button: whole room when nothing is selected, selected boom/assembly
/// configuration when something is selected. Both use the OS save dialog.
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
            b_Save.onClick.AddListener(OnToolbarSaveClicked);
            _saveTooltip = b_Save.GetComponent<UI_HoverTooltip>()
                           ?? b_Save.gameObject.AddComponent<UI_HoverTooltip>();
        }

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

    private void UpdateSaveButtonChrome()
    {
        bool hasSelection = ExportRequest.HasSelection();
        string tip = hasSelection ? "Save configuration" : "Save room";

        if (_saveTooltip != null)
            _saveTooltip.SetText(tip);

        if (b_Save == null)
            return;

        var label = b_Save.GetComponentInChildren<TMP_Text>(true);
        if (label != null && !string.IsNullOrWhiteSpace(label.text))
            label.text = hasSelection ? "Save configuration…" : "Save room…";
    }

    private void OnToolbarSaveClicked()
    {
        if (ExportRequest.HasSelection())
            BeginSaveConfiguration();
        else
            BeginSaveRoom();
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
        if (!ExportRequest.HasSelection())
        {
            BeginSaveRoom();
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

        if (!ExportRequest.HasSelection())
        {
            UI_DialogPrompt.Open(
                "Select a boom or object first to save a configuration.",
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
