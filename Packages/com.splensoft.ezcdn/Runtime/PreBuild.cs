#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Packs local placeable/UI bundles before BuildPlayer, then builds.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlaceablePlayerBuildHook
    {
        private static bool _isHandlingBuild;

        static PlaceablePlayerBuildHook()
        {
            BuildPlayerWindow.RegisterBuildPlayerHandler(OnBuildPlayer);
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
                {
                    AssetBundleManager.CleanupProjectStreamingAssetBundles();

                    if (!AssetBundleManager.BuiltPlatformManifestExists(options.target, settings))
                    {
                        Debug.Log($"[Placeables] Packing bundles for {options.target}…");
                        AssetBundleManager.BuildAndStageLocalBundles(options.target, forceRebuild: false);
                    }
                    else
                    {
                        Debug.Log($"[Placeables] Using existing packs for {options.target}");
                        AssetBundleManager.EnsureRuntimePlatformManifestAlias(options.target, settings);
                    }
                }

                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
            }
            finally
            {
                _isHandlingBuild = false;
            }
        }
    }

    internal class PreBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = AssetBundleManagerSettings.Get();
            if (settings.AllowRemoteCdn && !settings.KeepLocalCopy)
                return;

            BuildTarget target = report.summary.platform;
            if (!AssetBundleManager.BuiltPlatformManifestExists(target, settings))
            {
                throw new BuildFailedException(
                    $"Placeable bundles missing under Library/PlayerLocalBundles/{target}/. " +
                    "Wait for scripts to finish compiling, then Build again so packing can complete.");
            }
        }
    }
}
#endif
