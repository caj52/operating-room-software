using System;

namespace SplenSoft.AssetBundles
{
    public abstract class CustomAssetHandler
    {
        public abstract Type Type { get; }
        public abstract void Handle(UnityEngine.Object obj, out string assetBundleNamePrefix);
    }
}