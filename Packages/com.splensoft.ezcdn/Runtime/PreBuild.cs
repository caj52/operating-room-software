#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SplenSoft.AssetBundles
{
    internal class PreBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = AssetBundleManagerSettings.Get();
            if (!settings.KeepLocalCopy) return;

            BuildTarget buildTarget = report.summary.platform;
            string buildTargetString = buildTarget.ToString();

            var streamingAssetsPath = Path.Combine(
                Application.dataPath,
                "StreamingAssets"
            );

            var assetPath = Path.Combine(
                streamingAssetsPath,
                "AssetBundles"
            );

            int exists = Directory.Exists(streamingAssetsPath) ? 0 : 1;
            PlayerPrefs.SetInt("ezcdn-streamingassets-didnotexist", exists);

            if (!Directory.Exists(assetPath)) 
            {
                Directory.CreateDirectory(assetPath);
            }

            var di = new DirectoryInfo(assetPath);

            // delete existing assets in StreamAssets
            foreach (FileInfo file in di.EnumerateFiles())
            {
                file.Delete();
            }

            var assetBundlesPath = Path.Combine(
                AssetBundleManager.AssetBundlePath,
                buildTargetString
            );

            // ensure assets have been built once
            if (!Directory.Exists(assetBundlesPath)) 
            {
                throw new System.Exception($"No asset bundle " +
                    $"path ({assetBundlesPath}) exists for build target " +
                    $"{buildTargetString}. Please complete an " +
                    $"assetbundle build before building the app.");
            }

            // get existing asset bundle files
            string[] files = Directory.GetFiles(
                assetBundlesPath,
                "*",
                SearchOption.AllDirectories
            );

            // copy all files to streaming assets
            foreach (string path in files)
            {
                string newFilePath = path.Replace(
                    assetBundlesPath,
                    assetPath
                );

                File.Copy(path, newFilePath, true);
            }
        }
    }
}
#endif