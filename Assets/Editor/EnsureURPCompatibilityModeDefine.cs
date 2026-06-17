#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;

public static class EnsureURPCompatibilityModeDefine
{
    const string Define = "URP_COMPATIBILITY_MODE";
    const string SessionKey = "EnsureURPCompatibilityModeDefine.Compiled";

    [InitializeOnLoadMethod]
    static void OnLoad()
    {
        EditorApplication.delayCall += () =>
        {
            bool changed = ApplyAll();
            if (changed || !SessionState.GetBool(SessionKey, false))
            {
                SessionState.SetBool(SessionKey, true);
                CompilationPipeline.RequestScriptCompilation();
            }
        };
    }

    static bool ApplyAll()
    {
        bool changed = false;

        foreach (BuildTargetGroup group in (BuildTargetGroup[])Enum.GetValues(typeof(BuildTargetGroup)))
        {
            if (group == BuildTargetGroup.Unknown)
                continue;

            try
            {
                var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
                var defines = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
                if (defines.Contains(Define))
                    continue;

                PlayerSettings.SetScriptingDefineSymbols(
                    namedTarget,
                    string.IsNullOrEmpty(defines) ? Define : $"{defines};{Define}");
                changed = true;
            }
            catch (ArgumentException)
            {
                // Platform not installed in this Editor.
            }
        }

        if (changed)
            AssetDatabase.SaveAssets();

        return changed;
    }
}
#endif
