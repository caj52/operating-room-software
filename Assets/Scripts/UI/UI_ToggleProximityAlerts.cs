using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UI_ToggleProximityAlerts : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    public Toggle toggle;
    public void ToggleProximityAlerts()
    {
        IsActive = toggle.isOn;
        Debug.Log($"ProximityAlerts = {IsActive}");
    }
}
