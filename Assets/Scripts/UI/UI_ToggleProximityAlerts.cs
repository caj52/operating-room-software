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

    /// <summary>
    /// Nested suppress for elev PDF capture: silences alerts without flipping the UI toggle.
    /// Restores the pre-suppress <see cref="IsActive"/> when the outermost End returns.
    /// </summary>
    static int _captureSuppressDepth;
    static bool _activeBeforeCaptureSuppress;

    public static void BeginCaptureSuppress()
    {
        if (_captureSuppressDepth++ == 0)
        {
            _activeBeforeCaptureSuppress = IsActive;
            IsActive = false;
        }
    }

    public static void EndCaptureSuppress()
    {
        if (_captureSuppressDepth <= 0)
            return;
        if (--_captureSuppressDepth == 0)
            IsActive = _activeBeforeCaptureSuppress;
    }

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
        bool desired = toggle != null && toggle.isOn;
        if (_captureSuppressDepth > 0)
        {
            // Capture owns IsActive; remember the user's choice for restore.
            _activeBeforeCaptureSuppress = desired;
            ProximityAlertsToggled?.Invoke();
            Debug.Log($"ProximityAlerts toggle deferred (elev capture); will restore to {desired}");
            return;
        }

        IsActive = desired;

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
