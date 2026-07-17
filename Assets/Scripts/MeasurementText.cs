using RTG;
using System;
using System.Globalization;
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
            camera = Camera.main;

        var quat = GetRotationTowardCamera(camera);
        transform.SetPositionAndRotation(transform.position, quat);

        float angleOfMeasurer = Vector3.Angle(_measurer.transform.forward, Vector3.up);
        float angle2 = Vector3.Angle(_measurer.transform.forward, Vector3.down);
        if (angleOfMeasurer < 10f || angle2 < 10f)
            transform.Rotate(0, 0, 90);
    }

    public Quaternion GetRotationTowardCamera(Camera camera = null)
    {
        if (camera == null)
            camera = Camera.main;

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        Plane nearFrustrumPlane = planes[4];

        Vector3 planePoint = nearFrustrumPlane.ClosestPointOnPlane(transform.position);
        var vec = (transform.position - planePoint).normalized;
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
            // During elevation capture, Camera.main is wrong — use the elevation RT camera.
            if (Selectable.IsInElevationPhotoMode && Selectable.ActiveCameraRenderTextureElevation != null)
                camera = Selectable.ActiveCameraRenderTextureElevation;
            else
                camera = Camera.main;
        }

        // Metric elevation overlays already include "mm"; imperial still normalized.
        Text.text = _measurer.Distance != null && _measurer.Distance.IndexOf("mm", StringComparison.OrdinalIgnoreCase) >= 0
            ? _measurer.Distance
            : NormalizeFeetInches(_measurer.Distance);

        if (Selectable.IsInElevationPhotoMode)
        {
            PlaceElevationText(camera);
            return;
        }

        Vector3 directionFromCamera = (_measurer.TextPosition - camera.transform.position).normalized;
        float distanceToCamera = Vector3.Distance(camera.transform.position, _measurer.TextPosition);
        float bufferDistance = Mathf.Clamp(-0.5f * (distanceToCamera * 0.1f), -2.0f, -0.1f);
        Vector3 bufferedPosition = _measurer.TextPosition + directionFromCamera * bufferDistance;
        bufferedPosition.y -= 0.18f;
        transform.position = bufferedPosition;
        RotateTowardCamera(camera);
    }

    /// <summary>
    /// Horizontal arm dims: text above the line.
    /// Vertical floor dims: text beside the line.
    /// Offset uses TMP glyph bounds — not the empty RectTransform (200×50), which was
    /// inventing ~12 cm of gap via world-space canvas scale.
    /// </summary>
    private void PlaceElevationText(Camera camera)
    {
        const float gapMeters = 0.015f;

        if (Text != null)
        {
            var rt = Text.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        Vector3 onLine = _measurer.TextPosition;
        Vector3 dimDir = _measurer.transform.forward;
        if (dimDir.sqrMagnitude < 1e-8f)
            dimDir = Vector3.down;
        dimDir.Normalize();

        Vector3 camUp = camera.transform.up;
        Vector3 camRight = camera.transform.right;
        bool isHorizontalOnPage =
            Mathf.Abs(Vector3.Dot(dimDir, camRight)) >= Mathf.Abs(Vector3.Dot(dimDir, camUp));
        Vector3 perp = isHorizontalOnPage ? camUp : camRight;
        if (perp.sqrMagnitude < 1e-8f)
            perp = Vector3.up;
        perp.Normalize();

        float halfExtent = 0f;
        transform.position = onLine;
        RotateTowardCamera(camera);
        if (Text != null)
        {
            Text.ForceMeshUpdate();
            Bounds glyphBounds = Text.textBounds;
            Vector3 c = glyphBounds.center;
            Vector3 e = glyphBounds.extents;
            Vector3[] localCorners =
            {
                c + new Vector3(-e.x, -e.y, 0f),
                c + new Vector3(-e.x,  e.y, 0f),
                c + new Vector3( e.x, -e.y, 0f),
                c + new Vector3( e.x,  e.y, 0f),
            };

            float minAlong = float.MaxValue;
            float maxAlong = float.MinValue;
            var rt = Text.rectTransform;
            for (int i = 0; i < 4; i++)
            {
                float d = Vector3.Dot(rt.TransformPoint(localCorners[i]) - onLine, perp);
                minAlong = Mathf.Min(minAlong, d);
                maxAlong = Mathf.Max(maxAlong, d);
            }

            halfExtent = Mathf.Max(0f, (maxAlong - minAlong) * 0.5f);
        }

        transform.position = onLine + perp * (halfExtent + gapMeters);
        RotateTowardCamera(camera);
    }

    string NormalizeFeetInches(string input)
    {
        int footIndex = input.IndexOf('\'');
        int inchIndex = input.IndexOf('"');

        if (footIndex == -1 || inchIndex == -1 || inchIndex <= footIndex)
            return input;

        string feetStr = input.Substring(0, footIndex).Trim();
        string inchesStr = input.Substring(footIndex + 1, inchIndex - footIndex - 1).Trim();

        if (!int.TryParse(feetStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int feet))
            return input;
        if (!float.TryParse(inchesStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float inches))
            return input;

        float totalInches = feet * 12f + inches;
        totalInches = (float)Math.Round(totalInches, 1, MidpointRounding.AwayFromZero);

        int normFeet = Mathf.FloorToInt(totalInches / 12f);
        float normInches = totalInches - normFeet * 12f;
        if (normInches >= 12f)
        {
            normFeet += 1;
            normInches -= 12f;
        }

        return $"{normFeet}' {normInches:0.0}\"";
    }
}
