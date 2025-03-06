using System;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Add this attribute to a custom class to include it in the <see cref="AssetBundleManager"/> operations. Useful for custom <see cref="UnityEngine.ScriptableObject"/>s. You can also add the class to the list of managed types in the SplenSoft -> Asset Bundles -> Settings
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class ManagedAssetAttribute : Attribute
    {
    }
}