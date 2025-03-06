#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SplenSoft.AssetBundles
{
    public static partial class AssetBundleManager
    {
        class AssetBundleManagerWindow : EditorWindow
        {
            [field: SerializeField] private bool CleanBuild { get; set; }

            private Dictionary<BuildTarget, bool> BuildTargets { get; set; } = new Dictionary<BuildTarget, bool>();

            [MenuItem("Tools/Easy CDN/Build")]
            private static void ShowWindow()
            {
                GetWindow(typeof(AssetBundleManagerWindow));
            }

            [MenuItem("Tools/Easy CDN/Build", isValidateFunction: true)]
            private static bool ShowWindow_Validate()
            {
                return !Application.isPlaying;
            }

            void OnGUI()
            {
                var label = new GUIContent("Clean Build", "Delete all asset bundles and fully rebuild. Should be done at least once prior to pushing a major version to production");
                CleanBuild = EditorGUILayout.Toggle(label, CleanBuild);

                var settings = AssetBundleManagerSettings
                                        .Get();

                List<BuildTarget> validBuildTargets = settings
                    .BuildTargetsByPlatform
                    .Select(x => (BuildTarget)x.Value)
                    .Distinct()
                    .ToList();

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Build for:");
                EditorGUI.indentLevel++;

                var BuildTargetBuckets = new List<BuildTargetBucket>();

                validBuildTargets.ForEach(x =>
                    {
                        BuildTargets.TryGetValue(x, out bool value);
                        BuildTargets[x] = EditorGUILayout.Toggle(x.ToString(), value);
                        if (BuildTargets[x])
                        {
                            string bucketId = settings.BucketsByBuildTarget[(int)x];
                            BuildTargetBuckets.Add(new BuildTargetBucket(x, bucketId));
                        }
                    });
                EditorGUI.indentLevel--;

                if (GUILayout.Button("Build"))
                {
                    if (CleanBuild) 
                    { 
                        if (!EditorUtility.DisplayDialog("Clean build asset bundles", "This will delete all asset bundles, fully rebuild and regenerate names. This may take a while. Do you want to continue?", "Yes", "Cancel"))
                        {
                            return;
                        }
                    }

                    PackageAssetBundles(
                        BuildTargetBuckets,
                        cleanBuild: CleanBuild
                    );
                }
            }
        }

        public class BuildTargetBucket
        {
            public BuildTargetBucket( BuildTarget target, string bucketId )
            {
                BuildTarget = target;
                BucketId = bucketId;
            }
            public BuildTarget BuildTarget;
            public string BucketId;
        }
    }
}
#endif