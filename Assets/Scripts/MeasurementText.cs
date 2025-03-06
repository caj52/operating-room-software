using RTG;
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
        transform.position = _measurer.TextPosition;
        RotateTowardCamera(camera);

        //if (Selectable.IsInElevationPhotoMode)
        //{
        //    Debug.Log($"Successfully rotated measurer text toward camera, active state is {gameObject.activeSelf}");
        //}
    }
}
