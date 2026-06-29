using System.Collections.Generic;
using UnityEngine;

public class FlyCamera : MonoBehaviour
{
    public float mainSpeed = 5.0f; // Regular speed
    public float shiftAdd = 25f; // Multiplied by how long shift is held. Basically running
    public float maxShift = 100.0f; // Maximum speed when holding shift
    public float camSens = 0.25f; // Mouse sensitivity
    public bool rotateOnlyIfMousedown = true;
    public bool movementStaysFlat = false;

    private Vector3 lastMouse;
    private float totalRun = 1.0f;
    private float yaw = 0.0f;
    private float pitch = 0.0f;
    private bool isRotating = false;
    private readonly List<Selectable> _ceilingSelectables = new();
    private bool _wasAboveCeiling;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;
        lastMouse = Input.mousePosition;

        RebuildCeilingList();
        Selectable.ActiveSelectablesInSceneChanged.AddListener(RebuildCeilingList);
    }

    private void OnDestroy()
    {
        Selectable.ActiveSelectablesInSceneChanged.RemoveListener(RebuildCeilingList);
    }

    private void RebuildCeilingList()
    {
        _ceilingSelectables.Clear();
        if (Selectable.ActiveSelectables == null) return;

        foreach (var selectable in Selectable.ActiveSelectables)
        {
            if (selectable.name.ToLower().Contains("roomboundary_ceil"))
                _ceilingSelectables.Add(selectable);
        }
    }

    void Update()
    {
        HandleMouseLook();
        HandleMovement();
    }

    void HandleMouseLook()
    {
        if (rotateOnlyIfMousedown)
        {
            if (Input.GetMouseButtonDown(0))
            {
                isRotating = true;
                lastMouse = Input.mousePosition;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                isRotating = false;
            }
        }

        if (!isRotating) return;

        Vector3 mouseDelta = Input.mousePosition - lastMouse;
        lastMouse = Input.mousePosition;

        yaw += mouseDelta.x * camSens;
        pitch -= mouseDelta.y * camSens;
        pitch = Mathf.Clamp(pitch, -89f, 89f); // Prevent flipping

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        UpdateCeilingVisibility();
    }

    private void UpdateCeilingVisibility()
    {
        var cam = transform.GetComponent<Camera>();
        if (cam == null) return;

        if (!cam.enabled)
        {
            if (_wasAboveCeiling)
            {
                _wasAboveCeiling = false;
                ChangeCeilingLayer(_ceilingSelectables, "Wall");
            }
            return;
        }

        bool aboveCeiling = transform.position.y > 10f;
        if (aboveCeiling == _wasAboveCeiling)
            return;

        _wasAboveCeiling = aboveCeiling;
        ChangeCeilingLayer(_ceilingSelectables, aboveCeiling ? "HiddenCeilings" : "Wall");
    }

    void HandleMovement()
    {
        Vector3 p = GetBaseInput();

        if (Input.GetKey(KeyCode.LeftShift))
        {
            totalRun += Time.deltaTime;
            p *= totalRun * shiftAdd;
            p = Vector3.ClampMagnitude(p, maxShift);
        }
        else
        {
            totalRun = Mathf.Clamp(totalRun * 0.5f, 1f, 1000f);
            p *= mainSpeed;
        }

        p *= Time.deltaTime;
        transform.Translate(p, Space.Self);
        UpdateCeilingVisibility();
    }

    private Vector3 GetBaseInput()
    {
        Vector3 p_Velocity = Vector3.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) p_Velocity += Vector3.forward;
        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) p_Velocity += Vector3.back;
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) p_Velocity += Vector3.left;
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) p_Velocity += Vector3.right;
        if (Input.GetKey(KeyCode.Q)) p_Velocity += Vector3.down;
        if (Input.GetKey(KeyCode.E)) p_Velocity += Vector3.up;
        return p_Velocity;
    }


    public void ChangeCeilingLayer(List<Selectable> ceilings, string layerName)
    {
        int newLayer = LayerMask.NameToLayer(layerName);
        if (newLayer == -1)
        {
            Debug.LogError($"Layer '{layerName}' not found! Make sure it exists in the Layer settings.");
            return;
        }

        foreach (var item in ceilings)
        {
            ChangeLayerRecursively(item.gameObject, newLayer);
        }
    }

    void ChangeLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            ChangeLayerRecursively(child.gameObject, layer);
        }
    }
}
