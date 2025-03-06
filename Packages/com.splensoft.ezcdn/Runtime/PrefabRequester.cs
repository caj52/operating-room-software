using System.Collections;
using UnityEngine;

namespace SplenSoft.AssetBundles
{
    [AddComponentMenu("EZ-CDN/Prefab Requester")]
    public class PrefabRequester : Requester<GameObject>
    {
        [field: SerializeField, 
        AssetBundleReference(typeof(GameObject), "Prefab")]
        public override string AssetBundleName { get; set; }

        [field: SerializeField] 
        private bool InstantiateOnAwake { get; set; }

        private IEnumerator Start()
        {
            if (!InstantiateOnAwake) yield break;

            bool failed = false;

            var task = AssetBundleManager.GetAsset<GameObject>(
                AssetBundleName,
                onFailure: _ => failed = true
            );

            yield return new WaitUntil(() => task.IsCompleted);

            if (failed)
            {
                Debug.LogError("Failed while retrieving asset");
                yield break;
            }

            Instantiate(task.Result, transform.position, transform.rotation);
        }
    }
}