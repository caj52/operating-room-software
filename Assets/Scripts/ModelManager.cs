//2024-03-26 deprecated in favor of AssetBundlePreprocessor

//#if UNITY_EDITOR
//using System;
//using System.Collections;
//using System.Collections.Generic;
//using UnityEditor;
//using UnityEditor.Build.Reporting;
//using UnityEditor.Build;
//using UnityEngine;

///// <summary>
///// Automatically manages models to make them properly configured for ORS
///// </summary>
//public static class ModelManager
//{
//    /// <summary>
//    /// Finds all models in the project and marks them as read/write
//    /// </summary>
//    [MenuItem("ORS/Mark all models as read-write")]
//    public static void MarkAllModelsAsReadWrite()
//    {
//        string[] guids = AssetDatabase.FindAssets("t: model");

//        foreach (string guid in guids) 
//        {
//            HandleModel(guid);
//        }
//    }

//    /// <summary>
//    /// If guid or path leads to a model asset, marks the ModelImporter as read/write
//    /// </summary>
//    public static void HandleModel(string guidOrPath)
//    {
//        try
//        {
//            string path = AssetDatabase.GUIDToAssetPath(guidOrPath);
//            if (string.IsNullOrEmpty(path))
//            {
//                path = guidOrPath;
//            }

//            var modelImporter = (ModelImporter)AssetImporter.GetAtPath(path);
//            if (modelImporter != null && !modelImporter.isReadable)
//            {
//                var obj = AssetDatabase.LoadAssetAtPath(path, typeof(UnityEngine.Object));
//                if (obj != null)
//                {
//                    Debug.Log($"Model {obj.name} was not marked as Read/Write. Fixing ...");
//                }

//                modelImporter.isReadable = true;
//                EditorUtility.SetDirty(modelImporter);
//                modelImporter.SaveAndReimport();
//            }
//        }
//        catch (InvalidCastException) { }
//        catch (Exception ex)
//        {
//            //Debug.Log($"Model adjustment failed: {ex.Message}");
//            Debug.LogException(ex);
//        }
//    }
//}

///// <summary>
///// Checks paths on save. If path is a model asset, will be processed to be properly configuerd for ORS
///// </summary>
//public class CheckModelsOnSave : AssetModificationProcessor
//{
//    private static string[] OnWillSaveAssets(string[] paths)
//    {
//        foreach (string path in paths)
//            ModelManager.HandleModel(path);
//        return paths;
//    }
//}

///// <summary>
///// Ensures all model files are properly configured before the app is built
///// </summary>
//class CheckModelsOnPreBuild : IPreprocessBuildWithReport
//{
//    public int callbackOrder { get { return 0; } }
//    public void OnPreprocessBuild(BuildReport report)
//    {
//        ModelManager.MarkAllModelsAsReadWrite();
//    }
//}
//#endif