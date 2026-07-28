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

    private Material _defaultSharedMaterial;
    private Material _elevationSharedMaterial;
    private bool _elevationStyleApplied;

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
        if (_elevationSharedMaterial != null)
            Destroy(_elevationSharedMaterial);
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
        // Elevation capture builds labels explicitly in ElevationCutsheetPass.
        // ActiveMeasurablesChanged must never revive imperial / wall canvas texts mid-capture.
        if (Selectable.IsInElevationPhotoMode)
        {
            if (_measurer == null || !_measurer.ShouldDrawInElevationPhoto())
                gameObject.SetActive(false);
            return;
        }

        bool active = _measurer != null
            && _measurer.Measurement?.Measurable != null
            && _measurer.Measurement.Measurable.IsActive;

        gameObject.SetActive(active);
    }

    /// <summary>True when this label is allowed to remain visible for elevation RT capture.</summary>
    public bool ShouldRemainVisibleInElevationCapture()
    {
        if (_measurer == null || !_measurer.gameObject.activeInHierarchy)
            return false;
        if (!_measurer.ShouldDrawInElevationPhoto())
            return false;
        // Must be metric cutsheet text — never keep imperial leftovers / partial glyphs.
        string d = _measurer.Distance;
        return !string.IsNullOrEmpty(d)
            && d.IndexOf("mm", StringComparison.OrdinalIgnoreCase) >= 0;
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
        if (_measurer == null)
        {
            gameObject.SetActive(false);
            return;
        }

        if (Text == null)
            Text = GetComponent<TextMeshProUGUI>();
        if (Text == null)
        {
            gameObject.SetActive(false);
            return;
        }

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
            if (!_measurer.ShouldDrawInElevationPhoto())
            {
                gameObject.SetActive(false);
                return;
            }

            // Never show stale imperial leftovers on cutsheets.
            if (Text.text != null && Text.text.IndexOf('\'') >= 0
                && Text.text.IndexOf("mm", StringComparison.OrdinalIgnoreCase) < 0)
            {
                gameObject.SetActive(false);
                return;
            }

            ApplyElevationTextStyle();
            PlaceElevationText(camera);
            gameObject.SetActive(true);
            return;
        }

        RestoreDefaultTextStyle();

        if (camera == null)
            return;

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
    /// One gap for all cutsheet labels — see ElevationDimPlacement.LabelGapMeters.
    /// </summary>
    private void PlaceElevationText(Camera camera)
    {
        bool isFloor = _measurer?.Measurement?.MeasurementType == MeasurementType.Floor;
        int floorLane = isFloor ? Mathf.Max(0, _measurer.ElevationTextLane) : 0;

        Vector3 onLine = _measurer.TextPosition;
        Vector3 dimDir = _measurer.transform.forward;
        if (dimDir.sqrMagnitude < 1e-8f)
            dimDir = Vector3.down;

        transform.position = onLine;
        RotateTowardCamera(camera);
        ElevationDimPlacement.PlaceLabel(
            transform, Text, onLine, dimDir, camera, isFloor, floorLane);
        RotateTowardCamera(camera);
    }

    /// <summary>
    /// Elevation cutsheets need crisp black type. The shared MeasurementText prefab
    /// uses LiberationSans Drop Shadow (OUTLINE_ON + UNDERLAY_ON), which reads as
    /// doubled/fuzzy "800 mm" in orthographic PDF captures.
    /// </summary>
    private void ApplyElevationTextStyle()
    {
        if (Text == null || _elevationStyleApplied)
            return;

        _defaultSharedMaterial = Text.fontSharedMaterial;

        Material source = Text.font != null ? Text.font.material : Text.fontSharedMaterial;
        if (source == null)
            return;

        if (_elevationSharedMaterial == null)
            _elevationSharedMaterial = new Material(source);

        _elevationSharedMaterial.CopyPropertiesFromMaterial(source);
        // Prefer plain font face — Drop Shadow mat keywords read as doubled type in elev PDFs.
        _elevationSharedMaterial.DisableKeyword("OUTLINE_ON");
        _elevationSharedMaterial.DisableKeyword("UNDERLAY_ON");
        if (_elevationSharedMaterial.HasProperty("_OutlineWidth"))
            _elevationSharedMaterial.SetFloat("_OutlineWidth", 0f);
        if (_elevationSharedMaterial.HasProperty("_UnderlayDilate"))
            _elevationSharedMaterial.SetFloat("_UnderlayDilate", 0f);
        if (_elevationSharedMaterial.HasProperty("_UnderlayOffsetX"))
            _elevationSharedMaterial.SetFloat("_UnderlayOffsetX", 0f);
        if (_elevationSharedMaterial.HasProperty("_UnderlayOffsetY"))
            _elevationSharedMaterial.SetFloat("_UnderlayOffsetY", 0f);
        if (_elevationSharedMaterial.HasProperty("_UnderlaySoftness"))
            _elevationSharedMaterial.SetFloat("_UnderlaySoftness", 0f);

        Text.fontSharedMaterial = _elevationSharedMaterial;
        Text.outlineWidth = 0f;
        Text.fontStyle = FontStyles.Normal;
        _elevationStyleApplied = true;
    }

    private void RestoreDefaultTextStyle()
    {
        if (!_elevationStyleApplied || Text == null)
            return;

        if (_defaultSharedMaterial != null)
            Text.fontSharedMaterial = _defaultSharedMaterial;

        _elevationStyleApplied = false;
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
