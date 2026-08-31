using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Resolves export output folders.
/// Parent is always Application.persistentDataPath (AppData LocalLow).
/// Every export goes under {parent}/{RoomName}/ with deliverables in subfolders
/// (OBJ/Room, elevations, proposals, snapshots). No OS folder picker.
/// </summary>
public static class ExportPaths
{
    private const string ParentFolderPrefsKey = "ExportParentFolder";

    /// <summary>
    /// Optional one-shot base path override for the current export. Cleared when
    /// the export finishes or is cancelled. Room/object exports no longer set this.
    /// </summary>
    private static string _sessionExportBaseOverride;

    /// <summary>
    /// Ensures the room is saved, creates {persistentDataPath}/{RoomName}/, then runs
    /// <paramref name="onReady"/>. No File Explorer — exports always land under AppData.
    /// </summary>
    public static void PromptForExportFolderThen(Action onReady)
    {
        if (onReady == null)
            return;

        if (!EnsureRoomSavedForExport())
            return;

        ClearExportBaseOverride();
        ClearCustomParentFolder();

        try
        {
            EnsureDirectories();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not create export folders: {e.Message}");
            UI_DialogPrompt.Open(
                "Could not create the export folder under AppData.",
                new ButtonAction("OK"));
            return;
        }

        onReady();
    }

    public static string GetSuggestedExportParentFolder()
    {
        string parent = GetParentFolder();
        try
        {
            if (!Directory.Exists(parent))
                Directory.CreateDirectory(parent);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not create export parent folder '{parent}': {e.Message}");
            parent = GetDefaultParentFolder();
            try
            {
                Directory.CreateDirectory(parent);
            }
            catch (Exception)
            {
            }
        }

        return parent;
    }

    public static string GetSuggestedExportFolderName()
        => SanitizeFolderName(GetRoomExportName());

    /// <summary>
    /// Applies a path chosen in the export save dialog (directory + folder name, no file required).
    /// </summary>
    public static void ApplyPickedExportLocation(string pickedPath)
    {
        if (string.IsNullOrWhiteSpace(pickedPath))
            return;

        string full;
        try
        {
            full = Path.GetFullPath(pickedPath.Trim());
        }
        catch (Exception)
        {
            full = pickedPath.Trim();
        }

        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Dialog is for confirming the room folder name only — parent is always AppData.
        string leaf = Path.GetFileName(full);
        string suggested = GetSuggestedExportFolderName();
        string withoutExt = Path.GetFileNameWithoutExtension(leaf);
        if (!string.IsNullOrEmpty(Path.GetExtension(leaf))
            && (string.IsNullOrEmpty(suggested)
                || withoutExt.Equals(suggested, StringComparison.OrdinalIgnoreCase)))
            leaf = withoutExt;

        if (string.IsNullOrEmpty(leaf))
            leaf = suggested;
        if (string.IsNullOrEmpty(leaf))
            return;

        leaf = SanitizeFolderName(leaf);
        ClearCustomParentFolder();
        SetExportBaseOverride(Path.Combine(GetDefaultParentFolder(), leaf));
    }

    public static void SetExportBaseOverride(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _sessionExportBaseOverride = null;
            return;
        }

