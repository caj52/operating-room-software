#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Autodesk's FBXSDK_RUNTIME post-process picks the native plugin from the
/// <em>host</em> OS folder. Building a Mac player on Windows therefore copies
/// from Plugins/WinX64/UnityFbxSdkNative.bundle (missing) after creating an
/// empty .bundle in the .app — Gatekeeper / MDM then refuse to open it.
/// Replace that stub with the real Mac bundle from the package.
/// </summary>
internal class FbxMacNativePluginPostBuild : IPostprocessBuildWithReport
{
    public int callbackOrder => 100;

    const string BundleName = "UnityFbxSdkNative.bundle";
    const string PackageBundle = "Packages/com.autodesk.fbx/Editor/Plugins/" + BundleName;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneOSX)
            return;

        string source = Path.GetFullPath(PackageBundle);
        if (!Directory.Exists(source))
        {
            Debug.LogError(
                $"[FBX] Mac native plugin missing at {source}. " +
                "The player may contain an empty UnityFbxSdkNative.bundle that macOS will block.");
            return;
        }

        string dest = GetMacPluginBundlePath(report.summary.outputPath);
        if (string.IsNullOrEmpty(dest))
        {
            Debug.LogError("[FBX] Could not resolve .app/Contents/Plugins for the Mac player.");
            return;
        }

        if (Directory.Exists(dest))
            Directory.Delete(dest, true);

        CopyDirectory(source, dest);

        string native = Path.Combine(dest, "Contents", "MacOS", "UnityFbxSdkNative");
        var info = new FileInfo(native);
        if (!info.Exists || info.Length < 1_000_000)
        {
            throw new BuildFailedException(
                "[FBX] Mac UnityFbxSdkNative binary missing or empty after copy. " +
                "FBX export would fail in the player. Source: " + source);
        }

        Debug.Log($"[FBX] Copied Mac UnityFbxSdkNative.bundle ({info.Length} bytes) into {dest}");
    }

    static string GetMacPluginBundlePath(string outputPath)
    {
        if (string.IsNullOrEmpty(outputPath))
            return null;

        string appPath = outputPath;
        if (!appPath.EndsWith(".app", System.StringComparison.OrdinalIgnoreCase))
        {
            string withoutExt = Path.ChangeExtension(outputPath, null);
            string guessed = withoutExt + ".app";
            if (Directory.Exists(guessed))
                appPath = guessed;
            else if (Directory.Exists(outputPath))
            {
                string named = Path.Combine(outputPath, PlayerSettings.productName + ".app");
                if (Directory.Exists(named))
                    appPath = named;
            }
        }

        return Path.Combine(appPath, "Contents", "Plugins", BundleName);
    }

    static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            if (file.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        }

        foreach (string dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }
}
#endif
