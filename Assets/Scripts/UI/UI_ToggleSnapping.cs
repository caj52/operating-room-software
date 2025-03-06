using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class UI_ToggleSnapping : MonoBehaviour
{
    public static bool SnappingEnabled { get; private set; } = true;
    public static UnityEvent SnappingToggled { get; } = new();

    public void ToggleSnapping(bool isOn)
    {
        SnappingEnabled = isOn;
        SnappingToggled?.Invoke();
    }
}
