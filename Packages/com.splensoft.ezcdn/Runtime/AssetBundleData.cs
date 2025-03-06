using System.Collections.Generic;
using UnityEngine;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Data instantiated by <see cref="AssetBundleManager"/> 
    /// on initialization. Each asset bundle in 
    /// the <see cref="AssetBundleManifest"/> will have its 
    /// own <see cref="AssetBundleData"/> entry
    /// </summary>
    public class AssetBundleData
    {
        public AssetBundleData(string assetBundleName)
        {
            AssetBundleName = assetBundleName;
        }

        public string AssetBundleName { get; }

        /// <summary>
        /// List of asset bundle names that this asset 
        /// bundle depends on. These should be loaded 
        /// first, and will be loaded first automatically 
        /// if the asset is requested through the 
        /// <see cref="AssetBundleManager"/>
        /// </summary>
        public List<string> Dependencies { get; set; } = new List<string>();

        /// <summary>
        /// The actual asset that has been cached after 
        /// successful retrieval. Can be used to 
        /// instantiate the asset any time, as long as 
        /// it's not null and as long as you know the type. 
        /// Check <see cref="Loaded"/> to ensure the asset 
        /// has been unpacked by the <see cref="AssetBundleManager"/> 
        /// at least once to ensure this is not null
        /// </summary>
        public Object Asset { get; set; }

        /// <summary>
        /// The asset bundle that has been cached after 
        /// successful retrieval. Can be used to 
        /// unpack its asset at any time, as long as its not null
        /// </summary>
        public AssetBundle AssetBundle { get; set; }

        /// <summary>
        /// Check this value to ensure the <see cref="Asset"/> is not null
        /// </summary>
        public bool Loaded { get; set; }

        /// <summary>
        /// True if at least one attempt has been made to retrieve this asset
        /// </summary>
        public bool DownloadStarted { get; set; }

        /// <summary>
        /// Used for caching
        /// </summary>
        public Hash128 Hash { get; set; }

        /// <summary>
        /// The last response code from a 
        /// download attempt. Returns 0 if no 
        /// download attempt was made, and can return 
        /// 200 even if it was loaded from cache
        /// </summary>
        public long LastResponseCode { get; set; }

        /// <summary>
        /// True if asset is currently being asynchronously loaded by Unity. 
        /// Must be checked to ensure an asset is never loaded more 
        /// than once if it's already loaded
        /// </summary>
        public bool IsLoading { get; set; }

        /// <summary>
        /// Removes the <see cref="AssetBundle"/> and 
        /// <see cref="Asset"/> from 
        /// this cache so it can be garbage collected. Destroys Asset.
        /// Unloads the asset bundle from memory. 
        /// <para />
        /// If you're looking to save memory without 
        /// unloading the <see cref="Asset"/>, try 
        /// <see cref="AssetBundle.Unload"/> instead.
        /// </summary>
        /// <param name="unloadAllLoadedObjects">If true, 
        /// all objects loaded from this 
        /// asset bundle will also be unloaded. Use only 
        /// if you're sure none of this 
        /// asset bundle's assets are in the scene</param>
        public void Flush(bool unloadAllLoadedObjects = false)
        {
            Log.Write(LogLevel.Verbose, 
                $"EZ-CDN: Flushing asset {AssetBundleName}");

            if (Asset != null) 
            {
                Object.Destroy(Asset);
                Asset = null;
            }
            
            if (AssetBundle != null)
            {
                Log.Write(LogLevel.Verbose, 
                    $"EZ-CDN: Unloading AssetBundle {AssetBundleName} (unloadAllLoadedObjects = {unloadAllLoadedObjects})");

                AssetBundle.Unload(unloadAllLoadedObjects);
            }

            Loaded = false;
        }
    }
}