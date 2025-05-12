using UnityEngine;
using UnityEngine.EventSystems;

public class UI_CameraLockOnInput : MonoBehaviour
{
    

    void Update()
    {
        if (IsInputFieldFocused())
        {
            if (FreeLookCam.IsActive)
                FreeLookCam.Instance.isLocked = true;
        }
        else
        {
            if (FreeLookCam.IsActive)
                FreeLookCam.Instance.isLocked = false;
        }
    }

    bool IsInputFieldFocused()
    {
        // Check if the currently selected UI element is an InputField or TMP_InputField
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
        {
            return EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null ||
                   EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null;
        }
        return false;
    }
}
