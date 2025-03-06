using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;

public class OrbitCamera : MonoBehaviour
{
    public static OrbitCamera Instance { get; private set; }

    [field: SerializeField] 
    public CinemachineVirtualCamera VirtualCamera { get; private set; }

    [field: SerializeField] 
    public float minFOV { get; private set; }

    [field: SerializeField] 
    public float maxFOV { get; private set; }

    [field: SerializeField] 
    public OrbitMode orbitMode { get; private set; } = OrbitMode.FreeMove;

    [field: SerializeField] 
    public GameObject orbitTarget { get; private set; }

    private Rigidbody orbitRigidBody;
    private Vector3 previousPosition;

    public void Awake()
    {
        Instance = this;
        orbitTarget.transform.position = new Vector3(0, 0, 0);
        orbitRigidBody = orbitTarget.GetComponent<Rigidbody>();
        VirtualCamera.LookAt = orbitTarget.transform;

        CameraManager.Register(VirtualCamera);

        RoomSize.RoomSizeChanged.AddListener(ResetPosition);
        Selectable.SelectionChanged += UpdateTarget;
    }

    private void OnDestroy()
    {
        RoomSize.RoomSizeChanged.RemoveListener(ResetPosition);
        Selectable.SelectionChanged -= UpdateTarget;
        CameraManager.Unregister(VirtualCamera);
    }

    private void ResetPosition(RoomDimension roomDimension)
    {
        orbitTarget.transform.position = new Vector3(0, 0, 0);
    }

    private void Update()
    {
        if (!RoomSize.Bounds.Contains(orbitTarget.transform.position))
        {
            orbitTarget.transform.position = RoomSize.Bounds
                .ClosestPoint(orbitTarget.transform.position);
        }
    }

    private void FixedUpdate()
    {
        if (CameraManager.ActiveCamera != VirtualCamera 
            || GizmoHandler.GizmoBeingUsed) 
        {
            return;
        }
            
        float h = Input.GetAxis("Mouse X");
        float v = Input.GetAxis("Mouse Y");

        if (Input.GetMouseButton(0))
        {
            orbitTarget.transform.Rotate(new Vector3(v, h, 0));

            Vector3 currentRot = orbitTarget.transform.localRotation.eulerAngles;
            currentRot.x = ClampAngle(currentRot.x, -54, 32);
            currentRot.z = Mathf.Clamp(currentRot.z, 0, 0);
            orbitTarget.transform.localRotation = Quaternion.Euler(currentRot);
        }

        if (Input.GetMouseButton(1))
        {
            if (orbitMode == OrbitMode.SelectableLocked 
                && Selectable.SelectedSelectables.Count > 0) 
                return;

            orbitRigidBody.AddRelativeForce(new Vector3(h, 0, v), ForceMode.Impulse);
        }

        if (Input.GetMouseButtonUp(1))
        {
            orbitRigidBody.velocity = Vector3.zero;
        }

        float fov = VirtualCamera.m_Lens.OrthographicSize;
        fov += -Input.GetAxis("Mouse ScrollWheel") * 0.5f;
        fov = Mathf.Clamp(fov, minFOV, maxFOV);
        VirtualCamera.m_Lens.OrthographicSize = fov;
    }

    private void UpdateTarget()
    {
        if (orbitMode != OrbitMode.SelectableLocked) 
            return;

        if (Selectable.SelectedSelectables.Count == 0)
        {
            if (previousPosition == null) 
                previousPosition = Vector3.zero;

            orbitTarget.transform.position = previousPosition;
        }
        else
        {
            previousPosition = orbitTarget.transform.position;

            orbitTarget.transform.position = Selectable.SelectedSelectables[0].transform.position;
        }
    }

    public float ClampAngle(float angle, float min, float max)
    {
        if (angle < 90 || angle > 270) // critic zone
        {
            // convert all angles to -180 to +180
            if (angle > 180) 
                angle -= 360; 

            if (max > 180) 
                max -= 360;

            if (min > 180) 
                min -= 360;
        }

        angle = Mathf.Clamp(angle, min, max);

        // if angle is negative, convert to 0 to 360
        if (angle < 0) 
            angle += 360; 

        return angle;
    }
}

public enum OrbitMode
{
    SelectableLocked,
    FreeMove,
    Locked
}