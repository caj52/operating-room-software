using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SplenSoft.AssetBundles
{
    public class CustomAssetHandler_Texture2D : CustomAssetHandler
    {
        public override Type Type { get; } = typeof(Texture2D);

        public override void Handle(UnityEngine.Object obj, out string assetBundleNamePrefix)
        {
#if UNITY_EDITOR
            // Unity doesn't import sprites by the 'Sprite' type.
            // This is bad and will cause the AssetBundleReference
            // attribute to fail if used on a Sprite field

            // Instead, we need to search for a certain type of Texture2D

            // First we convert the asset importer to a TextureImporter
            var textureImporter = (TextureImporter)AssetImporter
                                  .GetAtPath(AssetDatabase.GetAssetPath(obj));

            // Then we filter by the type of texture
            if (textureImporter.textureType == TextureImporterType.Sprite)
            {
                // Then this asset bundle can be named properly
                assetBundleNamePrefix = nameof(Sprite);
            }
            else
#endif
            {
                // Otherwise, we can just handle it normally if it's not a sprite
                assetBundleNamePrefix = Type.ToString();
            }

            // It's extremely important to note that in order for this to function
            // properly, the types Sprite and Texture2D must be included in the
            // Asset Bundle Manager prefab config (they both are by default)
        }
    }
}