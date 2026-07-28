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
    private LineRenderer _elevationBodyLine;

    /// <summary>Elevation floor-label lane (0,1,2…) for text side/offset — dim line stays on part.</summary>
    public int ElevationTextLane { get; set; }
    //public bool AllowInElevationPhotoMode => Measurement != null && Measurement.Measurable.ArmAssemblyActiveInElevationPhotoMode && Measurement.MeasurementType == MeasurementType.ToArmAssemblyOrigin;
    public Measurable.Measurement Measurement { get; private set; }
    public string Distance { get; private set; } = string.Empty;
    private Transform _childTransform;
    public Vector3 TextPosition => _childTransform.position;
    public static bool Initialized { get; private set; }

    private void OnEnable()
    {
        if (MeasurementText == null)
            return;

        // Never auto-show labels. Interactive use turns them on via Measurable.SetActive;
        // elevation cutsheet turns them on explicitly after Distance is metric mm.
        if (MeasurementText != null)
            MeasurementText.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (MeasurementText != null)
            MeasurementText.gameObject.SetActive(false);
    }

    private void Update()
    {
        // Elevation photos only draw cutsheet dims. Leave stale interactive (often imperial)
        // labels hidden — they live on a shared canvas, not under the selectable.
        if (Selectable.IsInElevationPhotoMode)
        {
            if (!ShouldDrawInElevationPhoto())
            {
                if (MeasurementText != null && MeasurementText.gameObject.activeSelf)
                    MeasurementText.gameObject.SetActive(false);
                if (LineRenderers != null)
                {
                    foreach (var lr in LineRenderers)
                    {
                        if (lr != null)
                            lr.enabled = false;
                    }
                }
                if (_elevationBodyLine != null)
                    _elevationBodyLine.enabled = false;
                if (Renderer != null)
                    Renderer.enabled = false;
            }
            // Do not UpdateTransform — cutsheet pass already placed Origin/HitPoint/leaders.
            return;
        }

        if (_elevationBodyLine != null)
            _elevationBodyLine.enabled = false;
        if (Renderer != null && !Renderer.enabled)
            Renderer.enabled = true;

        UpdateTransform();
    }

    /// <summary>
    /// Cutsheet elevation whitelist: catalog arm/tube lengths + floor clearances only.
    /// </summary>
    public bool ShouldDrawInElevationPhoto()
    {
        if (Measurement?.Measurable == null)
            return false;
        if (!Measurement.Measurable.ShowInElevationPhoto)
            return false;

        if (Measurement.MeasurementType == MeasurementType.ToArmAssemblyOrigin)
            return TryGetOwningCatalogLengthMeters() > 0f;

        return Measurement.MeasurementType == MeasurementType.Floor;
    }

    public static Measurer GetMeasurer(Measurable.Measurement measurement)
    {
        var newObj = Instantiate(Prefab);
        var measurer = newObj.GetComponent<Measurer>();
        measurer.Measurement = measurement;
        return measurer;
    }

    /// <summary>
    /// Catalog length for this measurable's owning Selectable only (no ancestor steal).
    /// Size is meters; returns -1 when the owner has no configured length.
    /// </summary>
    private float TryGetOwningCatalogLengthMeters()
    {
        var measurable = Measurement?.Measurable;
        if (measurable == null)
            return -1f;

        // Cutsheet pass may pin catalog length when dual-select ownership is ambiguous.
        if (measurable.CutsheetCatalogLengthMeters > 0f)
            return measurable.CutsheetCatalogLengthMeters;

        // Cutsheet pin first; else own Size only (never Related steal).
        float sizeM = ElevationLengthFormat.ResolveOwnSizeMeters(measurable.GetOwningSelectable());
        return sizeM > 0f ? sizeM : -1f;
    }

    public void UpdateTransform(Camera camera = null)
    {
        if (camera == null)
        {
            camera = Camera.main;
        }

        Vector3 hitPoint = Measurement.HitPoint;
        // Floor clearances only — never snap catalog length dims that happen to sit near y=0.
        bool isFloorHit = Measurement.MeasurementType == MeasurementType.Floor;
        if (isFloorHit)
            hitPoint.y = ElevationDimPlacement.FloorTopY();

        transform.position = Measurement.Origin;
        transform.LookAt(hitPoint);

        // Drawn leader uses world span. Catalog length wins for arm/tube callouts.
        float worldMeters = Vector3.Distance(Measurement.Origin, hitPoint);
        float distanceMeters = worldMeters;
        float catalogLen = -1f;
        if (Measurement != null
            && Measurement.MeasurementType == MeasurementType.ToArmAssemblyOrigin)
        {
            catalogLen = TryGetOwningCatalogLengthMeters();
            if (catalogLen > 0f)
                distanceMeters = catalogLen;
        }

        // Elevation / cutsheet overlays: metric (mm) per CS / Architecture feedback.
        if (Selectable.IsInElevationPhotoMode)
        {
            // Catalog Size may be legacy raw-mm; world floor spans are always meters.
            int mm = Measurement != null
                && Measurement.MeasurementType == MeasurementType.ToArmAssemblyOrigin
                ? ElevationLengthFormat.SizeValueToMm(distanceMeters)
                : Mathf.RoundToInt(distanceMeters * 1000f);
            Distance = $"{mm} mm";
        }
        else
        {
            float distanceFeet = Mathf.Floor(distanceMeters.ToFeet());
            float distanceInches = Mathf.Round((distanceMeters.ToFeet() - distanceFeet) * 12f * 10f) / 10f;
            Distance = $"{distanceFeet}' {distanceInches}\"";
        }

        // Body must match Origin→HitPoint so ticks and leaders agree. Only fall back to
        // catalog when the world span collapsed (~0) — e.g. mount pivots on the AP.
        float drawMeters = worldMeters;
        if (drawMeters < 0.01f && catalogLen > 0f)
            drawMeters = catalogLen;
        if (drawMeters < 0.01f)
            drawMeters = 0.01f;

        // Always reset thickness — preserving localScale.x/y once let a fat scale stick
        // and drew the black rectangular "boxes" on elevation PDFs.
        float thickness = Selectable.IsInElevationPhotoMode ? 0.006f : 0.01f;
        transform.localScale = new Vector3(thickness, thickness, drawMeters);

        // Elevation: hide the mesh body; leaders + a thin body line carry the dim.
        // The cube mesh read as a heavy black rectangle in ortho captures.
        if (Renderer == null)
            Renderer = GetComponentInChildren<MeshRenderer>(true);
        if (Renderer != null)
            Renderer.enabled = !Selectable.IsInElevationPhotoMode;

        if (Selectable.IsInElevationPhotoMode)
            EnsureElevationBodyLine(Measurement.Origin, hitPoint, camera);

        // Elevation: show label only when this is a cutsheet dim. Live scene: only if
        // the measurable is interactively active (never auto-revive after export).
        if (MeasurementText != null)
        {
            bool show = Selectable.IsInElevationPhotoMode
                ? ShouldDrawInElevationPhoto()
                : (Measurement?.Measurable != null && Measurement.Measurable.IsActive);
            if (show && !MeasurementText.gameObject.activeSelf)
                MeasurementText.gameObject.SetActive(true);
            else if (!show && MeasurementText.gameObject.activeSelf)
                MeasurementText.gameObject.SetActive(false);
            if (show)
                MeasurementText.UpdateVisibilityAndPosition(camera);
        }

        // Do not draw an extra floor graphic in elevation exports. The room floor
        // mesh already renders the thick hatched floor line; dimension rays should
        // simply terminate there.
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

        float floorY = GetFloorTopY();

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

    /// <summary>
    /// World Y of the visible floor surface (top of floor mesh). Room convention
    /// places this at y=0; floor transform center sits half-thickness below.
    /// </summary>
    private static float GetFloorTopY() => ElevationDimPlacement.FloorTopY();

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
        EnsureLineRenderers();
        LineRenderers.ForEach(x => x.enabled = false);
    }

    /// <summary>
    /// Prefab instances can wake with an empty LineRenderers list (Awake order / inactive).
    /// Re-query children before indexing — avoids ArgumentOutOfRange spam in Update/elev.
    /// </summary>
    public void EnsureLineRenderers()
    {
        if (LineRenderers != null && LineRenderers.Count >= 2
            && LineRenderers[0] != null && LineRenderers[1] != null
            && LineRenderers[0] != _elevationBodyLine
            && LineRenderers[1] != _elevationBodyLine)
            return;
        LineRenderers = GetComponentsInChildren<LineRenderer>(true)
            .Where(lr => lr != null && lr != _elevationBodyLine)
            .ToList();
    }

    /// <summary>
    /// Ortho elevation captures turn the scaled cube mesh into a heavy black rectangle.
    /// Draw the dim span as a world-space line instead.
    /// </summary>
    private void EnsureElevationBodyLine(Vector3 origin, Vector3 hitPoint, Camera camera)
    {
        if (_elevationBodyLine == null)
        {
            var go = new GameObject("ElevationBodyLine");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            _elevationBodyLine = go.AddComponent<LineRenderer>();
            _elevationBodyLine.useWorldSpace = true;
            _elevationBodyLine.positionCount = 2;
            _elevationBodyLine.material = new Material(Shader.Find("Sprites/Default"));
            _elevationBodyLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _elevationBodyLine.receiveShadows = false;
        }

        float w = 0.008f;
        if (camera != null)
        {
            float d0 = Mathf.Abs(Vector3.Dot(origin - camera.transform.position, camera.transform.forward));
            float d1 = Mathf.Abs(Vector3.Dot(hitPoint - camera.transform.position, camera.transform.forward));
            w = Mathf.Max(0.004f, 0.0025f * Mathf.Max(d0, d1));
        }

        _elevationBodyLine.enabled = true;
        _elevationBodyLine.SetPosition(0, origin);
        _elevationBodyLine.SetPosition(1, hitPoint);
        _elevationBodyLine.startWidth = w;
        _elevationBodyLine.endWidth = w;
        _elevationBodyLine.startColor = Color.black;
        _elevationBodyLine.endColor = Color.black;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        _elevationBodyLine.colorGradient = grad;
    }

    /// <summary>True when the thin elev body LineRenderer is drawn (orphan scan uses this).</summary>
    public bool ElevationBodyLineEnabled =>
        _elevationBodyLine != null && _elevationBodyLine.enabled;

    /// <summary>
    /// Hide every elev visual on this measurer (leaders, body line, mesh, text).
    /// Safe to call after PDF capture so overlays do not remain in the live scene.
    /// </summary>
    public void DisableElevationVisuals()
    {
        if (_elevationBodyLine != null)
            _elevationBodyLine.enabled = false;
        if (LineRenderers != null)
        {
            foreach (var lr in LineRenderers)
            {
                if (lr != null)
                    lr.enabled = false;
            }
        }
        if (MeasurementText != null)
            MeasurementText.gameObject.SetActive(false);
        // Restore mesh for interactive use later; elev had it forced off.
        if (Renderer == null)
            Renderer = GetComponentInChildren<MeshRenderer>(true);
        if (Renderer != null)
            Renderer.enabled = true;
    }

    public bool TryGetLeaderPair(out LineRenderer a, out LineRenderer b)
    {
        EnsureLineRenderers();
        a = null;
        b = null;
        if (LineRenderers == null || LineRenderers.Count < 2)
            return false;
        a = LineRenderers[0];
        b = LineRenderers[1];
        return a != null && b != null;
    }

    private void OnDestroy()
    {
        if (MeasurementText != null)
            Destroy(MeasurementText.gameObject);
    }
}
