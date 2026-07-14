using System.Collections;
using System.Collections.Generic;
using SplenSoft.AssetBundles;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Hover label for a UI control.
/// </summary>
[DisallowMultipleComponent]
public class UI_HoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private string _text;

    public void SetText(string text) => _text = text;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (string.IsNullOrWhiteSpace(_text))
            return;
        UI_HoverTooltipPopup.Show(_text, transform as RectTransform);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        UI_HoverTooltipPopup.Hide();
    }

    private void OnDisable()
    {
        UI_HoverTooltipPopup.Hide();
    }
}

/// <summary>
/// One shared tooltip popup for the whole UI.
/// </summary>
public static class UI_HoverTooltipPopup
{
    private static RectTransform _root;
    private static TextMeshProUGUI _label;
    private static Canvas _overlayCanvas;

    public static void Show(string text, RectTransform anchor)
    {
        EnsureCreated();
        if (_root == null || _label == null)
            return;

        TMP_RuntimeFontRepair.Repair(_label);
        _label.text = text;
        _label.ForceMeshUpdate();

        float padX = 14f;
        float padY = 8f;
        float width = Mathf.Clamp(_label.preferredWidth + padX * 2f, 72f, 360f);
        float height = Mathf.Clamp(_label.preferredHeight + padY * 2f, 28f, 120f);
        _root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        _root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

        PositionNear(anchor, width, height);
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
    }

    public static void Hide()
    {
        if (_root != null)
            _root.gameObject.SetActive(false);
    }

    private static void EnsureCreated()
    {
        if (_root != null)
            return;

        var overlayGo = new GameObject(
            "UI_HoverTooltipOverlay",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        Object.DontDestroyOnLoad(overlayGo);

        _overlayCanvas = overlayGo.GetComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 5000;
        _overlayCanvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
            | AdditionalCanvasShaderChannels.Normal
            | AdditionalCanvasShaderChannels.Tangent;

        var scaler = overlayGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // Never steal pointer events from the buttons underneath.
        overlayGo.GetComponent<GraphicRaycaster>().enabled = false;

        var tipGo = new GameObject("Tip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        tipGo.transform.SetParent(overlayGo.transform, false);

        _root = tipGo.GetComponent<RectTransform>();
        _root.pivot = new Vector2(0.5f, 1f);
        _root.anchorMin = new Vector2(0.5f, 0.5f);
        _root.anchorMax = new Vector2(0.5f, 0.5f);

        var bg = tipGo.GetComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.08f, 0.94f);
        bg.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(tipGo.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(10f, 6f);
        textRt.offsetMax = new Vector2(-10f, -6f);

        _label = textGo.GetComponent<TextMeshProUGUI>();
        _label.fontSize = 15;
        _label.alignment = TextAlignmentOptions.Center;
        _label.color = Color.white;
        _label.enableWordWrapping = true;
        _label.raycastTarget = false;
        _label.overflowMode = TextOverflowModes.Overflow;
        TMP_RuntimeFontRepair.Repair(_label);

        tipGo.SetActive(false);
    }

    private static void PositionNear(RectTransform anchor, float width, float height)
    {
        if (anchor == null || _overlayCanvas == null)
            return;

        var canvasRt = _overlayCanvas.transform as RectTransform;
        Vector3[] corners = new Vector3[4];
        anchor.GetWorldCorners(corners);

        Vector2 screenBottom = RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[3]) * 0.5f);
        Vector2 screenTop = RectTransformUtility.WorldToScreenPoint(null, (corners[1] + corners[2]) * 0.5f);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRt, screenBottom, null, out Vector2 localBottom);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRt, screenTop, null, out Vector2 localTop);

        float x = localBottom.x;
        float yBelow = localBottom.y - 8f;
        float yAbove = localTop.y + 8f;

        Vector2 canvasSize = canvasRt.rect.size;
        float halfW = width * 0.5f;
        x = Mathf.Clamp(x, -canvasSize.x * 0.5f + halfW + 8f, canvasSize.x * 0.5f - halfW - 8f);

        bool placeAbove = yBelow - height < -canvasSize.y * 0.5f + 8f;
        if (placeAbove)
        {
            _root.pivot = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(x, yAbove);
        }
        else
        {
            _root.pivot = new Vector2(0.5f, 1f);
            _root.anchoredPosition = new Vector2(x, yBelow);
        }
    }
}

