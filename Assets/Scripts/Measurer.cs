using NPOI.SS.Formula.Functions;
using RTG;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using static Measurable;

public class Measurer : MonoBehaviour
{
    //public static List<Measurer> Measurers = new List<Measurer>();
    private static GameObject Prefab { get; set; }
    public UnityEvent ActiveStateToggled = new();
    public UnityEvent VisibilityToggled = new();
    public MeshRenderer Renderer { get; private set; }
    public List<LineRenderer> LineRenderers { get; private set; } = new();
    public MeasurementText MeasurementText;
    //public bool AllowInElevationPhotoMode => Measurement != null && Measurement.Measurable.ArmAssemblyActiveInElevationPhotoMode && Measurement.MeasurementType == MeasurementType.ToArmAssemblyOrigin;
    public Measurable.Measurement Measurement { get; private set; }
    public string Distance { get; private set; } = string.Empty;
    private Transform _childTransform;
    public Vector3 TextPosition => _childTransform.position;
    public static bool Initialized { get; private set; }

    private void OnEnable()
    {
        if (MeasurementText != null)
            MeasurementText.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (MeasurementText != null)
            MeasurementText.gameObject.SetActive(false);
    }

    private void Update()
    {
        UpdateTransform();
    }

    public static Measurer GetMeasurer(Measurable.Measurement measurement)
    {
        var newObj = Instantiate(Prefab);
        var measurer = newObj.GetComponent<Measurer>();
        measurer.Measurement = measurement;
        return measurer;
    }

    private float TryGetArmLengthMetersFromHierarchy()
    {
        // Walk up the hierarchy to find a Selectable that represents an arm
        var parents = Measurement?.Measurable ?
            Measurement.Measurable.GetComponentsInParent<Selectable>(true) : null;

        if (parents == null || parents.Length == 0) return -1f;

        bool NameHas(Selectable s, string token) => s != null && s.gameObject.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        bool HasSize(Selectable s) => s != null && s.CurrentScaleLevel != null && s.CurrentScaleLevel.Size > 0f;

        // Support multiple common naming schemes for arm segments
        string[] bottomHints = { "BottomArm", "BoomSegment_2", "Segment_2", "Arm2", "LowerArm", "Lower_Arm" };
        string[] topHints    = { "TopArm", "BoomSegment_1", "Segment_1", "Arm1", "UpperArm", "Upper_Arm" };

        // Prefer distal (bottom/segment_2) if present, otherwise top/segment_1, else any ancestor with a size
        Selectable bottom = parents.FirstOrDefault(s => HasSize(s) && bottomHints.Any(h => NameHas(s, h)));
        Selectable top    = parents.FirstOrDefault(s => HasSize(s) && topHints.Any(h => NameHas(s, h)));
        Selectable armSel = bottom ?? top ?? parents.FirstOrDefault(HasSize);

        if (armSel != null)
        {
            // Size is in meters
            return armSel.CurrentScaleLevel.Size;
        }

        return -1f;
    }

