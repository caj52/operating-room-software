using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UI_ToggleProximityAlerts : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    // Event for listeners that need to respond to toggle changes
    public static UnityEvent ProximityAlertsToggled { get; } = new UnityEvent();

    // Optional registry of targets that should mirror this toggle's active state
    private static readonly HashSet<GameObject> RegisteredTargets = new HashSet<GameObject>();

    public static void RegisterTarget(GameObject go)
    {
        if (go == null) return;
        RegisteredTargets.Add(go);
        go.SetActive(IsActive);
    }

    public static void UnregisterTarget(GameObject go)
    {
        if (go == null) return;
        RegisteredTargets.Remove(go);
    }

    public Toggle toggle;
    public void ToggleProximityAlerts()
    {
        IsActive = toggle != null && toggle.isOn;

        // Notify listeners first
        ProximityAlertsToggled?.Invoke();

        // Then enforce state on registered targets
        if (RegisteredTargets.Count > 0)
        {
            var toRemove = new List<GameObject>();
            foreach (var go in RegisteredTargets)
            {
                if (go == null) { toRemove.Add(go); continue; }
                go.SetActive(IsActive);
            }
            foreach (var dead in toRemove) RegisteredTargets.Remove(dead);
        }

        Debug.Log($"ProximityAlerts = {IsActive}");
    }
}
