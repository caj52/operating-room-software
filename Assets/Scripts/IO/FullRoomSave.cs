using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Optional custom export folder picker ("Choose export folder…").
/// Default exports use Documents/Operating Room Exports without this step.
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

    private Action _onFolderChosen;

    private void Awake()
    {
        Instance = this;

        if (savePanel != null)
        {
            if (savePanel.GetComponent<FullScreenMenu>() == null)
                savePanel.AddComponent<FullScreenMenu>();

            // Stronger dim so world selection cannot pass through.
            var dim = savePanel.GetComponent<Image>();
            if (dim != null)
            {
                dim.raycastTarget = true;
                var c = dim.color;
                if (c.a < 0.35f)
                {
                    c.a = 0.61f;
                    dim.color = c;
                }
            }

            int uiLayer = LayerMask.NameToLayer("UI");
            foreach (var t in savePanel.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = uiLayer;
        }
    }

    public static void Close()
    {
        if (Instance != null && Instance.savePanel != null)
            Instance.savePanel.SetActive(false);
    }

    void Start()
    {
        // b_Save is the toolbar Export button — wired to UI_ButtonExport, not this folder picker.

        b_Confirm.onClick.AddListener(() =>
        {
            if (string.IsNullOrEmpty(fileName.text))
                return;

            string folderName = ExportPaths.SanitizeFolderName(fileName.text);
            RoomName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Operating Room Exports",
                folderName);

            if (Directory.Exists(RoomName))
            {
                UI_DialogPrompt.Open(
                    "That folder already exists. Use it for exports?",
                    new ButtonAction("Yes", () =>
                    {
                        UI_DialogPrompt.Close();
                        ConfirmFolder();
                    }),
                    new ButtonAction("Cancel", () =>
                    {
                        UI_DialogPrompt.Close();
                        header.text = "Name your export folder";
                        header.color = Color.red;
                        fileName.text = "";
                        FreeLookCam.Instance.isLocked = true;
                    }));
            }
            else
            {
                ConfirmFolder();
            }
        });

        WireCancelButtons();
        savePanel.SetActive(false);
    }

    private void WireCancelButtons()
    {
        if (b_Cancel == null)
            return;

        foreach (var cancel in b_Cancel)
        {
            if (cancel == null || cancel == b_Confirm)
                continue;

            cancel.onClick.AddListener(() =>
            {
                savePanel.SetActive(false);
                FreeLookCam.Instance.isLocked = false;
                _onFolderChosen = null;
            });
        }

        // Fallback: root dim click / any child named Cancel that wasn't wired.
        var namedCancel = savePanel.GetComponentsInChildren<Button>(true);
        foreach (var button in namedCancel)
        {
            if (button == b_Confirm)
                continue;
            if (!button.gameObject.name.Contains("Cancel", StringComparison.OrdinalIgnoreCase))
                continue;
            if (b_Cancel != null && Array.IndexOf(b_Cancel, button) >= 0)
                continue;

            button.onClick.AddListener(() =>
            {
                savePanel.SetActive(false);
                FreeLookCam.Instance.isLocked = false;
                _onFolderChosen = null;
            });
        }
    }

    public static void OpenChooseExportFolderPrompt(Action onFolderChosen = null)
    {
        if (Instance == null)
            return;

        Instance._onFolderChosen = onFolderChosen;
        Instance.header.text = "Name your export folder";
        Instance.header.color = Color.black;
        Instance.fileName.text = ExportPaths.SanitizeFolderName(ExportPaths.GetRoomExportName());
        FreeLookCam.Instance.isLocked = true;
        Instance.savePanel.SetActive(true);
    }

    public void ConfirmFolder()
    {
        Directory.CreateDirectory(RoomName);
        fileName.text = "";
        header.color = Color.black;
        FreeLookCam.Instance.isLocked = false;
        savePanel.SetActive(false);

        var callback = _onFolderChosen;
        _onFolderChosen = null;

        UI_DialogPrompt.Open(
            $"Export folder set.\nFiles will save to:\n{RoomName}",
            new ButtonAction("Open Folder", () => ExportFolderUtility.RevealInFileManager(RoomName)),
            new ButtonAction("Done", () =>
            {
                UI_DialogPrompt.Close();
                callback?.Invoke();
            }));
    }

    public static string GetRoomPath()
    {
        if (Instance == null || string.IsNullOrEmpty(Instance.RoomName))
            return null;
        return Instance.RoomName;
    }

    public static bool HasCustomFolder()
        => !string.IsNullOrEmpty(GetRoomPath());

    public static void ClearCustomFolder()
    {
        if (Instance != null)
            Instance.RoomName = null;
    }
}