/// <summary>
/// Attaches hover tips only to the room-editor corner toolbar icons.
/// Everything else is left alone (and any stray tip is removed).
/// </summary>
public class UI_IconButtonTooltipBinder : MonoBehaviour
{
    // Exact GameObject names for the in-room corner icon strip.
    private static readonly Dictionary<string, string> CornerToolbarTips = new()
    {
        { "Button_Save", "Save room" },
        { "Button_SaveConfiguration", "Save object configuration" },
        { "Button_Settings", "Settings" },
        { "Button_OpenArticulation", "Rotation" },
        { "Button_Quotation", "Sales proposal" },
        { "Button_OpenObjectMenu", "Object menu" },
        { "Button_OpenSceneSelectablesMenu", "Objects in scene" },
        { "Button_CycleCam", "Change view" },
        { "Button_ChangeCamera", "Change camera" },
        { "Button_Screenshot", "Screenshot" },
        { "Button_MaterialPallete", "Material palette" },
        { "Button_SetRoomSize", "Room size" },
        { "Button_OpenRoomPanel", "Room panel" },
        { "Button_DeleteObject", "Delete" },
        { "Button_DeleteObject (1)", "Delete" },
    };

    private static UI_IconButtonTooltipBinder _runner;

    private void Start() => BindAllInScene();

    public static void BindAllInScene()
    {
        var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            if (button == null)
                continue;

            var hover = button.GetComponent<UI_HoverTooltip>();

            if (!CornerToolbarTips.TryGetValue(button.gameObject.name, out string tip)
                || string.IsNullOrWhiteSpace(tip))
            {
                // Strip leftover tips only from room Button_* clutter (Export, Load, …).
                // Leave other tipped controls alone (e.g. proposal PDF hotspots).
                if (hover != null && button.gameObject.name.StartsWith("Button_"))
                    Object.Destroy(hover);
                continue;
            }

            EnsureRaycastTarget(button);

            if (hover == null)
                hover = button.gameObject.AddComponent<UI_HoverTooltip>();
            hover.SetText(tip);
        }
    }

    /// <summary>
    /// Some icon buttons only raycast on a child Image — fine — but if neither
    /// the button nor any child can be hit, hover never fires.
    /// </summary>
    private static void EnsureRaycastTarget(Button button)
    {
        if (button.targetGraphic != null)
        {
            button.targetGraphic.raycastTarget = true;
            return;
        }

        var img = button.GetComponent<Image>();
        if (img != null)
        {
            img.raycastTarget = true;
            if (button.targetGraphic == null)
                button.targetGraphic = img;
            return;
        }

        var childImg = button.GetComponentInChildren<Image>(true);
        if (childImg != null)
            childImg.raycastTarget = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterCallbacks()
    {
        SceneManager.sceneLoaded += (_, __) => ScheduleBind();
        try
        {
            AutoInstantiator.OnJobsFinished.AddListener(ScheduleBind);
        }
        catch
        {
            // AutoInstantiator may not exist in every scene.
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => ScheduleBind();

    private static void ScheduleBind()
    {
        EnsureRunner();
        _runner.StartCoroutine(_runner.BindSoon());
    }

    private static void EnsureRunner()
    {
        if (_runner != null)
            return;

        var go = new GameObject(nameof(UI_IconButtonTooltipBinder));
        Object.DontDestroyOnLoad(go);
        _runner = go.AddComponent<UI_IconButtonTooltipBinder>();
    }

    private IEnumerator BindSoon()
    {
        BindAllInScene();
        yield return null;
        BindAllInScene();
        yield return new WaitForSecondsRealtime(0.5f);
        BindAllInScene();
        yield return new WaitForSecondsRealtime(1.5f);
        BindAllInScene();
    }
}
