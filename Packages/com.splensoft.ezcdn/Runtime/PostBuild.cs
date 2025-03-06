#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SplenSoft.AssetBundles
{
    internal class PostBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPostprocessBuild(BuildReport report)
        {
            var settings = AssetBundleManagerSettings.Get();
            if (!settings.KeepLocalCopy) return;

            var streamingAssetsPath = Path.Combine(
                Application.dataPath,
                "StreamingAssets"
            );

            var assetPath = Path.Combine(
                streamingAssetsPath,
                "AssetBundles"
            );

            if (!Directory.Exists(assetPath))
            {
                // maybe the user is deleting it
                return;
            }

            Directory.Delete(assetPath, true);

            var assetPathMeta = assetPath + ".meta";

            if (File.Exists(assetPathMeta)) 
            {
                File.Delete(assetPathMeta);
            }

            //if (IsDirectoryEmpty(streamingAssetsPath))
            if (PlayerPrefs.GetInt("ezcdn-streamingassets-didnotexist") == 1)
            {
                Directory.Delete(streamingAssetsPath, true);

                var streamingAssetsMeta = streamingAssetsPath + ".meta";

                if (File.Exists(streamingAssetsMeta))
                {
                    File.Delete(streamingAssetsMeta);
                }

                AssetDatabase.Refresh();
            }
        }

        private bool IsDirectoryEmpty(string path)
        {
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }
    }
}
#endif