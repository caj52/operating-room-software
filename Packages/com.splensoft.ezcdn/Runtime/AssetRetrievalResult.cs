using UnityEngine.Networking;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// Stores the results of the last attempt to download an asset bundle from the CDN
    /// </summary>
    public class AssetRetrievalResult
    {
        public AssetRetrievalResult() { }

        public AssetRetrievalResult(long responseCode, UnityWebRequest.Result result)
        {
            ResponseCode = responseCode;
            Result = result;
        }

        public AssetRetrievalResult(UnityWebRequest request)
        {
            ResponseCode = request.responseCode;
            Result = request.result;
        }

        public long ResponseCode;
        public UnityWebRequest.Result Result;
    }
}