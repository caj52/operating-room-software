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
            return PlayerPrefs.GetString(ParentFolderPrefsKey);

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

        PlayerPrefs.SetString(ParentFolderPrefsKey, path.Trim());
        PlayerPrefs.Save();
    }

    public static void ClearCustomParentFolder()
    {
        PlayerPrefs.DeleteKey(ParentFolderPrefsKey);
        PlayerPrefs.Save();
    }

    /// <summary>Parent folder + room-name subfolder. All deliverables live under here.</summary>
    public static string GetExportBasePath()
    {
        return Path.Combine(GetParentFolder(), SanitizeFolderName(GetRoomExportName()));
    }

    public static string ObjSceneDir => Path.Combine(GetExportBasePath(), "ObjFile");
    public static string ElevationsDir => Path.Combine(GetExportBasePath(), "elevations");
    public static string ProposalsDir => Path.Combine(GetExportBasePath(), "proposals");
    public static string SnapshotsDir => Path.Combine(GetExportBasePath(), "snapshots");
    public static string PdfDir => ElevationsDir;
    public static string RendersDir => Path.Combine(GetExportBasePath(), "Renders");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(GetExportBasePath());
        Directory.CreateDirectory(ObjSceneDir);
        Directory.CreateDirectory(ElevationsDir);
        Directory.CreateDirectory(ProposalsDir);
        Directory.CreateDirectory(SnapshotsDir);
        Directory.CreateDirectory(RendersDir);
    }
}
