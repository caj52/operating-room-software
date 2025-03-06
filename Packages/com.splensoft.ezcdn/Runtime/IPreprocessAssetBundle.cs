namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Used to add custom processing to an asset 
    /// before it is packaged by EZ-CDN
    /// </summary>
    public interface IPreprocessAssetBundle
    {
        public void OnPreprocessAssetBundle();
    }
}