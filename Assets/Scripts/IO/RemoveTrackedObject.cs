using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// In the vast majority of situations, 
/// <see cref="TrackedObject"/> is needed on 
/// <see cref="Selectable"/> objects to facilitate save/load. 
/// However, in some 
/// situations (additional wall baseboards), we do not want to 
/// track the object as it will be automatically instantiated 
/// by something else on awake. Add this component to any 
/// object with a <see cref="TrackedObject"/> component to 
/// remove it on Awake and also to prevent it from being added
/// to the selectable by 
/// <see cref="Selectable.OnPreprocessAssetBundle"/>
/// </summary>
public class RemoveTrackedObject : MonoBehaviour
{
    private void Awake()
    {
        if (TryGetComponent<TrackedObject>(out var comp))
        {
            Destroy(comp);
        }
    }
}
