using SplenSoft.AssetBundles;
using UnityEngine;

// The sample event workflow involves much less code,
// but you don't quite get as much control
public class SampleEventWorkflow : MonoBehaviour
{
    // This launches an asynchronous request.
    // Called by a button in the sample scene
    // Since we subscribed to the two result events
    // in the editor,
    // we will know when its done downloading
    public void RequestAsset()
    {
        GetComponent<PrefabRequester>().GetAsset();
    }

    // Called automatically by the OnRetrievalSuccess event.
    // We subscribed to the event in the editor
    public void InstantiateResult()
    {
        var prefab = GetComponent<PrefabRequester>().Asset;
        Instantiate(prefab, transform.position, transform.rotation);
    }
}