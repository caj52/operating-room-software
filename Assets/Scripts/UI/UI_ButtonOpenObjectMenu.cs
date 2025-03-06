using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UI_ButtonOpenObjectMenu : MonoBehaviour
{
    private void Awake()
    {
        CameraManager.CameraChanged.AddListener(UpdateVisibility);
        UI_ToggleShowCeilingObjects.CeilingObjectVisibilityToggled.AddListener(UpdateVisibility);
        ObjectMenu.LastOpenedSelectableChanged.AddListener(UpdateVisibility);
        Selectable.ActiveSelectablesInSceneChanged.AddListener(UpdateVisibility);
    }

    private void OnDestroy()
    {
        CameraManager.CameraChanged.RemoveListener(UpdateVisibility);
        UI_ToggleShowCeilingObjects.CeilingObjectVisibilityToggled.RemoveListener(UpdateVisibility);
        ObjectMenu.LastOpenedSelectableChanged.RemoveListener(UpdateVisibility);
        Selectable.ActiveSelectablesInSceneChanged.RemoveListener(UpdateVisibility);
    }

    private void UpdateVisibility()
    {
        gameObject.SetActive(CanBeVisible());
    }

    private bool CanBeVisible()
    {
        if (SceneManager.GetActiveScene().name == "ObjectEditor")
        {
            return Selectable.ActiveSelectables.Count == 0;
        }

        if (CameraManager.ActiveCamera == null) return false;

        bool isOrthoCeilingCam = CameraManager.ActiveCamera.GetComponent<OperatingRoomCamera>().CameraType == OperatingRoomCameraType.OrthoCeiling;
        bool isFreeLookCam = CameraManager.ActiveCamera == FreeLookCam.Instance.VirtualCamera;
        bool isOrbitalCam = CameraManager.ActiveCamera.GetComponent<OperatingRoomCamera>().CameraType == OperatingRoomCameraType.Orbital;

        return isFreeLookCam || isOrthoCeilingCam || isOrbitalCam;
    }

    public void OpenObjectMenu()
    {
      //  GetHierarchyObjects.instance.toggleSliderPanelAndClearList(false);
        ObjectMenu.Open();
    }
}
