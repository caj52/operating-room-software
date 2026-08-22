using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-game list of room JSON files under AppData LocalLow/.../Saved.
/// Load from this panel — no File Explorer required.
/// </summary>
public class RoomConfigLoader : MonoBehaviour
{
    public static RoomConfigLoader Instance;

    [field: SerializeField] public Transform contentView { get; private set; }
    [field: SerializeField] public ScrollRect scroll { get; private set; }
    [field: SerializeField] public GameObject filePrefab { get; private set; }

    void Awake()
    {
        Instance = this;
        EnsureSavedFolder();
        // Don't rebuild here — object is usually deactivated after Awake; OnEnable
        // (or OpenLoadRoomPrompt) rebuilds when the user actually opens Load.
        gameObject.SetActive(false);
    }

    void OnEnable()
    {
        RebuildRoomList();
    }

    /// <summary>Shows the in-game saved-rooms list.</summary>
    public static void OpenLoadRoomPrompt()
    {
        if (Instance == null)
        {
            UI_DialogPrompt.Open(
                "Load UI is missing from the scene.",
                new ButtonAction("OK"));
            return;
        }

        Instance.EnsureSavedFolder();
        // SetActive(true) → OnEnable → RebuildRoomList. Do not rebuild twice.
        if (Instance.gameObject.activeSelf)
            Instance.RebuildRoomList();
        else
            Instance.gameObject.SetActive(true);
    }

    public void GenerateRoomItem(string f) => RefreshOrAddRoomItem(f);

    /// <summary>Adds a load-list entry, or no-ops if that room name is already listed.</summary>
    public void RefreshOrAddRoomItem(string f)
    {
        if (contentView == null || filePrefab == null || string.IsNullOrWhiteSpace(f))
            return;

        if (!TryResolveSavedRoomPath(f, out string full, out string display))
            return;

        foreach (Transform child in contentView)
        {
            if (child == null)
                continue;
            var label = child.GetComponentInChildren<TMP_Text>(true);
            if (label != null && label.text == display)
                return;
        }

        AddRoomListEntry(full, display);
    }

    static bool TryResolveSavedRoomPath(string f, out string full, out string display)
    {
        full = null;
        display = null;

        string savedRoot = Path.GetFullPath(ConfigurationManager.GetSavedRoomsFolder())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        try
        {
            full = Path.GetFullPath(f);
        }
        catch
        {
            return false;
        }

        string parent = Path.GetDirectoryName(full) ?? "";
        try
        {
            parent = Path.GetFullPath(parent)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        if (!string.Equals(parent, savedRoot, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!full.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!File.Exists(full))
            return false;

        display = Path.GetFileNameWithoutExtension(full).Replace("_", " ");
        return true;
    }

    void AddRoomListEntry(string fullPath, string display)
    {
        GameObject go = Instantiate(filePrefab, contentView);
        go.SetActive(true);
        go.transform.localScale = Vector3.one;
        go.transform.localRotation = Quaternion.identity;

        var tmp = go.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
            tmp.text = display;

        var button = go.GetComponent<Button>();
        if (button == null)
            button = go.GetComponentInChildren<Button>(true);
        if (button == null)
        {
            Debug.LogWarning("[RoomConfigLoader] filePrefab has no Button — cannot load from list.");
            return;
        }

        string pathToLoad = fullPath;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => LoadRoomAndClose(pathToLoad));
    }

    void LoadRoomAndClose(string pathToLoad)
    {
        if (ConfigurationManager.Instance == null)
        {
            UI_DialogPrompt.Open("Load system is unavailable.", new ButtonAction("OK"));
            return;
        }

        ConfigurationManager.Instance.LoadRoom(pathToLoad);
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
        if (transform.root != null && transform.root.gameObject.activeSelf
            && transform.root != transform)
            transform.root.gameObject.SetActive(false);
    }

    void EnsureSavedFolder()
    {
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
    }

    void RebuildRoomList()
    {
        if (contentView == null || filePrefab == null)
        {
            Debug.LogWarning("[RoomConfigLoader] contentView or filePrefab is not assigned.");
            return;
        }

        // Destroy() is end-of-frame — if we add rows in the same call, the duplicate
        // check still sees doomed children and skips adds → blank/partial list.
        ClearContentImmediate();

        string savedFolder = ConfigurationManager.GetSavedRoomsFolder();
        if (!Directory.Exists(savedFolder))
            return;

        string[] files;
        try
        {
            files = Directory.GetFiles(savedFolder, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not list saved rooms: {e.Message}");
            return;
        }

        foreach (string f in files.OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
        {
            if (!TryResolveSavedRoomPath(f, out string full, out string display))
                continue;
            AddRoomListEntry(full, display);
        }

        if (scroll != null)
        {
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1f;
        }

        Debug.Log($"[RoomConfigLoader] Listed {files.Length} room file(s) from \"{savedFolder}\"");
    }

    void ClearContentImmediate()
    {
        if (contentView == null)
            return;

        for (int i = contentView.childCount - 1; i >= 0; i--)
        {
            Transform child = contentView.GetChild(i);
            if (child == null)
                continue;
            // Immediate so the same-frame rebuild does not see stale rows.
            DestroyImmediate(child.gameObject);
        }
    }
}
