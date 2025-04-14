using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FreeLookCam : MonoBehaviour
{
    public static FreeLookCam Instance { get; private set; }

    [field: HideInInspector] public bool isLocked = false;
    [field: SerializeField] private Transform Head { get; set; }
    [field: SerializeField] private float LookSensitivityX { get; set; } = 60f;
    [field: SerializeField] private float LookSensitivityY { get; set; } = 33.75f;
    [field: SerializeField] private Rigidbody Rigidbody { get; set; }
    [field: SerializeField] public CinemachineVirtualCamera VirtualCamera { get; private set; }
    private Collider _collider;
    public static bool IsActive => (CinemachineVirtualCamera)CinemachineCore.Instance.GetActiveBrain(0).ActiveVirtualCamera == Instance.VirtualCamera;

    private bool _noRotating;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
  
        _collider = GetComponent<Collider>();
        CameraManager.Register(VirtualCamera);
        CameraManager.SetActiveCamera(VirtualCamera);
        CameraManager.CameraChanged.AddListener(() =>
        {
            bool active = CameraManager.ActiveCamera == VirtualCamera;
            _collider.enabled = active;
            Rigidbody.isKinematic = !active;
        });
    }

    private void OnDestroy()
    {
        CameraManager.Unregister(VirtualCamera);
    }

    private void Update()
    {
        if (!IsActive || FullScreenMenu.IsOpen) return;

        if (Input.GetMouseButtonDown(0) && InputHandler.IsPointerOverUIElement())
        {
            _noRotating = true;
            return;
        }

        if (_noRotating) 
        {
            if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
                _noRotating = false;
        }

        if (!_noRotating && Input.GetMouseButton(0))
        {
            HandleRotation();
        }

      /*  if (!RoomSize.Bounds.Contains(transform.position))
        {
            transform.position = RoomSize.Bounds.ClosestPoint(transform.position);
        }*/

        //if (transform.position.y < 10)
        //{
        //    transform.position = Vector3.zero;
        //}
    }

    private void FixedUpdate()
    {
        if (!IsActive || FullScreenMenu.IsOpen || isLocked) 
            return;

        HandleMovement();
    }

    public void OnCameraLive()
    {
        RoomBoundary.EnableAllMeshRenderersAndColliders();
    }

    private void HandleMovement()
    {
        Vector3 velVector = new Vector3(0, Rigidbody.velocity.y, 0);

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            velVector += transform.forward;
        }
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S))
        {
            velVector -= transform.forward;
        }
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A))
        {
            velVector -= transform.right;
        }
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D))
        {
            velVector += transform.right;
        }

        Rigidbody.velocity = velVector;
    }

    private void HandleRotation()
    {
        if (GizmoHandler.GizmoBeingUsed || _noRotating) return;

        if (_skipNextMouseInput)
        {
            _skipNextMouseInput = false;
            return; // Skip this frame to avoid jerk
        }

        transform.Rotate(new Vector3(0, InputHandler.MouseDeltaScreenPercentage.x * LookSensitivityX, 0));
        Head.transform.Rotate(new Vector3(-InputHandler.MouseDeltaScreenPercentage.y * LookSensitivityY, 0, 0));

        var signedAngle = Vector3.SignedAngle(transform.forward, Head.forward, transform.right);

        if (signedAngle > 70)
        {
            Head.transform.localEulerAngles = new Vector3(70, 0, 0);
        }

        if (signedAngle < -70)
        {
            Head.transform.localEulerAngles = new Vector3(-70, 0, 0);
        }
    }



    private bool _skipNextMouseInput = false;

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            _skipNextMouseInput = true;
        }
    }

}
