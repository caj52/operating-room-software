using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Elevation cutsheet overlay draw order (ortho depth nudge + sorting):
/// equipment → white label plates → measurement lines + mm text.
/// </summary>
public static class ElevOverlayDrawOrder
{
    /// <summary>Lines above white plates, below mm text.</summary>
    public const int LineSortingOrder = 800;

    /// <summary>Milky frosted stroke behind black dim lines.</summary>
    public const int LineBackingSortingOrder = 700;

    /// <summary>World-space canvas for white plates (above equipment, below lines/text).</summary>
    public const int CanvasSortingOrder = 400;

    /// <summary>Nested canvas on each mm label — always above plates (and lines).</summary>
    public const int TextSortingOrder = 900;

    const int TransparentQueue = 4500;

    /// <summary>White plates — in front of boom, behind text/lines.</summary>
    public const float BackingTowardCameraMeters = 1.2f;

    /// <summary>TMP glyphs — in front of white plates.</summary>
    public const float TextTowardCameraMeters = 2.5f;

    /// <summary>Dim body/leaders — same plane as text (above plates).</summary>
    public const float LineTowardCameraMeters = 2.55f;

    /// <summary>Frosted line stroke — behind black lines, above equipment/plates.</summary>
    public const float LineBackingTowardCameraMeters = 2.0f;

    static Material _elevLineMaterial;
    static Material _elevLineBackingMaterial;

    public static Material SharedLineMaterial()
    {
        if (_elevLineMaterial != null)
            return _elevLineMaterial;

        var shader = Shader.Find("Sprites/Default");
        _elevLineMaterial = shader != null
            ? new Material(shader)
            : new Material(Shader.Find("Hidden/InternalErrorShader"));
        _elevLineMaterial.name = "ElevOverlayLine_Runtime";
        _elevLineMaterial.renderQueue = TransparentQueue;
        if (_elevLineMaterial.HasProperty("_ZTest"))
            _elevLineMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);
        _elevLineMaterial.SetInt("_ZWrite", 0);
        return _elevLineMaterial;
    }

    public static Material SharedLineBackingMaterial()
    {
        if (_elevLineBackingMaterial != null)
            return _elevLineBackingMaterial;

        _elevLineBackingMaterial = new Material(SharedLineMaterial());
        _elevLineBackingMaterial.name = "ElevOverlayLineBacking_Runtime";
        return _elevLineBackingMaterial;
    }

    public static Vector3 NudgeTowardCamera(Vector3 world, Camera camera, float meters)
    {
        if (camera == null)
            return world;
        return world - camera.transform.forward * meters;
    }

    /// <summary>Default text-plane nudge (lines/text).</summary>
    public static Vector3 NudgeTowardCamera(Vector3 world, Camera camera) =>
        NudgeTowardCamera(world, camera, TextTowardCameraMeters);

    public static void ApplyToLineRenderer(LineRenderer lr)
    {
        if (lr == null)
            return;
        lr.sharedMaterial = SharedLineMaterial();
        lr.sortingOrder = LineSortingOrder;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    public static void ApplyFrostedBackingToLineRenderer(LineRenderer lr, float blackWidth)
    {
        if (lr == null)
            return;
        lr.sharedMaterial = SharedLineBackingMaterial();
        lr.sortingOrder = LineBackingSortingOrder;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        float w = Mathf.Max(blackWidth * 3.2f, blackWidth + 0.012f);
        lr.startWidth = w;
        lr.endWidth = w;
        var milk = new Color(1f, 1f, 1f, 0.78f);
        lr.startColor = milk;
        lr.endColor = milk;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.78f, 0f), new GradientAlphaKey(0.78f, 1f) });
        lr.colorGradient = grad;
    }

    public static void NudgeLineRendererTowardCamera(LineRenderer lr, Camera camera)
    {
        if (lr == null || camera == null || !lr.useWorldSpace)
            return;
        int n = lr.positionCount;
        for (int i = 0; i < n; i++)
            lr.SetPosition(i, NudgeTowardCamera(lr.GetPosition(i), camera, LineTowardCameraMeters));
    }

    public static void NudgeLineBackingTowardCamera(LineRenderer lr, Camera camera)
    {
        if (lr == null || camera == null || !lr.useWorldSpace)
            return;
        int n = lr.positionCount;
        for (int i = 0; i < n; i++)
            lr.SetPosition(i, NudgeTowardCamera(lr.GetPosition(i), camera, LineBackingTowardCameraMeters));
    }

    public static void BeginCanvasForElevation(Camera elevCamera, out Canvas canvas,
        out Camera prevWorldCamera, out bool prevOverride, out int prevOrder)
    {
        canvas = null;
        prevWorldCamera = null;
        prevOverride = false;
        prevOrder = 0;

        var go = GameObject.Find("UI_WorldspaceText");
        if (go == null)
            return;
        canvas = go.GetComponent<Canvas>();
        if (canvas == null)
            return;

        prevWorldCamera = canvas.worldCamera;
        prevOverride = canvas.overrideSorting;
        prevOrder = canvas.sortingOrder;
        canvas.worldCamera = elevCamera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = CanvasSortingOrder;
    }

    public static void EndCanvasForElevation(Canvas canvas, Camera prevWorldCamera,
        bool prevOverride, int prevOrder)
    {
        if (canvas == null)
            return;
        canvas.worldCamera = prevWorldCamera;
        canvas.overrideSorting = prevOverride;
        canvas.sortingOrder = prevOrder;
    }
}
