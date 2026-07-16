using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class CameraManager
{
    public static UnityEvent CameraChanged = new();
    private static List<CinemachineVirtualCamera> _cameras = new();
    public static CinemachineVirtualCamera ActiveCamera { get; private set; }

    public static void Register(CinemachineVirtualCamera cam)
    {
        _cameras.Add(cam);
    }

    public static void Unregister(CinemachineVirtualCamera cam) 
    { 
        _cameras.Remove(cam);
    }

    public static void SetActiveCamera(CinemachineVirtualCamera cam)
    {
       ActiveCamera = cam;
    }

    private static int _homeCamIndex = -1;

    public static void CycleCam()
    {
        if (_cameras == null || _cameras.Count == 0)
            return;

        var brain = CinemachineCore.Instance?.GetActiveBrain(0);
        var activeCam = brain != null
            ? brain.ActiveVirtualCamera as CinemachineVirtualCamera
            : null;

        // Remember FreeLook (or first registered cam) as home so cycling can return.
        if (_homeCamIndex < 0 || _homeCamIndex >= _cameras.Count)
        {
            _homeCamIndex = 0;
            for (int i = 0; i < _cameras.Count; i++)
            {
                var orCam = _cameras[i] != null
                    ? _cameras[i].GetComponent<OperatingRoomCamera>()
                    : null;
                if (orCam != null && orCam.CameraType == OperatingRoomCameraType.FreeLook)
                {
                    _homeCamIndex = i;
                    break;
                }
            }
        }

        int indexOfActiveCam = activeCam != null ? _cameras.IndexOf(activeCam) : -1;
        int nextCam = indexOfActiveCam + 1;
        if (nextCam >= _cameras.Count)
            nextCam = _homeCamIndex >= 0 ? _homeCamIndex : 0;

        // Reset all priorities so a cam can't get "stuck" elevated.
        for (int i = 0; i < _cameras.Count; i++)
        {
            if (_cameras[i] != null)
                _cameras[i].Priority = 0;
        }

        if (_cameras[nextCam] == null)
            return;

        _cameras[nextCam].Priority = 11;
        ActiveCamera = _cameras[nextCam];
        CameraChanged?.Invoke();
    }
}