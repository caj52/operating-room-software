#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies GEStorageUnit mesh facing through Unity's prefab API (not hand-edited YAML).
/// </summary>
public static class GEStorageUnitRotate
{
    private const string PrefabPath = "Assets/Prefabs/Selectables/GEStorageUnit.prefab";
    private const string ReportPath = "TestData/GEStorageUnit_renders/rotate_report.txt";

    /// <summary>
    /// Upright facing the user: (90, 270, 0).
    /// That is the confirmed upright pose (90, 90, 0) turned 180° around Y.
    /// </summary>
    [MenuItem("Tools/GEStorageUnit/Face User (180 Y)")]
    public static void FaceUser()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError("Could not load " + PrefabPath);
            return;
        }

        try
        {
            var mesh = root.transform.Find("SERVICE STORAGE CABINET/cerradura.006");
            if (mesh == null)
            {
                Debug.LogError("cerradura.006 not found under SERVICE STORAGE CABINET");
                return;
            }

            Vector3 before = mesh.localEulerAngles;
            // Plain absolute set — upright (90,90,0) + 180° Y.
            mesh.localRotation = Quaternion.Euler(90f, 270f, 0f);
            Vector3 after = mesh.localEulerAngles;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string msg =
                "GEStorageUnit Face User (180 Y)\n" +
                "before euler=" + before + "\n" +
                "after  euler=" + after + "\n" +
                "set to Quaternion.Euler(90, 270, 0)\n" +
                "scale=" + mesh.localScale + "\n";
            string abs = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllText(abs, msg);
            Debug.Log(msg);

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "GEStorageUnit",
                    "Set cerradura.006 to Euler (90, 270, 0).\n\n" +
                    "Before: " + before + "\nAfter: " + after +
                    "\n\nDelete any old scene instance and place a fresh one.",
                    "OK");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static void RunBatch()
    {
        FaceUser();
        EditorApplication.Exit(0);
    }
}
#endif
