#if UNITY_EDITOR
using UnityEditor;

namespace SplenSoft.AssetBundles
{
    class EnvironmentSettingsWindow : EditorWindow
    {
        public static void ShowWindow()
        {
            GetWindow(typeof(EnvironmentSettingsWindow));
        }

        void OnGUI()
        {
            var settings = EnvironmentsSettings.Get();
            settings.DisplayEditorFields();
        }
    }
}
#endif