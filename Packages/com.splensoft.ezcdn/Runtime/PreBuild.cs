#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Packs local placeable/UI bundles, stages them into
    /// Assets/StreamingAssets so Unity copies them into the player
    /// (and codesigns the Mac .app with those files already present).
    /// </summary>
    [InitializeOnLoad]
    internal static class PlaceablePlayerBuildHook
    {
        private static bool _isHandlingBuild;

        static PlaceablePlayerBuildHook()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(OnBuildPlayer);
            // Leftover stage from a failed build would import on editor load and hang.
            EditorApplication.delayCall += CleanupLeftoverStage;
        }

        private static void CleanupLeftoverStage()
        {
            EditorApplication.delayCall -= CleanupLeftoverStage;
            AssetBundleManager.EndProjectStreamingAssetsStage();
        }

        private static void OnBuildPlayer(BuildPlayerOptions options)
        {
            if (_isHandlingBuild)
            {
                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
                return;
            }

            _isHandlingBuild = true;
            try
            {
                var settings = AssetBundleManagerSettings.Get();
                if (!settings.AllowRemoteCdn || settings.KeepLocalCopy)
                    AssetBundleManager.PrepareLocalBundlesForPlayerBuild(options.target);

                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
            }
            finally
            {
                AssetBundleManager.EndProjectStreamingAssetsStage();
                _isHandlingBuild = false;
            }
        }
    }

    internal class PreBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = AssetBundleManagerSettings.Get();
            if (settings.AllowRemoteCdn && !settings.KeepLocalCopy)
                return;

            AssetBundleManager.PrepareLocalBundlesForPlayerBuild(report.summary.platform);
        }
    }
}
#endif
