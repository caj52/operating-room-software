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

    public static void CycleCam()
    {
        var activeCam = (CinemachineVirtualCamera)CinemachineCore
            .Instance.GetActiveBrain(0).ActiveVirtualCamera;

        activeCam.Priority = 0;
        int indexOfActiveCam = _cameras.IndexOf(activeCam);
        int nextCam = indexOfActiveCam + 1;

        if (nextCam >= _cameras.Count) 
            nextCam = 0;

        _cameras[nextCam].Priority = 11;
        ActiveCamera = _cameras[nextCam];
        CameraChanged?.Invoke();
    }
}