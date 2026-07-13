using System;
using TMPro;
using TriLibCore.SFB;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Legacy room-export panel retained for scene references.
/// Export destination is chosen via a save dialog prefilled with the suggested
/// path + room folder name (ExportPaths.PromptForExportFolderThen).
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

    /// <summary>
    /// Opens a native save dialog prefilled with
    /// Documents/Operating Room Exports / {RoomName} so the user sees the suggested
    /// output path and folder name (standard Save As behavior).
    /// </summary>
    /// <param name="onComplete">Invoked when the dialog closes. True if a location was chosen.</param>
    public static void OpenChooseExportFolderPrompt(Action<bool> onComplete = null)
    {
        string startDir = ExportPaths.GetSuggestedExportParentFolder();
        string defaultName = ExportPaths.GetSuggestedExportFolderName();

        try
        {
            // Empty extension → name field is prefilled without forcing a file type.
            StandaloneFileBrowser.SaveFilePanelAsync(
                "Choose export location",
                startDir,
                defaultName,
                "",
                item => OnExportLocationPicked(item, onComplete));
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to open export location dialog: {e}");
            UI_DialogPrompt.Open(
                "Could not open the export location dialog.\nExports will keep using the current folder.",
                new ButtonAction("OK", () =>
                {
                    UI_DialogPrompt.Close();
                    onComplete?.Invoke(false);
                }));
        }
    }

    private static void OnExportLocationPicked(ItemWithStream item, Action<bool> onComplete)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Name))
        {
            onComplete?.Invoke(false);
            return;
        }

        ExportPaths.ApplyPickedExportLocation(item.Name);
        onComplete?.Invoke(true);
    }

    /// <summary>Legacy alias — room export root (parent + room name).</summary>
    public static string GetRoomPath() => ExportPaths.GetExportBasePath();

    public static bool HasCustomFolder() => ExportPaths.HasCustomParentFolder();

    public static void ClearCustomFolder() => ExportPaths.ClearCustomParentFolder();
}
