using UnityEngine;
using Cinemachine;

public class FreezeCameraOnFocusLoss : MonoBehaviour
{
    public bool regainedFocus = false;
    public bool clickIgnored = false;

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            regainedFocus = true;
            clickIgnored = false;
        }
    }

    void Update()
    {
        if (OperatingRoomCamera.LiveCamera)
        {
            if (OperatingRoomCamera.LiveCamera.CameraType == OperatingRoomCameraType.OrthoCeiling)
            {

                if (regainedFocus && !clickIgnored)
                {
                    if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
                    {
                        // First click after focus regain is ignored
                        clickIgnored = true;
                        // Optional: Reset input axes to avoid delta spikes
                        Input.ResetInputAxes();
                        Debug.Log("Mouse click ignored after focus regain.");
                    }
                }

                // After first click, go back to normal behavior
                if (clickIgnored)
                {
                    regainedFocus = false;
                }

            }

        }

    }
}
