using System;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// A trackable progress object that can be passed to 
    /// <see cref="AssetBundleManager.GetAsset"/> and 
    /// <see cref="AssetBundleManager.GetAssetBundle"/> 
    /// through a <see cref="Progress{T}"/> object
    /// </summary>
    public class AssetRetrievalProgress : IProgress<AssetRetrievalProgress>
    {
        public AssetRetrievalProgress(AssetRetrievalStatus status, float progress)
        {
            Status = status;
            Progress = progress;
        }

        /// <summary>
        /// Gets the status of the current retrieval operation
        /// </summary>
        public AssetRetrievalStatus Status { get; }

        /// <summary>
        /// Returns true if <see cref="Status"/> 
        /// == <see cref="AssetRetrievalStatus.Done"/>
        /// </summary>
        public bool IsDone => Status == AssetRetrievalStatus.Done;

        /// <summary>
        /// Tracks the progress of the retrieval operation between 0 (just started) to 1 (done)
        /// </summary>
        public float Progress { get; }

        public void Report(AssetRetrievalProgress value)
        {
            
        }
    }
}