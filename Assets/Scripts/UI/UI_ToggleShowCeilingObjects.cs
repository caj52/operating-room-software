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
            bool orthoCeilingCamActive = CameraManager.ActiveCamera.GetComponent<OperatingRoomCamera>().CameraType == OperatingRoomCameraType.OrthoCeiling;
            ToggleShowCeilingObjects(false);
            _toggle.isOn = false;
            gameObject.SetActive(orthoCeilingCamActive);
        });

        ObjectMenu.ActiveStateChanged.AddListener(() =>
        {
            bool orthoCeilingCamActive = CameraManager.ActiveCamera != null && CameraManager.ActiveCamera.GetComponent<OperatingRoomCamera>().CameraType == OperatingRoomCameraType.OrthoCeiling;
            if (orthoCeilingCamActive && !_toggle.isOn && ObjectMenu.Instance.gameObject.activeSelf)
            {
                _toggle.isOn = true;
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