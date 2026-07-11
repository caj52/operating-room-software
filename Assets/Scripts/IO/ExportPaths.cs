using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Resolves export output folders.
/// Parent folder defaults to Documents/Operating Room Exports (override stored in PlayerPrefs).
/// Every export always goes under a room-name subfolder inside that parent.
/// </summary>
public static class ExportPaths
{
    private const string DocumentsFolderName = "Operating Room Exports";
    private const string ParentFolderPrefsKey = "ExportParentFolder";

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
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            DocumentsFolderName);
    }

    /// <summary>Documents/Operating Room Exports, or the user-chosen parent from PlayerPrefs.</summary>
    public static string GetParentFolder()
    {
        if (HasCustomParentFolder())
            return NormalizeParentFolder(PlayerPrefs.GetString(ParentFolderPrefsKey));

        return GetDefaultParentFolder();
    }

    public static bool HasCustomParentFolder()
    {
        return PlayerPrefs.HasKey(ParentFolderPrefsKey)
               && !string.IsNullOrWhiteSpace(PlayerPrefs.GetString(ParentFolderPrefsKey));
    }

    public static void SetParentFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        // If the user picks an existing room export folder (…/Exports/RoomName),
        // store its parent so we don't nest RoomName/RoomName on the next export.
        path = NormalizeParentFolder(path.Trim());

        PlayerPrefs.SetString(ParentFolderPrefsKey, path);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Walks up one level when <paramref name="path"/> already looks like a room
    /// export directory (leaf equals the current room save name).
    /// </summary>
    public static string NormalizeParentFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        try
        {
            string full = Path.GetFullPath(path.Trim());
            string leaf = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string room = SanitizeFolderName(GetRoomExportName());
            if (!string.IsNullOrEmpty(leaf)
                && leaf.Equals(room, StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(parent))
                    return parent;
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

    /// <summary>Parent folder + room-name subfolder. All deliverables live under here.</summary>
    public static string GetExportBasePath()
    {
        string parent = GetParentFolder();
        string room = SanitizeFolderName(GetRoomExportName());

        // Guard against a stale prefs parent that already ends with the room name
        // (…/Operating Room Exports/SaveExample + SaveExample → double nest).
        try
        {
            string parentLeaf = Path.GetFileName(
                Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(parentLeaf)
                && parentLeaf.Equals(room, StringComparison.OrdinalIgnoreCase))
            {
                string up = Path.GetDirectoryName(Path.GetFullPath(parent));
                if (!string.IsNullOrEmpty(up))
                    parent = up;
            }
        }
        catch (Exception)
        {
        }

        return Path.Combine(parent, room);
    }

    /// <summary>
    /// Folder for 3D model exports (self-contained .glb files).
    /// Lives directly in the room folder — no nested ObjFile/Models subfolder.
    /// </summary>
    public static string ObjSceneDir => GetExportBasePath();
    public static string ElevationsDir => Path.Combine(GetExportBasePath(), "elevations");
    public static string ProposalsDir => Path.Combine(GetExportBasePath(), "proposals");
    public static string SnapshotsDir => Path.Combine(GetExportBasePath(), "snapshots");
    public static string PdfDir => ElevationsDir;
    public static string RendersDir => Path.Combine(GetExportBasePath(), "Renders");

    /// <summary>
    /// Ensures the room export root exists. Deliverable subfolders
    /// (elevations, snapshots, etc.) are created only when that export actually runs.
    /// </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(GetExportBasePath());
    }
}