    private bool IsBoomArmFromHierarchy()
    {
        var parents = Measurement?.Measurable ?
            Measurement.Measurable.GetComponentsInParent<Selectable>(true) : null;
        if (parents == null || parents.Length == 0) return false;
        return parents.Any(s => s != null && s.gameObject != null && s.gameObject.name.IndexOf("Boom", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public void UpdateTransform(Camera camera = null)
    {
        if (camera == null)
        {
            camera = Camera.main;
        }

        transform.position = Measurement.Origin;
        transform.LookAt(Measurement.HitPoint);

        // Prefer world-space span for overlays (matches drawn ray). Use configured arm
        // length only when it closely matches world (avoids radial/half-scale drift).
        float worldMeters = Vector3.Distance(Measurement.Origin, Measurement.HitPoint);
        float distanceMeters = worldMeters;
        if (Measurement != null
            && Measurement.MeasurementType == MeasurementType.ToArmAssemblyOrigin
            && IsBoomArmFromHierarchy())
        {
            float armLen = TryGetArmLengthMetersFromHierarchy();
            if (armLen > 0f)
            {
                // If configured length is ~½ of the drawn span, prefer world (common ÷2 bug).
                if (Mathf.Abs(worldMeters - armLen * 2f) < 0.08f * Mathf.Max(worldMeters, armLen))
                    distanceMeters = worldMeters;
                else if (Mathf.Abs(worldMeters - armLen) < 0.08f * Mathf.Max(worldMeters, armLen))
                    distanceMeters = armLen;
                else
                    distanceMeters = armLen;
            }
        }

        // Elevation / cutsheet overlays: metric (mm) per CS / Architecture feedback.
        if (Selectable.IsInElevationPhotoMode)
        {
            int mm = Mathf.RoundToInt(distanceMeters * 1000f);
            Distance = $"{mm} mm";
        }
        else
        {
            float distanceFeet = Mathf.Floor(distanceMeters.ToFeet());
            float distanceInches = Mathf.Round((distanceMeters.ToFeet() - distanceFeet) * 12f * 10f) / 10f;
            Distance = $"{distanceFeet}' {distanceInches}\"";
        }

        transform.localScale = new Vector3(
            transform.localScale.x,
            transform.localScale.y,
            worldMeters);
        MeasurementText.UpdateVisibilityAndPosition(camera);

        if (Measurement != null
            && (Measurement.MeasurementType == MeasurementType.Floor
                || Measurement.RoomBoundaryType == RoomBoundaryType.Floor
                || Mathf.Abs(Measurement.HitPoint.y) < 0.05f))
        {
            CreateHorizontalLine();
            AlignHorizontalLineToFloor();
        }
    }
    private GameObject horizontalLine;
    private GameObject ceilingLine;

    public void CreateCeilingToFloorLine()
    {
        if (ceilingLine == null)
        {
            ceilingLine = new GameObject("CeilingToFloorLine");
            ceilingLine.transform.SetParent(transform);

            // Create line renderer for the vertical line
            LineRenderer lineRenderer = ceilingLine.AddComponent<LineRenderer>();
            lineRenderer.startWidth = 0.1f;
            lineRenderer.endWidth = 0.1f;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = Color.gray;
            lineRenderer.endColor = Color.gray;
            ceilingLine.layer = gameObject.layer;
            lineRenderer.positionCount = 2;

            // Calculate positions
            float ceilingHeight = GetCeilingHeight();
            // Assuming floor is at y = 0, adjust if needed
            float floorHeight = 0f;

            // Set the line from ceiling to floor (vertical line)
            lineRenderer.SetPosition(0, new Vector3(0, ceilingHeight, 0));
            lineRenderer.SetPosition(1, new Vector3(0, floorHeight, 0));

            // Create text mesh for height display
            GameObject heightText = new GameObject("HeightText");
            heightText.transform.SetParent(ceilingLine.transform);

            // Position the text at the middle of the line
            heightText.transform.position = new Vector3(0.2f, (ceilingHeight + floorHeight) / 2, 0);

            // Add TextMesh component
            TextMesh textMesh = heightText.AddComponent<TextMesh>();
            textMesh.text = ceilingHeight.ToString("F2") + " m";
            textMesh.fontSize = 14;
            textMesh.alignment = TextAlignment.Left;
            textMesh.anchor = TextAnchor.MiddleLeft;
            textMesh.color = Color.black;
            // Make text face the camera
            heightText.AddComponent<Billboard>();
        }
    }

    // Add this helper class to make the text always face the camera
    public class Billboard : MonoBehaviour
    {
        void Update()
        {
            if (Camera.main != null)
            {
                transform.rotation = Camera.main.transform.rotation;
            }
        }
    }


    private float GetCeilingHeight()
    {
        // You can replace this logic depending on how your room data is structured
        return RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling).Height;
    }
    public void CreateHorizontalLine()
    {
        if (horizontalLine == null)
        {
            horizontalLine = new GameObject("HorizontalLine");
            horizontalLine.transform.SetParent(transform, false);
            LineRenderer lineRenderer = horizontalLine.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.startWidth = 0.02f;
            lineRenderer.endWidth = 0.02f;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = Color.black;
            lineRenderer.endColor = Color.black;
            horizontalLine.layer = gameObject.layer;
            lineRenderer.positionCount = 2;
        }

        AlignHorizontalLineToFloor();
    }

    /// <summary>
    /// Aligns floor line graphic with the floor hit / dimension floor reference
    /// so print exports do not show a floating or double floor line.
    /// </summary>
    private void AlignHorizontalLineToFloor()
    {
        if (horizontalLine == null || Measurement == null)
            return;

        var lr = horizontalLine.GetComponent<LineRenderer>();
        if (lr == null)
            return;

        float floorY = Measurement.HitPoint.y;
        var floor = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor);
        if (floor != null)
            floorY = floor.transform.position.y;

        float halfWidth = GetFloorWidth() * 0.5f;
        Vector3 center = new Vector3(Measurement.HitPoint.x, floorY, Measurement.HitPoint.z);
        // Span horizontally in camera-facing elevation plane (camera right axis if available).
        Vector3 right = Camera.main != null ? Camera.main.transform.right : Vector3.right;
        right.y = 0f;
        if (right.sqrMagnitude < 1e-6f)
            right = Vector3.right;
        right.Normalize();

        lr.useWorldSpace = true;
        lr.SetPosition(0, center - right * halfWidth);
        lr.SetPosition(1, center + right * halfWidth);
    }

    private float GetFloorWidth()
    {
        var floor = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor);
        if (floor != null && floor.Width > 0.01f)
            return floor.Width;

        if (RoomSize.Instance != null)
            return RoomSize.Instance.CurrentDimensions.Width.ToMeters();

        return 4f;
    }

