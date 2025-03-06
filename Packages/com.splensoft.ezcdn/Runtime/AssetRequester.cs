using UnityEngine;

namespace SplenSoft.AssetBundles
{
    /// <summary>
    /// <see cref="MonoBehaviour"/> component that can request an asset from the CDN. Accepts any <see cref="UnityEngine.Object"/> as an asset. Cannot instantiate or further manipulate the asset without the help of code. For codeless or more specific usages, try <see cref="SceneRequester"/>
    /// </summary>
    public class AssetRequester : Requester<UnityEngine.Object>
    {
        [field: SerializeField,
        AssetBundleReference(typeof(UnityEngine.Object), "Asset")]
        public override string AssetBundleName { get; set; }
    }
}