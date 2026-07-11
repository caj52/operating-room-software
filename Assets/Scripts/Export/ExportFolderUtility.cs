using System.Diagnostics;
using System.IO;
using UnityEngine;

public static class ExportFolderUtility
{
    public static void RevealInFileManager(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string target = path;
        if (File.Exists(path))
            target = Path.GetDirectoryName(path);
        else if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{target.Replace('/', '\\')}\"",
            UseShellExecute = true
        });
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        Process.Start("open", $"\"{target}\"");
#else
        Application.OpenURL("file:///" + target.Replace("\\", "/"));
#endif
    }
}
