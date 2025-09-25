using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class UI_ToggleClearanceLines : MonoBehaviour
{
    public static UnityEvent ClearanceLinesToggled { get; } = new();
    public static bool IsActive { get; private set; }

    // Holds GameObjects that should mirror the clearance lines on/off state
    private static readonly HashSet<GameObject> RegisteredTargets = new();

    public static void RegisterTarget(GameObject go)
    {
        if (go == null) return;
        RegisteredTargets.Add(go);
        // Ensure the target immediately reflects the current toggle state
        go.SetActive(IsActive);
    }

    public static void UnregisterTarget(GameObject go)
    {
        if (go == null) return;
        RegisteredTargets.Remove(go);
    }

    public void ToggleClearanceLines(bool isOn)
    {
        IsActive = isOn;
        // First notify listeners (so they can do cleanup before deactivation)
        ClearanceLinesToggled?.Invoke();

        // Then enforce active state on registered targets
        if (RegisteredTargets.Count > 0)
        {
            // Use a temp list to avoid collection modification during iteration if some targets are destroyed
            var toRemove = new List<GameObject>();
            foreach (var go in RegisteredTargets)
            {
                if (go == null)
                {
                    toRemove.Add(go);
                    continue;
                }
                go.SetActive(isOn);
            }
            // Cleanup any null entries
            foreach (var dead in toRemove) RegisteredTargets.Remove(dead);
        }

        Debug.Log($"Clearance lines enabled = {IsActive}");
    }
}