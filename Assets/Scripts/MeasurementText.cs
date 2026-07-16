using RTG;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System.Globalization;

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


        // Metric elevation overlays already include "mm"; imperial still normalized.
        Text.text = _measurer.Distance != null && _measurer.Distance.IndexOf("mm", StringComparison.OrdinalIgnoreCase) >= 0
            ? _measurer.Distance
            : NormalizeFeetInches(_measurer.Distance);

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

    string NormalizeFeetInches(string input)
    {
        // Expected base format similar to: "1' 10.7\"" but be forgiving with spaces
        int footIndex = input.IndexOf('\''); // Find the index of the foot (') symbol
        int inchIndex = input.IndexOf('"'); // Find the index of the inch (") symbol

        if (footIndex == -1 || inchIndex == -1 || inchIndex <= footIndex)
            return input; // Return the input unmodified if the format is not as expected

        // Extract numbers (allow decimals in inches)
        string feetStr = input.Substring(0, footIndex).Trim(); // Extract and trim the feet part
        string inchesStr = input.Substring(footIndex + 1, inchIndex - footIndex - 1).Trim(); // Extract and trim the inches part

        // Try parsing the feet part as an integer
        if (!int.TryParse(feetStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int feet))
            return input; // Return unmodified if parsing fails

        // Try parsing the inches part as a float (to allow decimals)
        if (!float.TryParse(inchesStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float inches))
            return input; // Return unmodified if parsing fails

        // Convert to total inches and normalize
        float totalInches = feet * 12f + inches; // Convert the entire measurement to inches
        // Round to a single decimal overall, then split into feet/inches
        totalInches = (float)Math.Round(totalInches, 1, MidpointRounding.AwayFromZero);

        int normFeet = Mathf.FloorToInt(totalInches / 12f); // Calculate normalized feet
        float normInches = totalInches - normFeet * 12f; // Calculate normalized inches

        // Guard against rounding up to exactly 12.0"
        if (normInches >= 12f)
        {
            normFeet += 1;
            normInches -= 12f;
        }

        return $"{normFeet}' {normInches:0.0}\""; // Return in the format of "X' Y.Z\""
    }

}
