using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TriLibCore.SFB;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Load room via the OS open dialog (same path-picking UX as Save).
/// The in-scene LoadRoom panel is only a trigger surface — activating it opens the dialog.
/// </summary>
public class RoomConfigLoader : MonoBehaviour
{
    public static RoomConfigLoader Instance;

    [field: SerializeField] public Transform contentView { get; private set; }
    [field: SerializeField] public ScrollRect scroll { get; private set; }
    [field: SerializeField] public GameObject filePrefab { get; private set; }

    private bool _picking;
    private bool _openingDialog;

    void Awake()
    {
        Instance = this;

        string savedFolder = ConfigurationManager.GetSavedRoomsFolder();
        try
        {
            if (!Directory.Exists(savedFolder))
                Directory.CreateDirectory(savedFolder);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not ensure saves folder: {e.Message}");
        }

        gameObject.SetActive(false);
    }

    void OnEnable()
    {
        // Load button SetActive(true)s this panel — open the OS picker instead of a fixed list.
        if (!_openingDialog)
            StartCoroutine(OpenLoadDialogNextFrame());
    }

    /// <summary>Opens the OS load dialog (mirrors <see cref="Save.OpenSaveRoomPrompt"/>).</summary>
    public static void OpenLoadRoomPrompt()
    {
        if (Instance == null)
        {
            UI_DialogPrompt.Open(
                "Load UI is missing from the scene.",
                new ButtonAction("OK"));
            return;
        }

        Instance.BeginLoadRoom();
    }

    private IEnumerator OpenLoadDialogNextFrame()
    {
        _openingDialog = true;
        // Avoid SetActive(false) during OnEnable when the sync Windows dialog returns immediately.
        yield return null;
        BeginLoadRoom();
        _openingDialog = false;
    }

    private void BeginLoadRoom()
    {
        if (_picking)
            return;

        string folder = ConfigurationManager.GetSavedRoomsFolder();
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
            ClosePanelOnly();
            return;
        }

        _picking = true;
        try
        {
            StandaloneFileBrowser.OpenFilePanelAsync(
                "Load Room",
                folder,
                new[] { new ExtensionFilter("Room Save", "json") },
                false,
                OnRoomLoadPathPicked);
        }
        catch (Exception e)
        {
            _picking = false;
            Debug.LogError($"Failed to open load dialog: {e}");
            UI_DialogPrompt.Open(
                "Could not open the system open dialog.",
                new ButtonAction("OK"));
            ClosePanelOnly();
        }
    }

    private void OnRoomLoadPathPicked(IList<ItemWithStream> items)
    {
        _picking = false;

        if (items == null || items.Count == 0 ||
            items[0] == null || string.IsNullOrWhiteSpace(items[0].Name))
        {
            ClosePanelOnly();
            return;
        }

        string path = items[0].Name.Trim();
        if (!File.Exists(path))
        {
            ClosePanelOnly();
            UI_DialogPrompt.Open(
                "That room file could not be found.",
                new ButtonAction("OK"));
            return;
        }

        if (ConfigurationManager.Instance == null)
        {
            ClosePanelOnly();
            UI_DialogPrompt.Open("Load system is unavailable.", new ButtonAction("OK"));
            return;
        }

        ConfigurationManager.Instance.LoadRoom(path);
        ClosePanelAndMenu();
    }

    private void ClosePanelOnly()
    {
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    private void ClosePanelAndMenu()
    {
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
        if (transform.root != null && transform.root.gameObject.activeSelf)
            transform.root.gameObject.SetActive(false);
    }

    /// <summary>Legacy no-op — load uses the OS dialog, not an in-app list.</summary>
    public void GenerateRoomItem(string f) { }

    /// <summary>Legacy no-op — load uses the OS dialog, not an in-app list.</summary>
    public void RefreshOrAddRoomItem(string f) { }
}
