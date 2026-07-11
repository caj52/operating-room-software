using System.Diagnostics;
using System.IO;
using UnityEngine;

public static class ExportFolderUtility
{
    /// <summary>
    /// Opens the path in the OS file manager. If <paramref name="path"/> is a file,
    /// selects/highlights that file; if a folder, opens the folder.
    /// </summary>
    public static void RevealInFileManager(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        path = path.Replace('/', Path.DirectorySeparatorChar);

        bool isFile = File.Exists(path);
        if (!isFile && !Directory.Exists(path))
        {
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);
            if (Directory.Exists(parent))
                path = parent;
            else
                return;
            isFile = false;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (isFile)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        if (isFile)
            Process.Start("open", $"-R \"{path}\"");
        else
            Process.Start("open", $"\"{path}\"");
#else
        string openPath = isFile ? Path.GetDirectoryName(path) : path;
        Application.OpenURL("file:///" + openPath.Replace("\\", "/"));
#endif
    }
}
