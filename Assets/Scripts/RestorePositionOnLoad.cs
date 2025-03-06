using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Component that designates that an object should have its 
/// position restored on load. Used to allow free moving shelves 
/// (and other objects) to properly restore their position on load. 
/// Should be placed on the root object of a 
/// <see cref="Selectable"/> prefab
/// </summary>
public class RestorePositionOnLoad : MonoBehaviour
{
    /// <summary>
    /// Will be set back to null after a load event. 
    /// Should be set during a load by the 
    /// <see cref="ConfigurationManager"/>
    /// </summary>
    public Vector3? PositionToRestore;

    private void Awake()
    {
        ConfigurationManager.OnConfigurationLoadComplete.AddListener(SetPosition);
        ConfigurationManager.OnRoomLoadComplete.AddListener(SetPosition);
    }

    private void OnDestroy()
    {
        ConfigurationManager.OnConfigurationLoadComplete.RemoveListener(SetPosition);
        ConfigurationManager.OnRoomLoadComplete.RemoveListener(SetPosition);
    }

    private void SetPosition(GameObject gameObject = null)
    {
        SetPosition();
    }

    private void SetPosition()
    {
        // Might need to wait a few frames or check to make
        // sure that all selectables/attachment points on this object are initialized
        // if object initialization is ever async or takes more than 1 frame in Start

        if (PositionToRestore != null)
        {
            transform.position = (Vector3)PositionToRestore;
            PositionToRestore = null;
        }
    }
}