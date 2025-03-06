using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Is read by <see cref="ConfigurationManager"/> during 
/// room/arm loads. Destroyed during 
/// <see cref="ConfigurationManager.ProcessTrackedObjects"/>.
/// This is to prevent duplicate objects on load, namely
/// objecst with <see cref="Selectable.CanPlaceAnywhere"/>
/// and baseboards/wall protectors of additional wall 
/// selectables
/// </summary>
public class DestroyOnLoad : MonoBehaviour
{
}
