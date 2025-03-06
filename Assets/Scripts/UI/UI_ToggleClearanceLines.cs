using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class UI_ToggleClearanceLines : MonoBehaviour
{
    public static UnityEvent ClearanceLinesToggled { get; } = new();
    public static bool IsActive { get; private set; }

    public void ToggleClearanceLines(bool isOn)
    {
        IsActive = isOn;
        ClearanceLinesToggled?.Invoke();
        Debug.Log($"Clearance lines enabled = {IsActive}");
    }
}