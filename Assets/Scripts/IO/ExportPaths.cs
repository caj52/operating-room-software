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

        try
        {
            string project = UI_ClientMetaData.ProjectName;
            if (!string.IsNullOrWhiteSpace(project) &&
                !project.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                return project;
        }
        catch (Exception)
        {
        }

        return "Untitled Room";
    }

    public static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Untitled_Room";

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name.Trim().Replace(' ', '_');
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
