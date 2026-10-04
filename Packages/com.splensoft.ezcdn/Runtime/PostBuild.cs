#if UNITY_EDITOR
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// After player build: drop the temporary Assets/StreamingAssets/AssetBundles
    /// stage. Do not copy into the player — Unity already shipped those files,
    /// and injecting into a Mac .app after codesign makes Gatekeeper refuse to open it.
    /// </summary>
    internal class PostBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            var settings = AssetBundleManagerSettings.Get();
            bool shipLocal = !settings.AllowRemoteCdn || settings.KeepLocalCopy;

            if (shipLocal)
            {
                string playerBundles = Path.Combine(
                    AssetBundleManager.GetPlayerStreamingAssetsPath(report),
                    "AssetBundles");
                if (!Directory.Exists(playerBundles) ||
                    Directory.GetFiles(playerBundles, "*", SearchOption.AllDirectories).Length == 0)
                {
                    Debug.LogError(
                        "[Placeables] Player StreamingAssets/AssetBundles is empty. " +
                        "Placeables will fail at runtime. Rebuild so PreBuild can stage bundles " +
                        "into Assets/StreamingAssets before Unity copies them into the player.");
                }
            }

            AssetBundleManager.EndProjectStreamingAssetsStage();
        }
    }
}
#endif
