#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// After player build: copy packed placeables into the player's
    /// StreamingAssets/AssetBundles (kept outside Assets/ during the build).
    /// </summary>
    internal class PostBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            var settings = AssetBundleManagerSettings.Get();
            bool shipLocal = !settings.AllowRemoteCdn || settings.KeepLocalCopy;
            if (!shipLocal)
                return;

            BuildTarget target = report.summary.platform;
            string builtPath = AssetBundleManager.GetBuiltBundlesDirectory(target);
            if (!Directory.Exists(builtPath))
            {
                Debug.LogError(
                    $"[Placeables] Built bundles missing at {builtPath}; " +
                    "player will not have placeables in StreamingAssets.");
                return;
            }

            AssetBundleManager.EnsureRuntimePlatformManifestAlias(target, settings);

            string playerStreaming = AssetBundleManager.GetPlayerStreamingAssetsPath(report);
            AssetBundleManager.CopyBundlesIntoPlayerStreamingAssets(playerStreaming, builtPath);

            // Leftover from older stage-into-Assets flow — delete without Refresh.
            AssetBundleManager.CleanupProjectStreamingAssetBundles();
        }
    }
}
#endif