    private void CreateVerticalPattern(Vector3 startPosition, Vector3 endPosition)
    {
        float patternInterval = 0.2f; // Distance between each vertical line
        float verticalLineHeight = 0.5f; // Height of the vertical lines
        float rotationAngle = 10f; // Rotation angle for the vertical lines

        Vector3 direction = (endPosition - startPosition).normalized;
        float distance = Vector3.Distance(startPosition, endPosition);
        int numberOfLines = Mathf.FloorToInt(distance / patternInterval);

        for (int i = 0; i <= numberOfLines; i++)
        {
            Vector3 position = startPosition + direction * (i * patternInterval);
            CreateVerticalLine(position, verticalLineHeight, rotationAngle);
        }
    }


    private void CreateVerticalLine(Vector3 position, float height, float angle)
    {
        // Create a new GameObject for the vertical line
        GameObject verticalLine = new GameObject("VerticalLine");

        // Set the parent to the horizontal line object
        verticalLine.transform.SetParent(horizontalLine.transform);

        // Add a LineRenderer component to the vertical line
        LineRenderer lineRenderer = verticalLine.AddComponent<LineRenderer>();

        // Set the line width
        lineRenderer.startWidth = 0.05f;
        lineRenderer.endWidth = 0.05f;

        // Set the line color to black
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = Color.black;
        lineRenderer.endColor = Color.black;

        // Set the same layer as the other children objects
        verticalLine.layer = gameObject.layer;

        // Calculate the start and end positions for the vertical line
        Vector3 startPosition = position;
        Vector3 endPosition = position + Quaternion.Euler(0, 0, angle) * Vector3.up * height;

        // Set the line positions
        lineRenderer.SetPosition(0, startPosition);
        lineRenderer.SetPosition(1, endPosition);
    }



    private void Awake()
    {
        if (Prefab == null)
        {
            Prefab = gameObject;
            Initialized = true;
            gameObject.SetActive(false);
            return;
        }

        MeasurementText = MeasurementText.GetMeasurementText(this);
        _childTransform = transform.GetChild(0);
        Renderer = GetComponentInChildren<MeshRenderer>();
        LineRenderers = GetComponentsInChildren<LineRenderer>(true).ToList();
        LineRenderers.ForEach(x => x.enabled = false);
    }

    private void OnDestroy()
    {
        if (MeasurementText != null)
            Destroy(MeasurementText.gameObject);
    }
}
