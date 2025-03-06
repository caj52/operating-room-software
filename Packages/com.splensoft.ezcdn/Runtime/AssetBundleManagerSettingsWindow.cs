#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace SplenSoft.AssetBundles
{
    class AssetBundleManagerSettingsWindow : EditorWindow
    {
        [MenuItem("Tools/Easy CDN/Settings", priority = 999)]
        private static void ShowWindow()
        {
            GetWindow(typeof(AssetBundleManagerSettingsWindow));
        }

        [MenuItem("Tools/Easy CDN/Settings", isValidateFunction: true)]
        private static bool ShowWindow_Validate()
        {
            return !Application.isPlaying;
        }

        void OnGUI()
        {
            var settings = AssetBundleManagerSettings.Get();
            settings.DisplayEditorFields();
        }
    }
}
#endif