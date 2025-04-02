using RTG;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class MeasurementText : MonoBehaviour
{
    private static GameObject Prefab { get; set; }

    [SerializeField, ReadOnly] private Measurer _measurer;
    public TextMeshProUGUI Text; 

    private void Awake()
    {
        if (Prefab == null)
        {
            Prefab = gameObject;
            gameObject.SetActive(false);
            return;
        }

        Measurable.ActiveMeasurablesChanged.AddListener(CheckActiveState);
        Text = GetComponent<TextMeshProUGUI>();
    }

    private void OnDestroy()
    {
        Measurable.ActiveMeasurablesChanged.RemoveListener(CheckActiveState);
    }

    public static MeasurementText GetMeasurementText(Measurer measurer)
    {
        var newObj = Instantiate(Prefab, Prefab.transform.parent);
        MeasurementText measurementText = newObj.GetComponent<MeasurementText>();
        measurementText._measurer = measurer;
        return measurementText;
    }

    public void CheckActiveState()
    {
        gameObject.SetActive(_measurer.Measurement.Measurable.IsActive);
    }

    public void RotateTowardCamera(Camera camera = null)
    {
        if (camera == null)
        {
            camera = Camera.main;
        }

        var quat = GetRotationTowardCamera(camera);
        transform.SetPositionAndRotation(transform.position, quat);
      
        float angleOfMeasurer = Vector3.Angle(_measurer.transform.forward, Vector3.up);
        float angle2 = Vector3.Angle(_measurer.transform.forward, Vector3.down);
        if (angleOfMeasurer < 10f || angle2 < 10f)
        {
            transform.Rotate(0, 0, 90);
        }
    }

    public Quaternion GetRotationTowardCamera(Camera camera = null)
    {
        if (camera == null)
        {
            camera = Camera.main;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        Plane nearFrustrumPlane = planes[4];

        Vector3 planePoint = nearFrustrumPlane.ClosestPointOnPlane(transform.position);
        var vec = transform.position - planePoint;
        vec = vec.normalized;

        return Quaternion.LookRotation(vec);
    }

    public void Update()
    {
        UpdateVisibilityAndPosition();
    }

    public void UpdateVisibilityAndPosition(Camera camera = null, bool force = false)
    {
        if (camera == null)
        {
            camera = Camera.main;
        }

        Text.text = _measurer.Distance;

        // Get the direction from camera to text position
        Vector3 directionFromCamera = (_measurer.TextPosition - camera.transform.position).normalized;

        // Calculate dynamic buffer distance based on distance from camera
        float distanceToCamera = Vector3.Distance(camera.transform.position, _measurer.TextPosition);
        float bufferDistance = -0.5f * (distanceToCamera * 0.1f); // Scale buffer with distance

        // Clamp the buffer to reasonable min/max values
        bufferDistance = Mathf.Clamp(bufferDistance, -2.0f, -0.1f);

        // Apply the buffered position
        Vector3 bufferedPosition = _measurer.TextPosition + directionFromCamera * bufferDistance;

        // Offset in the Y axis to position the text a bit lower
        bufferedPosition.y -= 0.18f; // Adjust this value based on your scene scale

        // Apply the buffered position
        transform.position = bufferedPosition;

        RotateTowardCamera(camera);
    }
}