        try
        {
            _sessionExportBaseOverride = Path.GetFullPath(path.Trim());
        }
        catch (Exception)
        {
            _sessionExportBaseOverride = path.Trim();
        }
    }

    public static void ClearExportBaseOverride()
        => _sessionExportBaseOverride = null;

    public static bool HasExportBaseOverride()
        => !string.IsNullOrWhiteSpace(_sessionExportBaseOverride);

    public static string GetRoomExportName()
    {
        string savedName = ConfigurationManager.GetCurrentRoomSaveName();
        if (!string.IsNullOrWhiteSpace(savedName))
            return savedName;

        // Only reached if a caller skipped EnsureRoomSavedForExport — keep a safe fallback.
        return "Untitled Room";
    }

    /// <summary>True when the room has been saved and has a stable save name for export folders.</summary>
    public static bool HasSavedRoomName()
        => !string.IsNullOrWhiteSpace(ConfigurationManager.GetCurrentRoomSaveName());

    /// <summary>
    /// Exports require a saved room name. If missing, prompts the user to save and returns false.
    /// </summary>
    public static bool EnsureRoomSavedForExport()
    {
        if (HasSavedRoomName())
            return true;

        UI_DialogPrompt.Open(
            "Save the room before exporting.\nFiles are organized under the room's save name.",
            new ButtonAction("Save Room", () =>
            {
                UI_DialogPrompt.Close();
                Save.OpenSaveRoomPrompt();
            }),
            new ButtonAction("Cancel"));
        return false;
    }

    public static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Untitled_Room";

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name.Trim().Replace(' ', '_');
    }

    /// <summary>
    /// Human-readable export name for a placed object — catalog/type name, not the instance GameObject id.
    /// </summary>
    public static string GetSelectableExportName(Selectable selectable, string fallback = "Object")
    {
        if (selectable == null)
            return SanitizeFolderName(fallback);

        string name = null;

        try
        {
            if (selectable.RelatedSelectables != null && selectable.RelatedSelectables.Count > 0)
            {
                var meta = selectable.GetMetadata();
                if (meta != null && IsUsefulExportName(meta.Name))
                    name = meta.Name;
            }
        }
        catch (Exception)
        {
        }

        if (!IsUsefulExportName(name) && selectable.MetaData != null && IsUsefulExportName(selectable.MetaData.Name))
            name = selectable.MetaData.Name;

        if (!IsUsefulExportName(name) && IsUsefulExportName(selectable.UIButtonName))
            name = selectable.UIButtonName;

        if (!IsUsefulExportName(name) && IsUsefulExportName(fallback))
            name = fallback;

        if (!IsUsefulExportName(name))
            name = "Object";

        // Optional sub-part label (e.g. specific boom head variant) when present and readable.
        try
        {
            string sub = selectable.MetaData?.SubPartName;
            if (IsUsefulExportName(sub) && name.IndexOf(sub, StringComparison.OrdinalIgnoreCase) < 0)
                name = $"{name}_{sub}";
        }
        catch (Exception)
        {
        }

        return SanitizeFolderName(name);
    }

    public static string GetObjectExportName(GameObject obj, string fallback = "Object")
    {
        if (obj == null)
            return SanitizeFolderName(fallback);

        var selectable = obj.GetComponent<Selectable>()
                         ?? obj.GetComponentInParent<Selectable>();
        if (selectable != null)
            return GetSelectableExportName(selectable, fallback);

        return IsUsefulExportName(obj.name)
            ? SanitizeFolderName(obj.name)
            : SanitizeFolderName(fallback);
    }

    /// <summary>True if the string looks like a catalog/display name rather than a GUID or bundle id.</summary>
    public static bool IsUsefulExportName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        name = name.Trim();
        if (name.Equals("Selectable", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Scene", StringComparison.OrdinalIgnoreCase)
            || name.Equals("GameObject", StringComparison.OrdinalIgnoreCase))
            return false;

        // Asset-bundle / instance ids are long hex (often with dashes/underscores).
        string hexOnly = name.Replace("-", "").Replace("_", "");
        if (hexOnly.Length >= 24)
        {
            bool allHex = true;
            for (int i = 0; i < hexOnly.Length; i++)
            {
                if (!Uri.IsHexDigit(hexOnly[i]))
                {
                    allHex = false;
                    break;
                }
            }
            if (allHex)
                return false;
        }

        return true;
    }

    public static string GetDefaultParentFolder()
        => Application.persistentDataPath;

    /// <summary>
    /// Always AppData LocalLow (persistentDataPath). Custom/Documents prefs are cleared
    /// so room exports stay under …/Operating Room Software/{RoomName}/.
    /// </summary>
    public static string GetParentFolder()
    {
        if (HasCustomParentFolder())
            ClearCustomParentFolder();

        return GetDefaultParentFolder();
    }

    public static bool HasCustomParentFolder()
    {
        return PlayerPrefs.HasKey(ParentFolderPrefsKey)
               && !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(ParentFolderPrefsKey));
    }

    public static void SetParentFolder(string path)
    {
        // Exports are locked to AppData LocalLow — ignore attempts to store another parent.
        ClearCustomParentFolder();
    }

    /// <summary>
    /// Walks up while the leaf folder equals the current room save name so
    /// …/TestRoom/TestRoom/TestRoom does not keep nesting deeper each export.
    /// </summary>
    public static string NormalizeParentFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        try
        {
            string full = Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string room = SanitizeFolderName(GetRoomExportName());
            if (string.IsNullOrEmpty(room))
                return full;

            // Collapse every trailing room-name segment, not just one level.
            while (true)
            {
                string leaf = Path.GetFileName(full);
                if (string.IsNullOrEmpty(leaf)
                    || !leaf.Equals(room, StringComparison.OrdinalIgnoreCase))
                    break;

                string parent = Path.GetDirectoryName(full);
                if (string.IsNullOrEmpty(parent) || parent == full)
                    break;

                full = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return full;
        }
        catch (Exception)
        {
            return path.Trim();
        }
    }

    public static void ClearCustomParentFolder()
    {
        PlayerPrefs.DeleteKey(ParentFolderPrefsKey);
        PlayerPrefs.Save();
    }

    /// <summary>AppData LocalLow + room-name subfolder. All deliverables live under here.</summary>
    public static string GetExportBasePath()
    {
        string roomName = SanitizeFolderName(GetRoomExportName());

        if (!string.IsNullOrWhiteSpace(_sessionExportBaseOverride))
        {
            // Override is the room folder under AppData; strip nested room names then re-attach.
            string leaf = Path.GetFileName(
                _sessionExportBaseOverride.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(leaf))
                roomName = SanitizeFolderName(leaf);
        }

        return Path.Combine(GetDefaultParentFolder(), roomName);
    }

    /// <summary>Folder for 3D model exports: {RoomName}/OBJ/Room</summary>
    public static string ObjSceneDir => Path.Combine(GetExportBasePath(), "OBJ", "Room");
    public static string ElevationsDir => Path.Combine(GetExportBasePath(), "elevations");
    public static string ProposalsDir => Path.Combine(GetExportBasePath(), "proposals");
    public static string SnapshotsDir => Path.Combine(GetExportBasePath(), "snapshots");
    public static string PdfDir => ElevationsDir;
    public static string RendersDir => SnapshotsDir;

    /// <summary>
    /// Ensures the room export root exists. Deliverable subfolders
    /// (elevations, snapshots, etc.) are created only when that export actually runs.
    /// </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(GetExportBasePath());
    }
}
