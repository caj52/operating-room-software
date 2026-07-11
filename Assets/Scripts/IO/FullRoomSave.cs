using System;
using System.Collections.Generic;
using TMPro;
using TriLibCore.SFB;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Legacy room-export panel retained for scene references.
/// "Choose export folder…" now opens the OS folder picker and stores the
/// parent path in PlayerPrefs via ExportPaths.
/// </summary>
public class FullRoomSave : MonoBehaviour
{
    private static FullRoomSave Instance { get; set; }

    [Header("Constant UI")]
    public GameObject savePanel;
    public Button b_Save;
    public Button b_Confirm;
    public Button[] b_Cancel;
    public TMP_InputField fileName;
    public string RoomName;
    [Header("Dynamic UI")]
    public TMP_Text header;

    private void Awake()
    {
        Instance = this;

        if (savePanel != null)
            savePanel.SetActive(false);
    }

    public static void Close()
    {
        if (Instance != null && Instance.savePanel != null)
            Instance.savePanel.SetActive(false);
    }

    void Start()
    {
        // Old type-a-name panel is unused — keep it hidden.
        if (savePanel != null)
            savePanel.SetActive(false);
    }

    /// <summary>Opens the native OS folder browser and saves the chosen parent folder.</summary>
    /// <param name="onComplete">Invoked when the picker closes. True if a folder was chosen.</param>
    public static void OpenChooseExportFolderPrompt(Action<bool> onComplete = null)
    {
        string startDir = ExportPaths.GetParentFolder();
        try
        {
            if (!System.IO.Directory.Exists(startDir))
                System.IO.Directory.CreateDirectory(ExportPaths.GetDefaultParentFolder());
            if (!System.IO.Directory.Exists(startDir))
                startDir = ExportPaths.GetDefaultParentFolder();
        }
        catch (Exception)
        {
            startDir = ExportPaths.GetDefaultParentFolder();
        }

        try
        {
            StandaloneFileBrowser.OpenFolderPanelAsync(
                "Choose export folder",
                startDir,
                false,
                items => OnFolderPicked(items, onComplete));
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to open folder picker: {e}");
            UI_DialogPrompt.Open(
                "Could not open the folder picker.\nExports will keep using the current folder.",
                new ButtonAction("OK", () =>
                {
                    UI_DialogPrompt.Close();
                    onComplete?.Invoke(false);
                }));
        }
    }

    private static void OnFolderPicked(IList<ItemWithStream> items, Action<bool> onComplete)
    {
        if (items == null || items.Count == 0 || string.IsNullOrWhiteSpace(items[0]?.Name))
        {
            onComplete?.Invoke(false);
            return;
        }

        ExportPaths.SetParentFolder(items[0].Name);
        onComplete?.Invoke(true);
    }

    /// <summary>Legacy alias — room export root (parent + room name).</summary>
    public static string GetRoomPath() => ExportPaths.GetExportBasePath();

    public static bool HasCustomFolder() => ExportPaths.HasCustomParentFolder();

    public static void ClearCustomFolder() => ExportPaths.ClearCustomParentFolder();
}
