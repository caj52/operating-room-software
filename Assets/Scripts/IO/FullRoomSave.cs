using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Legacy room-export panel retained for scene references.
/// Export destination is fixed under AppData LocalLow (see ExportPaths).
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
    /// Completes immediately with the fixed AppData export root (no OS dialog).
    /// Kept for any leftover callers; prefer <see cref="ExportPaths.PromptForExportFolderThen"/>.
    /// </summary>
    public static void OpenChooseExportFolderPrompt(Action<bool> onComplete = null)
    {
        try
        {
            ExportPaths.EnsureDirectories();
            onComplete?.Invoke(true);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to prepare export folder: {e}");
            onComplete?.Invoke(false);
        }
    }

    /// <summary>Legacy alias — room export root (parent + room name).</summary>
    public static string GetRoomPath() => ExportPaths.GetExportBasePath();

    public static bool HasCustomFolder() => ExportPaths.HasCustomParentFolder();

    public static void ClearCustomFolder() => ExportPaths.ClearCustomParentFolder();
}
