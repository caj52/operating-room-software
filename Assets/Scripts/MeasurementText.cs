using RTG;
using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MeasurementText : MonoBehaviour
{
    private static GameObject Prefab { get; set; }

    [SerializeField, ReadOnly] private Measurer _measurer;
    public TextMeshProUGUI Text;

    private Material _defaultSharedMaterial;
    private Material _elevationSharedMaterial;
    private bool _elevationStyleApplied;
    private Image _elevTextBacking;
    private Canvas _elevTextCanvas;
    private static Sprite _elevWhiteSprite;

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
        // Labels are display-only overlays; they must not steal UI / scene mouse hits.
        if (Text != null)
            Text.raycastTarget = false;
    }

    private void OnDestroy()
    {
        Measurable.ActiveMeasurablesChanged.RemoveListener(CheckActiveState);
        DestroyElevTextBacking();
        if (_elevationSharedMaterial != null)
            Destroy(_elevationSharedMaterial);
    }

    private void OnDisable()
    {
        // Plate is a canvas sibling — deactivating this label must hide it too.
        HideElevTextBacking();
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
            {
                HideElevTextBacking();
                gameObject.SetActive(false);
            }
            return;
        }

        bool active = _measurer != null
            && _measurer.Measurement?.Measurable != null
            && _measurer.Measurement.Measurable.IsActive;

        if (!active)
            HideElevTextBacking();
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
            HideElevTextBacking();
            gameObject.SetActive(false);
            return;
        }

        if (Text == null)
        {
            Text = GetComponent<TextMeshProUGUI>();
            if (Text != null)
                Text.raycastTarget = false;
        }
        if (Text == null)
        {
            HideElevTextBacking();
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
                HideElevTextBacking();
                gameObject.SetActive(false);
                return;
            }

            // Never show stale imperial leftovers on cutsheets.
            if (Text.text != null && Text.text.IndexOf('\'') >= 0
                && Text.text.IndexOf("mm", StringComparison.OrdinalIgnoreCase) < 0)
            {
                HideElevTextBacking();
                gameObject.SetActive(false);
                return;
            }

            ApplyElevationTextStyle();
            PlaceElevationText(camera);
            gameObject.SetActive(true);
            return;
        }

        HideElevTextBacking();
        RestoreDefaultTextStyle();
        ClearElevTextCanvasOverride();

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
        bool preferBelow = !isFloor && _measurer != null && _measurer.ElevationPreferLabelBelow;
        float outboard = !isFloor && _measurer != null ? _measurer.ElevationLabelSideSign : 0f;

        Vector3 onLine = _measurer.TextPosition;
        Vector3 dimDir = _measurer.transform.forward;
        if (dimDir.sqrMagnitude < 1e-8f)
            dimDir = Vector3.down;

        transform.position = onLine;
        RotateTowardCamera(camera);
        Vector3 spanA = default;
        Vector3 spanB = default;
        if (!isFloor && _measurer.Measurement != null)
        {
            spanA = _measurer.Measurement.Origin;
            spanB = _measurer.Measurement.HitPoint;
        }
        ElevationDimPlacement.PlaceLabel(
            transform, Text, onLine, dimDir, camera, isFloor, floorLane, preferBelow, outboard,
            spanA, spanB);
        // Rotation can change glyph world extents — clamp after final orient.
        RotateTowardCamera(camera);
        if (!isFloor)
            ElevationDimPlacement.ClampLabelInsideDimSpan(transform, Text, spanA, spanB);
        // Ortho depth stack: white plate (near), then mm text (closer to camera).
        Vector3 pagePos = transform.position;
        if (Selectable.IsInElevationPhotoMode && camera != null)
        {
            transform.position = ElevOverlayDrawOrder.NudgeTowardCamera(
                pagePos, camera, ElevOverlayDrawOrder.TextTowardCameraMeters);
        }
        EnsureElevTextBacking(camera, pagePos);
    }

    /// <summary>
    /// Tight white plate under elev mm text. Sibling of the label (not TMP child) so
    /// glyphs paint on top; nested text canvas sorting keeps mm above plates.
    /// </summary>
    void EnsureElevTextBacking(Camera camera, Vector3 pagePos)
    {
        if (Text == null)
            return;

        if (_elevTextBacking == null)
        {
            var go = new GameObject("ElevTextBacking", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = gameObject.layer;
            _elevTextBacking = go.GetComponent<Image>();
            _elevTextBacking.raycastTarget = false;
            // Milky frosted plate: white-on-white vanishes; over gear you still see a wash of it.
            _elevTextBacking.color = new Color(1f, 1f, 1f, 0.78f);
            if (_elevWhiteSprite == null)
                _elevWhiteSprite = CreateElevFrostedSprite();
            _elevTextBacking.sprite = _elevWhiteSprite;
            _elevTextBacking.type = Image.Type.Sliced;
            _elevTextBacking.pixelsPerUnitMultiplier = 1f;
        }

        Transform canvasParent = transform.parent;
        if (canvasParent != null)
        {
            if (_elevTextBacking.transform.parent != canvasParent)
                _elevTextBacking.transform.SetParent(canvasParent, false);
            // Hierarchy alone is unreliable once many labels reshuffle — still keep plate
            // immediately under this label, then force text above via nested canvas order.
            PlaceBackingUnderLabel();
        }

        EnsureElevTextCanvasOverride();

        _elevTextBacking.gameObject.SetActive(true);
        _elevTextBacking.color = new Color(1f, 1f, 1f, 0.78f);
        if (_elevWhiteSprite == null)
            _elevWhiteSprite = CreateElevFrostedSprite();
        if (_elevTextBacking.sprite != _elevWhiteSprite)
        {
            _elevTextBacking.sprite = _elevWhiteSprite;
            _elevTextBacking.type = Image.Type.Sliced;
        }
        Text.ForceMeshUpdate();
        Bounds gb = Text.textBounds;
        var brt = _elevTextBacking.rectTransform;
        brt.anchorMin = new Vector2(0.5f, 0.5f);
        brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.localScale = transform.localScale;
        brt.localRotation = Quaternion.identity;

        const float pad = 6f;
        brt.sizeDelta = new Vector2(gb.size.x + pad, gb.size.y + pad);

        Vector3 glyphCenterPage = pagePos + transform.TransformVector(gb.center);
        if (Selectable.IsInElevationPhotoMode && camera != null)
        {
            _elevTextBacking.transform.SetPositionAndRotation(
                ElevOverlayDrawOrder.NudgeTowardCamera(
                    glyphCenterPage, camera, ElevOverlayDrawOrder.BackingTowardCameraMeters),
                transform.rotation);
        }
        else
        {
            _elevTextBacking.transform.SetPositionAndRotation(glyphCenterPage, transform.rotation);
        }
    }

    void PlaceBackingUnderLabel()
    {
        int labelIdx = transform.GetSiblingIndex();
        int backIdx = _elevTextBacking.transform.GetSiblingIndex();
        if (backIdx > labelIdx)
        {
            // Insert before label (label shifts right).
            _elevTextBacking.transform.SetSiblingIndex(labelIdx);
        }
        else if (backIdx < labelIdx - 1)
        {
            _elevTextBacking.transform.SetSiblingIndex(labelIdx - 1);
        }

        // Never leave the plate after the label.
        if (transform.GetSiblingIndex() < _elevTextBacking.transform.GetSiblingIndex())
            transform.SetSiblingIndex(_elevTextBacking.transform.GetSiblingIndex());
    }

    void EnsureElevTextCanvasOverride()
    {
        if (_elevTextCanvas == null)
        {
            _elevTextCanvas = GetComponent<Canvas>();
            if (_elevTextCanvas == null)
                _elevTextCanvas = gameObject.AddComponent<Canvas>();
        }
        _elevTextCanvas.overrideSorting = true;
        _elevTextCanvas.sortingOrder = ElevOverlayDrawOrder.TextSortingOrder;
    }

    void ClearElevTextCanvasOverride()
    {
        if (_elevTextCanvas != null)
            _elevTextCanvas.overrideSorting = false;
    }

    /// <summary>Hide/destroy elev plate leftovers (sibling of label, not auto-disabled with it).</summary>
    public void HideElevOverlays()
    {
        HideElevTextBacking();
        ClearElevTextCanvasOverride();
    }

    void HideElevTextBacking()
    {
        if (_elevTextBacking != null)
            _elevTextBacking.gameObject.SetActive(false);
    }

    void DestroyElevTextBacking()
    {
        if (_elevTextBacking == null)
            return;
        Destroy(_elevTextBacking.gameObject);
        _elevTextBacking = null;
    }

    /// <summary>
    /// Soft-edged white sprite for elev label plates. Alpha falls off at the border so
    /// plates read as frosted pads (equipment washes through) instead of hard boxes.
    /// </summary>
    static Sprite CreateElevFrostedSprite()
    {
        const int size = 64;
        const int border = 14;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ElevFrostedPlate",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Min(x + 1, size - x) / (float)border;
                float dy = Mathf.Min(y + 1, size - y) / (float)border;
                float a = Mathf.Clamp01(Mathf.Min(dx, dy));
                a = a * a * (3f - 2f * a); // smoothstep
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply(false, true);
        return Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
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
