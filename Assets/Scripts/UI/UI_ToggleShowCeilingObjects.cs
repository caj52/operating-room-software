using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UI_ToggleShowCeilingObjects : MonoBehaviour
{
    public static UnityEvent CeilingObjectVisibilityToggled { get; } = new();
    public static bool ShowCeilingObjects { get; private set; } = true;
    private Toggle _toggle;

    private void Awake()
    {
        _toggle = GetComponent<Toggle>();   
        CameraManager.CameraChanged.AddListener(() =>
        {
            if (CameraManager.ActiveCamera == null) return;
            bool orthoCeilingCamActive = CameraManager.ActiveCamera.GetComponent<OperatingRoomCamera>().CameraType == OperatingRoomCameraType.OrthoCeiling;
            // Only show toggle when ortho ceiling cam is active, but do NOT force-disable ceiling objects
            gameObject.SetActive(orthoCeilingCamActive);
            if (orthoCeilingCamActive)
            {
                // Reflect current state without changing it
                _toggle.isOn = ShowCeilingObjects;
            }
        });

        ObjectMenu.ActiveStateChanged.AddListener(() =>
        {
            if (CameraManager.ActiveCamera == null) return;
            bool orthoCeilingCamActive = CameraManager.ActiveCamera.GetComponent<OperatingRoomCamera>().CameraType == OperatingRoomCameraType.OrthoCeiling;
            if (orthoCeilingCamActive && ObjectMenu.Instance.gameObject.activeSelf)
            {
                // Keep toggle state as-is; no auto forcing on/off
                _toggle.isOn = ShowCeilingObjects;
            }
        });

        gameObject.SetActive(false);
    }

    public void ToggleShowCeilingObjects(bool isOn)
    {
        ShowCeilingObjects = isOn;
        CeilingObjectVisibilityToggled?.Invoke();
    }
}