#if UNITY_EDITOR
using SplenSoft.AssetBundles;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;

public class AssetBundlePreprocessor : AssetBundleProcessor
{
    public override void OnPreprocessAssetBundles()
    {
        MarkAllTexturesReadWrite();
        MarkAllModelsReadWrite();
    }

    private void MarkAllTexturesReadWrite()
    {
        ForEachAsset("t: texture2d", path =>
        {
            try
            {
                var textureImporter =
                    (TextureImporter)AssetImporter.GetAtPath(path);

                if (!textureImporter.isReadable || 
                textureImporter.textureCompression != 
                TextureImporterCompression.Uncompressed)
                {
                    textureImporter.textureCompression = 
                        TextureImporterCompression.Uncompressed;

                    textureImporter.isReadable = true;
                    EditorUtility.SetDirty(textureImporter);
                    textureImporter.SaveAndReimport();
                }
            }
            catch (InvalidCastException)
            {
                Debug.Log($"{nameof(TextureImporter)} cast was invalid for asset {path}");
            }
        });
    }

    private void MarkAllModelsReadWrite()
    {
        ForEachAsset("t: model", path =>
        {
            var modelImporter = (ModelImporter)AssetImporter.GetAtPath(path);
            if (modelImporter != null && !modelImporter.isReadable)
            {
                modelImporter.isReadable = true;
                EditorUtility.SetDirty(modelImporter);
                modelImporter.SaveAndReimport();
            }
        });
    }

    private void ForEachAsset(string search, Action<string> action)
    {
        var guids = AssetDatabase.FindAssets(
                search,
                new string[] { "Assets" })
                .ToList();

        List<string> objPaths = guids
            .ConvertAll(x => AssetDatabase.GUIDToAssetPath(x));

        for (int i = 0; i < objPaths.Count; i++)
        {
            string path = objPaths[i];

            string guid = guids[i];
            if (string.IsNullOrWhiteSpace(guid))
            {
                Debug.LogError($"Guid was null for {path}");
                continue;
            }

            action(path);
        }
    }
}
#endif