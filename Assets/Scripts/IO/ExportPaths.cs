using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Resolves export output folders. Uses a custom folder when set; otherwise Documents.
/// </summary>
public static class ExportPaths
{
    private const string DocumentsFolderName = "Operating Room Exports";

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

    public static string GetExportBasePath()
    {
        string customPath = FullRoomSave.GetRoomPath();
        if (!string.IsNullOrEmpty(customPath))
            return customPath;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            DocumentsFolderName,
            SanitizeFolderName(GetRoomExportName()));
    }

    public static string ObjSceneDir => Path.Combine(GetExportBasePath(), "ObjFile");
    public static string ElevationsDir => Path.Combine(GetExportBasePath(), "elevations");
    public static string ProposalsDir => Path.Combine(GetExportBasePath(), "proposals");
    public static string SnapshotsDir => Path.Combine(GetExportBasePath(), "snapshots");
    public static string PdfDir => ElevationsDir;
    public static string RendersDir => Path.Combine(GetExportBasePath(), "Renders");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ObjSceneDir);
        Directory.CreateDirectory(ElevationsDir);
        Directory.CreateDirectory(ProposalsDir);
        Directory.CreateDirectory(SnapshotsDir);
        Directory.CreateDirectory(RendersDir);
    }
}
