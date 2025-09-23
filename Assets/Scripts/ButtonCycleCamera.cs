using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;

public class ButtonCycleCamera : MonoBehaviour
{
    [Range(0f,1f)] public float transparentCeilingAlpha = 0.1f; // alpha for non-FreeLook cams
    [Range(0f,1f)] public float freeLookCeilingAlpha = 1f;      // alpha when FreeLook is active (opaque)

    public void CycleCamera()
    {
        CameraManager.CycleCam();
        ApplyCeilingAlphaForActiveCamera();
    }

    private void OnEnable()
    {
        CameraManager.CameraChanged.AddListener(ApplyCeilingAlphaForActiveCamera);
    }

    private void OnDisable()
    {
        CameraManager.CameraChanged.RemoveListener(ApplyCeilingAlphaForActiveCamera);
    }

    private void ApplyCeilingAlphaForActiveCamera()
    {
        var activeCM = CameraManager.ActiveCamera;
        if (activeCM == null)
        {
            return;
        }
        var orCam = activeCM.GetComponent<OperatingRoomCamera>();
        if (orCam != null && orCam.CameraType == OperatingRoomCameraType.FreeLook)
        {
            RoomBoundary.SetCeilingsAlpha(freeLookCeilingAlpha);
        }
        else
        {
            RoomBoundary.SetCeilingsAlpha(transparentCeilingAlpha);
        }
    }
}
