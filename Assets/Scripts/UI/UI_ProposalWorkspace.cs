using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Near-fullscreen sales-proposal viewer. Shows the real generated PDF as rasterized
/// page images with clickable hotspots over editable regions (plus a slim side rail
/// for options / prices / client data / refresh).
/// </summary>
[RequireComponent(typeof(FullScreenMenu))]
public class UI_ProposalWorkspace : MonoBehaviour
{
    public static UI_ProposalWorkspace Instance { get; private set; }

    const float PreviewDebounceSeconds = 0.45f;
    /// <summary>A4 portrait aspect (210 / 297).</summary>
    const float A4Aspect = 210f / 297f;

    /// <summary>Live discount for PDF default pull while the workspace is open.</summary>
    public static float? LiveDiscountPercentage =>
        Instance != null && Instance.isActiveAndEnabled && Instance._model != null
            ? Instance._model.DiscountPercentage
            : (float?)null;

    static class Theme
    {
        public static readonly Color Dimmer = new(0.12f, 0.14f, 0.16f, 0.58f);
        public static readonly Color Shell = new(0.90f, 0.91f, 0.93f, 1f);
        public static readonly Color Desk = new(0.78f, 0.80f, 0.84f, 1f);
        public static readonly Color Paper = new(1f, 1f, 0.995f, 1f);
        public static readonly Color Ink = new(0.12f, 0.13f, 0.15f, 1f);
        public static readonly Color InkMuted = new(0.42f, 0.44f, 0.48f, 1f);
        public static readonly Color InkFaint = new(0.58f, 0.60f, 0.64f, 1f);
        public static readonly Color Rule = new(0.88f, 0.89f, 0.91f, 1f);
        public static readonly Color Section = new(0.94f, 0.94f, 0.95f, 1f);
        public static readonly Color Banner = new(0.93f, 0.93f, 0.94f, 1f);
        public static readonly Color Editable = new(0.99f, 0.97f, 0.94f, 1f);
        public static readonly Color EditableHover = new(0.98f, 0.93f, 0.86f, 1f);
        public static readonly Color EditableBorder = new(0.90f, 0.82f, 0.70f, 1f);
        /// <summary>Soft highlight drawn over PDF regions that open an editor.</summary>
        public static readonly Color Hotspot = new(0.98f, 0.82f, 0.35f, 0.32f);
        public static readonly Color HotspotHover = new(0.98f, 0.68f, 0.18f, 0.52f);
        public static readonly Color HotspotPressed = new(0.95f, 0.55f, 0.10f, 0.62f);
        public static readonly Color Accent = new(0.91f, 0.47f, 0.13f, 1f);
        public static readonly Color AccentDark = new(0.78f, 0.38f, 0.08f, 1f);
        public static readonly Color Ghost = new(1f, 1f, 1f, 0.92f);
        public static readonly Color GhostBorder = new(0.72f, 0.74f, 0.78f, 1f);
        public static readonly Color NavIdle = new(0.70f, 0.72f, 0.76f, 1f);
        public static readonly Color TableHead = new(0.16f, 0.17f, 0.19f, 1f);
        public static readonly Color Popover = new(0.98f, 0.98f, 0.99f, 1f);
        public static readonly Color InputFill = new(0.95f, 0.95f, 0.96f, 1f);
        public static readonly Color Placeholder = new(0.94f, 0.94f, 0.95f, 1f);
        public static readonly Color RowAlt = new(0.97f, 0.97f, 0.98f, 1f);
    }

    ProposalPreviewModel _model;
    int _pageIndex;
    bool _built;

    /// <summary>Bump to force-rebuild the DontDestroyOnLoad workspace after UI hierarchy fixes.</summary>
    const int UiBuildVersion = 6;
    static int _loadedUiBuildVersion;

    UnityAction _onPricingChanged;
    UnityAction<SelectablePrice> _onPriceEvent;

    /// <summary>Active workspace model while the UI is open (live discount / edit surface).</summary>
    public ProposalPreviewModel ActiveModel => isActiveAndEnabled ? _model : null;

    /// <summary>Preview model for PDF generation while the workspace has state.</summary>
    public ProposalPreviewModel PreviewModel => _model;

    RectTransform _root;
    RectTransform _popoverHost;
    RectTransform _dotsHost;
    ScrollRect _pageScroll;
    RectTransform _pageScrollRt;
    Image _pageImage;
    RectTransform _pageFrameRt;
    RectTransform _pageViewportRt;
    RectTransform _pageHostRt;
    RectTransform _hotspotLayer;
    float _pageAspectRatio = A4Aspect;
    TMP_Text _pageLabel;
    TMP_Text _statusLabel;
    GameObject _loadingOverlay;
    TMP_Text _loadingLabel;
    RectTransform _loadingBarFill;
    Coroutine _loadingAnimRoutine;
    bool _previewLoading;
    string _loadingBaseMessage = "Generating proposal preview…";
    readonly List<Image> _pageDots = new();
    readonly List<ProposalPreviewHotspot> _hotspots = new();

    readonly List<Texture2D> _pageTextures = new();
    readonly List<Sprite> _pageSprites = new();

    int _previewGenerationId;
    Coroutine _previewDebounce;
    Coroutine _previewBuildRoutine;
    bool _pendingForceVisuals;
    /// <summary>True while a preview bake is running — pricing events must not restart it.</summary>
    bool _suppressAutoPreviewRefresh;
    bool _queuedUserPreviewRefresh;
    bool _queuedUserForceVisuals;

    /// <summary>1 = fit page in viewport; higher values zoom in.</summary>
    float _pageZoom = 1f;
    TMP_Text _zoomPercentLabel;
    bool _pointerOverPageViewport;

    const float PageZoomMin = 1f;
    const float PageZoomMax = 3f;
    const float PageZoomStep = 0.1f;
    const float PageViewPadding = 24f;
    const float ScrollbarThickness = 14f;
    float _lastScrollOuterW = -1f;
    float _lastScrollOuterH = -1f;

    Scrollbar _pageScrollbarV;
    Scrollbar _pageScrollbarH;
    GameObject _openOptionDropdown;
    GameObject _optionDropdownDismiss;
    bool _suppressOptionDropdownClose;

    public static void Open()
    {
        try
        {
            HideLegacyPricingPanel();
            EnsureInstance();
            if (Instance.isActiveAndEnabled)
            {
                Instance._model?.PersistEditableFields();
                Instance.ClosePopover();
            }
            EnsurePricingOptionsInitialized();
            HideLegacyPricingPanel();
            Instance._suppressAutoPreviewRefresh = true;
            DropdownPopulator.RestoreAllPersistedSelections();

            // Show UI + loading first — defer Capture()/PDF bake so they don't freeze the frame.
            Instance._model ??= new ProposalPreviewModel();
            Instance.gameObject.SetActive(true);
            Instance.EnsureBlocksRaycasts();
            Instance._pageIndex = 0;
            Instance._pageZoom = 1f;
            Instance.ClosePopover();

            bool forceVisuals = ProposalPDFGenerator.PreviewVisualsStale;
            Instance.SetPreviewLoading(true, forceVisuals
                ? "Generating proposal preview…"
                : "Updating proposal preview…");
            Instance.ShowPage(0);
            Instance.BeginPreviewGeneration(forceVisuals: forceVisuals);

            Canvas.ForceUpdateCanvases();
            if (Instance._root != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(Instance._root);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to open proposal workspace: {e}");
            UI_DialogPrompt.Open(
                "Could not open the sales proposal.\nCheck the console for details.",
                new ButtonAction("OK"));
        }
    }

    static void HideLegacyPricingPanel()
    {
        var panel = FindPricingQuotePanel();
        if (panel != null && panel.activeSelf)
            panel.SetActive(false);
    }

    public static void Close()
    {
        if (Instance == null)
            return;
        Instance._model?.PersistEditableFields();
        Instance.ClosePopover();
        Instance.CancelPreviewRefresh();
        Instance.gameObject.SetActive(false);
    }

    static void EnsureInstance()
    {
        if (Instance != null && _loadedUiBuildVersion != UiBuildVersion)
        {
            var stale = Instance.gameObject;
            Instance = null;
            Destroy(stale);
        }

        if (Instance != null)
            return;

        var go = new GameObject(nameof(UI_ProposalWorkspace), typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image), typeof(CanvasGroup));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 280;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var rootImage = go.GetComponent<Image>();
        rootImage.color = Theme.Dimmer;
        rootImage.raycastTarget = true;

        go.AddComponent<FullScreenMenu>();

        var workspace = go.AddComponent<UI_ProposalWorkspace>();
        workspace.BuildUi();
        workspace.EnsureBlocksRaycasts();
        go.SetActive(false);
        DontDestroyOnLoad(go);
        Instance = workspace;
        _loadedUiBuildVersion = UiBuildVersion;
    }

    static void EnsurePricingOptionsInitialized()
    {
        if (DropdownPopulator.Instances != null && DropdownPopulator.Instances.Count > 0)
            return;

        var panel = FindPricingQuotePanel();
        if (panel == null)
            return;

        var cg = panel.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = panel.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.interactable = false;
        cg.blocksRaycasts = false;

        panel.SetActive(true);

        foreach (var pop in panel.GetComponentsInChildren<DropdownPopulator>(true))
        {
            if (pop == null)
                continue;
            var t = pop.transform;
            while (t != null)
            {
                if (!t.gameObject.activeSelf)
                    t.gameObject.SetActive(true);
                if (t.gameObject == panel)
                    break;
                t = t.parent;
            }
        }

        panel.SetActive(false);
        DropdownPopulator.RestoreAllPersistedSelections();
    }

    static GameObject _cachedPricingQuotePanel;

    static GameObject FindPricingQuotePanel()
    {
        if (_cachedPricingQuotePanel != null)
            return _cachedPricingQuotePanel;

        var all = UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in all)
        {
            if (t != null && t.name == "Panel_PricingQuote")
            {
                _cachedPricingQuotePanel = t.gameObject;
                return _cachedPricingQuotePanel;
            }
        }
        return null;
    }

    void Awake()
    {
        _onPricingChanged = OnPricingChanged;
        _onPriceEvent = _ => OnPricingChanged();

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        CancelPreviewRefresh();
        ReleasePageTextures();
        if (Instance == this)
            Instance = null;
    }

    void OnEnable()
    {
        if (PricingManager.Instance != null)
        {
            PricingManager.Instance.OnTotalPriceChanged.AddListener(_onPricingChanged);
            PricingManager.Instance.OnPriceAdded.AddListener(_onPriceEvent);
            PricingManager.Instance.OnPriceRemoved.AddListener(_onPriceEvent);
            PricingManager.Instance.OnPriceUpdated.AddListener(_onPriceEvent);
            PricingManager.Instance.OnBatchPricesAdded.AddListener(_onPricingChanged);
        }

        UI_ClientMetaData.OnClosed.AddListener(OnClientDataClosed);
    }

    void OnDisable()
    {
        if (PricingManager.Instance != null)
        {
            PricingManager.Instance.OnTotalPriceChanged.RemoveListener(_onPricingChanged);
            PricingManager.Instance.OnPriceAdded.RemoveListener(_onPriceEvent);
            PricingManager.Instance.OnPriceRemoved.RemoveListener(_onPriceEvent);
            PricingManager.Instance.OnPriceUpdated.RemoveListener(_onPriceEvent);
            PricingManager.Instance.OnBatchPricesAdded.RemoveListener(_onPricingChanged);
        }

        UI_ClientMetaData.OnClosed.RemoveListener(OnClientDataClosed);
        CancelPreviewRefresh();
    }

    void LateUpdate()
    {
        if (!isActiveAndEnabled || _pageScrollRt == null || _previewLoading)
            return;

        // Watch the outer page frame only — never the viewport, which ScrollRect
        // can resize when scrollbar chrome is applied.
        float w = _pageScrollRt.rect.width;
        float h = _pageScrollRt.rect.height;
        if (Mathf.Abs(w - _lastScrollOuterW) < 0.5f && Mathf.Abs(h - _lastScrollOuterH) < 0.5f)
            return;

        _lastScrollOuterW = w;
        _lastScrollOuterH = h;
        ApplyPageZoom(preserveScroll: true);
    }

    void Update()
    {
        UpdatePageViewportHover();
        HandlePageWheel();
        HandlePageArrowPan();
        HandleOpenOptionDropdownIdle();

        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        if (_popoverHost != null && _popoverHost.gameObject.activeSelf)
        {
            // Esc closes an open options dropdown first, then the popover.
            if (CloseOpenOptionDropdown())
                return;
            ClosePopover();
            return;
        }

        if (UI_ClientMetaData.IsOpen)
        {
            UI_ClientMetaData.Close();
            return;
        }
        if (UI_DialogPrompt.IsOpen)
            return;
        if (UI_GeneralLoadingScreen.instance != null &&
            UI_GeneralLoadingScreen.instance.gameObject.activeInHierarchy)
            return;

        Close();
    }

    void OnPricingChanged()
    {
        if (_suppressAutoPreviewRefresh || _previewLoading || _previewBuildRoutine != null)
        {
            Debug.Log(
                $"[ProposalPreview] ignore pricing event (suppress={_suppressAutoPreviewRefresh} loading={_previewLoading} bake={_previewBuildRoutine != null})");
            return;
        }

        RefreshLiveData(syncSalesRep: false);
        SchedulePreviewRefresh(forceVisuals: false, reason: "pricing");
    }

    void OnClientDataClosed()
    {
        var cg = GetComponent<CanvasGroup>();
        if (cg != null)
            cg.interactable = true;

        RefreshLiveData(syncSalesRep: true);

        // Don't rebuild the PDF if they opened Client Data and closed without edits.
        if (!UI_ClientMetaData.LastCloseHadChanges)
        {
            SetStatus("Preview up to date — click highlighted fields to edit");
            return;
        }

        SchedulePreviewRefresh(forceVisuals: false, reason: "client-data");
    }

    void RefreshLiveData(bool syncSalesRep = false)
    {
        if (_model == null)
            _model = ProposalPreviewModel.Capture();
        else
            _model.RefreshLive(syncSalesRep);
    }

    void EnsureBlocksRaycasts()
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = uiLayer;
        }

        var rootImage = GetComponent<Image>();
        if (rootImage == null)
            rootImage = gameObject.AddComponent<Image>();
        rootImage.color = Theme.Dimmer;
        rootImage.raycastTarget = true;

        var cg = GetComponent<CanvasGroup>();
        if (cg == null)
            cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = true;
        cg.interactable = true;
        cg.alpha = 1f;

        var canvas = GetComponent<Canvas>();
        if (canvas != null)
            canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, 280);
    }

    void BuildUi()
    {
        if (_built)
            return;
        _built = true;
        _root = transform as RectTransform;
        StretchFull(_root);

        var chrome = CreatePanel("Chrome", _root, Theme.Shell);
        var chromeRt = chrome.GetComponent<RectTransform>();
        chromeRt.anchorMin = new Vector2(0.5f, 0f);
        chromeRt.anchorMax = new Vector2(0.5f, 1f);
        chromeRt.pivot = new Vector2(0.5f, 0.5f);
        chromeRt.sizeDelta = new Vector2(1040f, -48f);
        chromeRt.anchoredPosition = Vector2.zero;

        var chromeLayout = chrome.AddComponent<VerticalLayoutGroup>();
        chromeLayout.padding = new RectOffset(20, 20, 14, 14);
        chromeLayout.spacing = 10f;
        chromeLayout.childAlignment = TextAnchor.UpperCenter;
        chromeLayout.childControlHeight = true;
        chromeLayout.childControlWidth = true;
        chromeLayout.childForceExpandHeight = false;
        chromeLayout.childForceExpandWidth = true;

        // Header: title + status on the left, Close on the right (no overlapping lines)
        var header = CreatePanel("Header", chrome.transform, Color.clear);
        var headerLe = header.AddComponent<LayoutElement>();
        headerLe.preferredHeight = 56f;
        headerLe.flexibleHeight = 0f;
        headerLe.minHeight = 56f;
        header.GetComponent<Image>().raycastTarget = false;
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(4, 4, 2, 2);
        headerLayout.spacing = 12f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandHeight = false;

        var titleBlock = new GameObject("TitleBlock", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        titleBlock.transform.SetParent(header.transform, false);
        var titleBlockLe = titleBlock.GetComponent<LayoutElement>();
        titleBlockLe.flexibleWidth = 1f;
        titleBlockLe.minWidth = 200f;
        titleBlockLe.preferredHeight = 52f;
        var tb = titleBlock.GetComponent<VerticalLayoutGroup>();
        tb.spacing = 2f;
        tb.childAlignment = TextAnchor.MiddleLeft;
        tb.childControlHeight = true;
        tb.childControlWidth = true;
        tb.childForceExpandHeight = false;
        tb.childForceExpandWidth = true;

        var titleLabel = CreateLabel(titleBlock.transform, "Sales proposal", 20f, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft, -1f, 26f);
        titleLabel.color = Theme.Ink;
        titleLabel.enableWordWrapping = false;
        titleLabel.overflowMode = TextOverflowModes.Ellipsis;

        _statusLabel = CreateLabel(titleBlock.transform, "Click highlighted areas on the page to edit", 12f,
            FontStyles.Normal, TextAlignmentOptions.MidlineLeft, -1f, 18f);
        _statusLabel.color = Theme.InkMuted;
        _statusLabel.enableWordWrapping = false;
        _statusLabel.overflowMode = TextOverflowModes.Ellipsis;

        CreateGhostButton(header.transform, "Close", Close, 88f, 36f);

        // Desk: PDF page + slim side rail (only for actions that don't map cleanly onto the page)
        var stage = CreatePanel("Stage", chrome.transform, Theme.Desk);
        var stageLe = stage.AddComponent<LayoutElement>();
        stageLe.flexibleHeight = 1f;
        stageLe.minHeight = 420f;
        stageLe.preferredHeight = 700f;
        var stageLayout = stage.AddComponent<HorizontalLayoutGroup>();
        stageLayout.padding = new RectOffset(16, 12, 16, 16);
        stageLayout.spacing = 12f;
        stageLayout.childAlignment = TextAnchor.UpperCenter;
        stageLayout.childControlHeight = true;
        stageLayout.childControlWidth = true;
        stageLayout.childForceExpandHeight = true;
        stageLayout.childForceExpandWidth = false;

        var pageScroll = new GameObject("PageScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        pageScroll.transform.SetParent(stage.transform, false);
        pageScroll.GetComponent<Image>().color = Theme.Paper;
        pageScroll.GetComponent<Image>().raycastTarget = true;
        var pageScrollLe = pageScroll.GetComponent<LayoutElement>();
        pageScrollLe.flexibleWidth = 1f;
        pageScrollLe.flexibleHeight = 1f;
        pageScrollLe.minWidth = 420f;
        pageScrollLe.minHeight = 360f;
        _pageScrollRt = pageScroll.GetComponent<RectTransform>();

        // Viewport starts full; Permanent scrollbars inset it once and keep it stable
        // (never toggle AutoHide — that resizes the viewport and fights fit math).
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(pageScroll.transform, false);
        _pageViewportRt = viewport.GetComponent<RectTransform>();
        StretchFull(_pageViewportRt);
        viewport.GetComponent<Image>().color = Theme.Paper;
        viewport.GetComponent<Image>().raycastTarget = true;

        var content = new GameObject("PageHost", typeof(RectTransform), typeof(Image));
        content.transform.SetParent(viewport.transform, false);
        _pageHostRt = content.GetComponent<RectTransform>();
        _pageHostRt.anchorMin = new Vector2(0f, 1f);
        _pageHostRt.anchorMax = new Vector2(0f, 1f);
        _pageHostRt.pivot = new Vector2(0f, 1f);
        _pageHostRt.anchoredPosition = Vector2.zero;
        var hostImg = content.GetComponent<Image>();
        hostImg.color = Theme.Paper;
        hostImg.raycastTarget = true;

        var pageFrame = CreatePanel("PageFrame", content.transform, Theme.Placeholder);
        _pageFrameRt = pageFrame.GetComponent<RectTransform>();
        _pageFrameRt.anchorMin = new Vector2(0f, 1f);
        _pageFrameRt.anchorMax = new Vector2(0f, 1f);
        _pageFrameRt.pivot = new Vector2(0f, 1f);
        _pageFrameRt.anchoredPosition = new Vector2(PageViewPadding, -PageViewPadding);

        _pageImage = pageFrame.GetComponent<Image>();
        _pageImage.color = Theme.Placeholder;
        _pageImage.preserveAspect = false;
        _pageImage.type = Image.Type.Simple;
        _pageImage.raycastTarget = true;

        var hotspotGo = new GameObject("Hotspots", typeof(RectTransform));
        hotspotGo.transform.SetParent(pageFrame.transform, false);
        _hotspotLayer = hotspotGo.GetComponent<RectTransform>();
        StretchFull(_hotspotLayer);

        _pageScroll = pageScroll.GetComponent<ScrollRect>();
        _pageScroll.viewport = _pageViewportRt;
        _pageScroll.content = _pageHostRt;
        _pageScroll.horizontal = true;
        _pageScroll.vertical = true;
        _pageScroll.movementType = ScrollRect.MovementType.Clamped;
        // Plain wheel/trackpad scroll pans via ScrollRect; Ctrl/pinch zooms in HandlePageWheel.
        _pageScroll.scrollSensitivity = 40f;
        _pageScroll.inertia = true;
        _pageScroll.decelerationRate = 0.135f;

        _pageScrollbarV = CreatePageScrollbar(pageScroll.transform, vertical: true);
        _pageScrollbarH = CreatePageScrollbar(pageScroll.transform, vertical: false);
        _pageScroll.verticalScrollbar = _pageScrollbarV;
        _pageScroll.horizontalScrollbar = _pageScrollbarH;
        // Permanent only — never AutoHide (that resizes the viewport and fights fit math).
        _pageScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        _pageScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        _pageScroll.verticalScrollbarSpacing = 0f;
        _pageScroll.horizontalScrollbarSpacing = 0f;

        BuildLoadingOverlay(pageScroll.transform);
        BuildSideRail(stage.transform);
        ApplyPageZoom(preserveScroll: false);

        // Bottom nav: Previous / dots / pages / Next · zoom · Export
        var nav = CreatePanel("Nav", chrome.transform, Color.clear);
        nav.GetComponent<Image>().raycastTarget = false;
        var navLe = nav.AddComponent<LayoutElement>();
        navLe.preferredHeight = 52f;
        navLe.flexibleHeight = 0f;
        navLe.minHeight = 52f;
        var navLayout = nav.AddComponent<HorizontalLayoutGroup>();
        navLayout.padding = new RectOffset(4, 4, 2, 2);
        navLayout.spacing = 10f;
        navLayout.childAlignment = TextAnchor.MiddleCenter;
        navLayout.childForceExpandWidth = false;
        navLayout.childControlWidth = true;
        navLayout.childControlHeight = true;

        CreateGhostButton(nav.transform, "Previous", () => ShowPage(_pageIndex - 1), 100f, 36f);

        _dotsHost = new GameObject("Dots", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement))
            .GetComponent<RectTransform>();
        _dotsHost.SetParent(nav.transform, false);
        _dotsHost.GetComponent<LayoutElement>().preferredWidth = 96f;
        var dotsLayout = _dotsHost.GetComponent<HorizontalLayoutGroup>();
        dotsLayout.spacing = 8f;
        dotsLayout.childAlignment = TextAnchor.MiddleCenter;
        dotsLayout.childForceExpandWidth = false;

        _pageLabel = CreateLabel(nav.transform, "— / —", 13f, FontStyles.Normal, TextAlignmentOptions.Center, 64f, 20f);
        _pageLabel.color = Theme.InkMuted;

        CreateGhostButton(nav.transform, "Next", () => ShowPage(_pageIndex + 1), 88f, 36f);

        BuildZoomControls(nav.transform);

        var navSpacer = new GameObject("NavSpacer", typeof(RectTransform), typeof(LayoutElement));
        navSpacer.transform.SetParent(nav.transform, false);
        navSpacer.GetComponent<LayoutElement>().flexibleWidth = 1f;

        CreatePrimaryButton(nav.transform, "Export PDF", ExportPdf, 160f, 40f);

        // Transparent container only — never put a Button here or nested inputs
        // (Sales Rep, notes, etc.) lose focus / clicks to the parent Selectable.
        _popoverHost = CreatePanel("PopoverHost", _root, Color.clear).GetComponent<RectTransform>();
        StretchFull(_popoverHost);
        _popoverHost.GetComponent<Image>().raycastTarget = false;
        _popoverHost.gameObject.SetActive(false);

        RebuildPageDots(0);
        RebuildHotspots();
        EnsureBlocksRaycasts();
        TMP_RuntimeFontRepair.RepairAll();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(chromeRt);
    }

    #region PDF preview

    void SchedulePreviewRefresh(bool forceVisuals = false, string reason = "unspecified")
    {
        if (!isActiveAndEnabled)
            return;

        bool bakeBusy = _suppressAutoPreviewRefresh || _previewLoading || _previewBuildRoutine != null;
        Debug.Log(
            $"[ProposalPreview] schedule reason={reason} force={forceVisuals} " +
            $"busy={bakeBusy} gen={_previewGenerationId}");

        if (bakeBusy)
        {
            if (reason != "pricing")
            {
                _queuedUserPreviewRefresh = true;
                _queuedUserForceVisuals |= forceVisuals;
            }
            return;
        }

        if (forceVisuals)
            _pendingForceVisuals = true;

        if (_previewDebounce != null)
            StopCoroutine(_previewDebounce);
        _previewDebounce = StartCoroutine(PreviewDebounceRoutine());
    }

    IEnumerator PreviewDebounceRoutine()
    {
        Debug.Log($"[ProposalPreview] debounce {PreviewDebounceSeconds:0.00}s then bake");
        yield return new WaitForSecondsRealtime(PreviewDebounceSeconds);
        _previewDebounce = null;
        bool forceVisuals = _pendingForceVisuals;
        _pendingForceVisuals = false;
        BeginPreviewGeneration(forceVisuals);
    }

    void FinishPreviewBake(int generationId)
    {
        if (generationId != _previewGenerationId)
            return;

        _previewBuildRoutine = null;
        _suppressAutoPreviewRefresh = false;
        Debug.Log(
            $"[ProposalPreview] bake finished gen={generationId} queuedUser={_queuedUserPreviewRefresh}");

        if (!_queuedUserPreviewRefresh || !isActiveAndEnabled)
            return;

        bool force = _queuedUserForceVisuals;
        _queuedUserPreviewRefresh = false;
        _queuedUserForceVisuals = false;
        SchedulePreviewRefresh(force, reason: "queued-user");
    }

    void CancelPreviewRefresh()
    {
        if (_previewDebounce != null)
        {
            StopCoroutine(_previewDebounce);
            _previewDebounce = null;
        }
        if (_previewBuildRoutine != null)
        {
            StopCoroutine(_previewBuildRoutine);
            _previewBuildRoutine = null;
        }
        _previewGenerationId++;
        _pendingForceVisuals = false;
        _suppressAutoPreviewRefresh = false;
        _queuedUserPreviewRefresh = false;
        SetPreviewLoading(false);

        var generator = FindAnyObjectByType<ProposalPDFGenerator>(FindObjectsInactive.Include);
        generator?.CancelPreview();
    }

    void BeginPreviewGeneration(bool forceVisuals)
    {
        if (!isActiveAndEnabled)
            return;

        _suppressAutoPreviewRefresh = true;
        if (_previewDebounce != null)
        {
            StopCoroutine(_previewDebounce);
            _previewDebounce = null;
        }

        if (_previewBuildRoutine != null)
        {
            StopCoroutine(_previewBuildRoutine);
            _previewBuildRoutine = null;
        }

        var generator = FindAnyObjectByType<ProposalPDFGenerator>(FindObjectsInactive.Include);
        generator?.CancelPreview();

        Debug.Log($"[ProposalPreview] start bake forceVisuals={forceVisuals} gen={_previewGenerationId + 1}");
        _previewBuildRoutine = StartCoroutine(BeginPreviewGenerationRoutine(forceVisuals));
    }

    IEnumerator BeginPreviewGenerationRoutine(bool forceVisuals)
    {
        int generationId = ++_previewGenerationId;
        bool firstLoad = _pageSprites.Count == 0;
        SetPreviewLoading(true, firstLoad
            ? "Generating proposal preview…"
            : "Updating preview…");

        // Let the loading overlay paint before heavy Capture / PDF work.
        yield return null;
        if (generationId != _previewGenerationId || !isActiveAndEnabled)
        {
            FinishPreviewBake(generationId);
            yield break;
        }

        if (_model == null)
            _model = ProposalPreviewModel.Capture();
        else
            _model.RefreshLive();

        yield return null;
        if (generationId != _previewGenerationId || !isActiveAndEnabled)
        {
            FinishPreviewBake(generationId);
            yield break;
        }

        _model.PersistEditableFields();

        var generator = FindAnyObjectByType<ProposalPDFGenerator>(FindObjectsInactive.Include);
        if (generator == null)
        {
            SetPreviewLoading(false);
            SetStatus("Sales proposal generator is missing from the scene.");
            FinishPreviewBake(generationId);
            yield break;
        }

        bool reuseVisuals = !forceVisuals;
        bool completed = false;
        bool okResult = false;
        string pathResult = null;
        string errResult = null;

        generator.GeneratePreviewPdf((ok, path, err) =>
        {
            completed = true;
            okResult = ok;
            pathResult = path;
            errResult = err;
        }, reuseVisuals);

        while (!completed)
        {
            if (generationId != _previewGenerationId || !isActiveAndEnabled)
            {
                generator.CancelPreview();
                FinishPreviewBake(generationId);
                yield break;
            }
            yield return null;
        }

        if (generationId != _previewGenerationId || !isActiveAndEnabled)
        {
            FinishPreviewBake(generationId);
            yield break;
        }

        if (!okResult || string.IsNullOrEmpty(pathResult))
        {
            SetPreviewLoading(false);
            if (!string.Equals(errResult, "cancelled", StringComparison.OrdinalIgnoreCase))
                SetStatus(string.IsNullOrWhiteSpace(errResult) ? "Preview failed." : errResult);
            FinishPreviewBake(generationId);
            yield break;
        }

        SetPreviewLoading(true, "Preparing page images…");
        yield return null;

        var textures = new List<Texture2D>();
        Exception rasterError = null;
        var raster = ProposalPdfPreviewRasterizer.RasterizePagesRoutine(
            pathResult, textures, ProposalPdfPreviewRasterizer.PreviewDpi);
        while (true)
        {
            object current;
            try
            {
                if (!raster.MoveNext())
                    break;
                current = raster.Current;
            }
            catch (Exception e)
            {
                rasterError = e;
                break;
            }
            yield return current;
        }

        if (generationId != _previewGenerationId || !isActiveAndEnabled)
        {
            for (int i = 0; i < textures.Count; i++)
            {
                if (textures[i] != null)
                    Destroy(textures[i]);
            }
            FinishPreviewBake(generationId);
            yield break;
        }

        if (rasterError != null)
        {
            Debug.LogWarning($"Proposal preview rasterize failed: {rasterError.Message}");
            SetPreviewLoading(false);
            SetStatus("Could not rasterize preview: " + rasterError.Message);
            FinishPreviewBake(generationId);
            yield break;
        }

        try
        {
            _hotspots.Clear();
            if (_model != null)
                _hotspots.AddRange(ProposalPdfPreviewHotspotFinder.Find(pathResult, _model));
            ApplyPageTextures(textures);
            SetPreviewLoading(false);
            SetStatus(_hotspots.Count > 0
                ? "Preview up to date — click highlighted fields to edit"
                : "Preview up to date");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Proposal preview apply failed: {e.Message}");
            SetPreviewLoading(false);
            SetStatus("Could not apply preview: " + e.Message);
        }

        FinishPreviewBake(generationId);
    }

    void ApplyPageTextures(List<Texture2D> textures)
    {
        ReleasePageTextures();

        if (textures == null || textures.Count == 0)
        {
            _hotspots.Clear();
            RebuildPageDots(0);
            ShowPage(0);
            return;
        }

        for (int i = 0; i < textures.Count; i++)
        {
            var tex = textures[i];
            if (tex == null)
                continue;
            _pageTextures.Add(tex);
            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
            sprite.name = "ProposalPage_" + i;
            _pageSprites.Add(sprite);
        }

        RebuildPageDots(_pageSprites.Count);
        ShowPage(Mathf.Clamp(_pageIndex, 0, Mathf.Max(0, _pageSprites.Count - 1)));
    }

    void ReleasePageTextures()
    {
        if (_pageImage != null)
        {
            _pageImage.sprite = null;
            _pageImage.color = Theme.Placeholder;
        }

        for (int i = 0; i < _pageSprites.Count; i++)
        {
            if (_pageSprites[i] != null)
                Destroy(_pageSprites[i]);
        }
        _pageSprites.Clear();

        for (int i = 0; i < _pageTextures.Count; i++)
        {
            if (_pageTextures[i] != null)
                Destroy(_pageTextures[i]);
        }
        _pageTextures.Clear();
        // Hotspots are refreshed after the next successful rasterize.
    }

    void RebuildPageDots(int pageCount)
    {
        _pageDots.Clear();
        if (_dotsHost == null)
            return;

        for (int i = _dotsHost.childCount - 1; i >= 0; i--)
            Destroy(_dotsHost.GetChild(i).gameObject);

        for (int i = 0; i < pageCount; i++)
        {
            int page = i;
            var dot = new GameObject("Dot" + i, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            dot.transform.SetParent(_dotsHost, false);
            var img = dot.GetComponent<Image>();
            img.color = Theme.NavIdle;
            var dle = dot.GetComponent<LayoutElement>();
            dle.preferredWidth = 8f;
            dle.preferredHeight = 8f;
            var btn = dot.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = UnityEngine.UI.Selectable.Transition.None;
            btn.onClick.AddListener(() => ShowPage(page));
            _pageDots.Add(img);
        }
    }

    void ShowPage(int index)
    {
        int pageCount = _pageSprites.Count;
        if (pageCount <= 0)
        {
            _pageIndex = 0;
            if (_pageImage != null)
            {
                _pageImage.sprite = null;
                _pageImage.color = Theme.Placeholder;
            }
            if (_pageLabel != null)
                _pageLabel.text = "— / —";
            for (int i = 0; i < _pageDots.Count; i++)
            {
                if (_pageDots[i] != null)
                    _pageDots[i].color = Theme.NavIdle;
            }
            RebuildHotspots();
            return;
        }

        _pageIndex = Mathf.Clamp(index, 0, pageCount - 1);

        var sprite = _pageSprites[_pageIndex];
        if (_pageImage != null)
        {
            _pageImage.sprite = sprite;
            _pageImage.color = Color.white;
            _pageImage.preserveAspect = false;
        }

        if (sprite != null && sprite.rect.height > 0.01f)
            _pageAspectRatio = sprite.rect.width / sprite.rect.height;
        else
            _pageAspectRatio = A4Aspect;

        ApplyPageZoom(preserveScroll: false);

        if (_pageLabel != null)
            _pageLabel.text = $"{_pageIndex + 1} / {pageCount}";

        for (int i = 0; i < _pageDots.Count; i++)
        {
            if (_pageDots[i] != null)
                _pageDots[i].color = i == _pageIndex ? Theme.Accent : Theme.NavIdle;
        }

        RebuildHotspots();
        ClosePopover();
    }

    void BuildZoomControls(Transform nav)
    {
        CreateGhostButton(nav, "−", () => AdjustPageZoom(-PageZoomStep), 40f, 36f);
        _zoomPercentLabel = CreateLabel(nav, "100%", 12f, FontStyles.Bold, TextAlignmentOptions.Center, 52f, 20f);
        _zoomPercentLabel.color = Theme.InkMuted;
        CreateGhostButton(nav, "+", () => AdjustPageZoom(PageZoomStep), 40f, 36f);
        CreateGhostButton(nav, "Fit", ResetPageZoom, 52f, 36f);
        UpdateZoomPercentLabel();
    }

    void AdjustPageZoom(float delta)
    {
        if (_pageSprites.Count == 0 || _previewLoading)
            return;

        float next = Mathf.Clamp(_pageZoom + delta, PageZoomMin, PageZoomMax);
        next = Mathf.Round(next * 10f) / 10f;
        if (Mathf.Approximately(next, _pageZoom))
            return;

        _pageZoom = next;
        ApplyPageZoom(preserveScroll: true);
        UpdateZoomPercentLabel();
    }

    void ResetPageZoom()
    {
        if (Mathf.Approximately(_pageZoom, 1f))
        {
            ApplyPageZoom(preserveScroll: false);
            UpdateZoomPercentLabel();
            return;
        }

        _pageZoom = 1f;
        ApplyPageZoom(preserveScroll: false);
        UpdateZoomPercentLabel();
    }

    void UpdateZoomPercentLabel()
    {
        if (_zoomPercentLabel != null)
            _zoomPercentLabel.text = Mathf.RoundToInt(_pageZoom * 100f) + "%";
    }

    void ApplyPageZoom(bool preserveScroll)
    {
        if (_pageFrameRt == null || _pageHostRt == null || _pageViewportRt == null || _pageScroll == null)
            return;

        float prevH = preserveScroll ? _pageScroll.horizontalNormalizedPosition : 0.5f;
        float prevV = preserveScroll ? _pageScroll.verticalNormalizedPosition : 1f;

        Canvas.ForceUpdateCanvases();

        float vw = _pageViewportRt.rect.width;
        float vh = _pageViewportRt.rect.height;
        if (vw < 8f || vh < 8f)
            return;

        float aspect = _pageAspectRatio > 0.01f ? _pageAspectRatio : A4Aspect;
        float availW = Mathf.Max(48f, vw - PageViewPadding * 2f);
        float availH = Mathf.Max(48f, vh - PageViewPadding * 2f);

        // Fit entire page in the (fixed-inset) viewport at 100%, then scale by zoom.
        float fitW = availW;
        float fitH = fitW / aspect;
        if (fitH > availH)
        {
            fitH = availH;
            fitW = fitH * aspect;
        }

        float scale = Mathf.Clamp(_pageZoom, PageZoomMin, PageZoomMax);
        float pageW = fitW * scale;
        float pageH = fitH * scale;

        float hostW = Mathf.Max(vw, pageW + PageViewPadding * 2f);
        float hostH = Mathf.Max(vh, pageH + PageViewPadding * 2f);

        _pageHostRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, hostW);
        _pageHostRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, hostH);

        _pageFrameRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, pageW);
        _pageFrameRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, pageH);

        float pageX = (hostW - pageW) * 0.5f;
        float pageY = hostH > vh + 0.5f
            ? -PageViewPadding
            : -(hostH - pageH) * 0.5f;
        _pageFrameRt.anchoredPosition = new Vector2(pageX, pageY);

        // Axes stay enabled; Clamped ScrollRect no-ops when content fits.
        _pageScroll.horizontal = true;
        _pageScroll.vertical = true;

        Canvas.ForceUpdateCanvases();
        bool canScrollH = hostW > vw + 1f;
        _pageScroll.horizontalNormalizedPosition = preserveScroll ? prevH : (canScrollH ? 0.5f : 0f);
        _pageScroll.verticalNormalizedPosition = preserveScroll ? prevV : 1f;

        UpdateZoomPercentLabel();
    }

    void HandlePageWheel()
    {
        bool zoomGesture = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
            || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

        // Suppress ScrollRect pan while pinching (Windows sends pinch as Ctrl+wheel).
        if (_pageScroll != null)
            _pageScroll.scrollSensitivity = zoomGesture ? 0f : 40f;

        if (!_pointerOverPageViewport || _previewLoading)
            return;
        if (_popoverHost != null && _popoverHost.gameObject.activeSelf)
            return;
        if (UI_ClientMetaData.IsOpen)
            return;

        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f)
            return;

        if (zoomGesture)
            AdjustPageZoom(wheel > 0f ? PageZoomStep : -PageZoomStep);
        // Plain wheel / two-finger scroll: ScrollRect.OnScroll pans using scrollSensitivity.
    }

    void HandlePageArrowPan()
    {
        if (!_pointerOverPageViewport || _previewLoading || _pageScroll == null)
            return;
        if (_pageZoom <= 1.01f)
            return;
        if (_popoverHost != null && _popoverHost.gameObject.activeSelf)
            return;
        if (UI_ClientMetaData.IsOpen)
            return;

        float dx = 0f;
        float dy = 0f;
        if (Input.GetKey(KeyCode.LeftArrow))
            dx -= 1f;
        if (Input.GetKey(KeyCode.RightArrow))
            dx += 1f;
        if (Input.GetKey(KeyCode.UpArrow))
            dy += 1f;
        if (Input.GetKey(KeyCode.DownArrow))
            dy -= 1f;
        if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) < 0.01f)
            return;

        float vw = Mathf.Max(1f, _pageViewportRt.rect.width);
        float vh = Mathf.Max(1f, _pageViewportRt.rect.height);
        float speed = 480f * Time.unscaledDeltaTime;
        float rangeH = Mathf.Max(1f, _pageHostRt.rect.width - vw);
        float rangeV = Mathf.Max(1f, _pageHostRt.rect.height - vh);

        if (Mathf.Abs(dx) > 0.01f)
            _pageScroll.horizontalNormalizedPosition = Mathf.Clamp01(
                _pageScroll.horizontalNormalizedPosition + dx * speed / rangeH);
        if (Mathf.Abs(dy) > 0.01f)
            _pageScroll.verticalNormalizedPosition = Mathf.Clamp01(
                _pageScroll.verticalNormalizedPosition + dy * speed / rangeV);
    }

    void UpdatePageViewportHover()
    {
        if (!isActiveAndEnabled || _pageViewportRt == null)
        {
            _pointerOverPageViewport = false;
            return;
        }

        var canvas = GetComponent<Canvas>();
        Camera eventCam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            eventCam = canvas.worldCamera;

        _pointerOverPageViewport = RectTransformUtility.RectangleContainsScreenPoint(
            _pageViewportRt, Input.mousePosition, eventCam);
    }

    Scrollbar CreatePageScrollbar(Transform parent, bool vertical)
    {
        string name = vertical ? "Scrollbar Vertical" : "Scrollbar Horizontal";
        var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        root.transform.SetParent(parent, false);
        root.layer = gameObject.layer;

        var rt = root.GetComponent<RectTransform>();
        if (vertical)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(-ScrollbarThickness, ScrollbarThickness);
            rt.offsetMax = new Vector2(0f, 0f);
        }
        else
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(-ScrollbarThickness, ScrollbarThickness);
        }

        var track = root.GetComponent<Image>();
        track.color = new Color(0.88f, 0.89f, 0.91f, 0.95f);
        track.raycastTarget = true;

        var sliding = new GameObject("Sliding Area", typeof(RectTransform));
        sliding.transform.SetParent(root.transform, false);
        var slidingRt = sliding.GetComponent<RectTransform>();
        StretchFull(slidingRt, 2f);

        var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(sliding.transform, false);
        var handleRt = handleGo.GetComponent<RectTransform>();
        StretchFull(handleRt);
        var handleImg = handleGo.GetComponent<Image>();
        handleImg.color = new Color(0.55f, 0.57f, 0.60f, 1f);
        handleImg.raycastTarget = true;

        var bar = root.GetComponent<Scrollbar>();
        bar.handleRect = handleRt;
        bar.targetGraphic = handleImg;
        bar.direction = vertical
            ? Scrollbar.Direction.BottomToTop
            : Scrollbar.Direction.LeftToRight;
        bar.size = 0.3f;
        bar.numberOfSteps = 0;
        var colors = bar.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.95f, 0.95f, 0.96f, 1f);
        colors.pressedColor = new Color(0.9f, 0.9f, 0.92f, 1f);
        bar.colors = colors;
        root.SetActive(true);
        return bar;
    }

    /// <summary>
    /// Vertical scrollbar that appears when content overflows — cue that there's more to scroll.
    /// </summary>
    void AttachVerticalOverflowScrollbar(ScrollRect scrollRect, float thickness = 10f)
    {
        if (scrollRect == null)
            return;

        var root = new GameObject("Scrollbar Vertical", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        root.transform.SetParent(scrollRect.transform, false);
        root.layer = gameObject.layer;

        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(-thickness, 0f);
        rt.offsetMax = Vector2.zero;

        var track = root.GetComponent<Image>();
        track.color = new Color(0.90f, 0.91f, 0.93f, 0.98f);
        track.raycastTarget = true;

        var sliding = new GameObject("Sliding Area", typeof(RectTransform));
        sliding.transform.SetParent(root.transform, false);
        StretchFull(sliding.GetComponent<RectTransform>(), 2f);

        var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(sliding.transform, false);
        var handleRt = handleGo.GetComponent<RectTransform>();
        StretchFull(handleRt);
        var handleImg = handleGo.GetComponent<Image>();
        handleImg.color = new Color(0.55f, 0.57f, 0.60f, 1f);
        handleImg.raycastTarget = true;

        var bar = root.GetComponent<Scrollbar>();
        bar.handleRect = handleRt;
        bar.targetGraphic = handleImg;
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.size = 1f;
        bar.numberOfSteps = 0;
        var colors = bar.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.95f, 0.95f, 0.96f, 1f);
        colors.pressedColor = new Color(0.9f, 0.9f, 0.92f, 1f);
        bar.colors = colors;

        scrollRect.vertical = true;
        scrollRect.verticalScrollbar = bar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scrollRect.verticalScrollbarSpacing = -2f;
        root.transform.SetAsLastSibling();
    }

    void SetStatus(string text)
    {
        if (_statusLabel == null)
            return;

        // Idle hint when preview is ready; otherwise show the live status.
        string msg = text ?? "";
        if (msg.StartsWith("Preview up to date", StringComparison.OrdinalIgnoreCase))
            msg = "Click highlighted areas on the page to edit";

        _statusLabel.text = msg;
        _statusLabel.fontStyle = FontStyles.Normal;
        _statusLabel.color = Theme.InkMuted;
    }

    void BuildLoadingOverlay(Transform pageScroll)
    {
        var overlay = CreatePanel("LoadingOverlay", pageScroll, new Color(0.96f, 0.96f, 0.97f, 0.82f));
        _loadingOverlay = overlay;
        StretchFull(overlay.GetComponent<RectTransform>());
        overlay.GetComponent<Image>().raycastTarget = true;
        overlay.transform.SetAsLastSibling();

        var card = CreatePanel("LoadingCard", overlay.transform, Theme.Paper);
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(300f, 96f);

        var outline = card.AddComponent<Outline>();
        outline.effectColor = Theme.GhostBorder;
        outline.effectDistance = new Vector2(1f, -1f);

        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 20, 18);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        _loadingLabel = CreateLabel(card.transform, "Generating proposal preview…", 13f, FontStyles.Normal,
            TextAlignmentOptions.Center, -1f, 22f);
        _loadingLabel.color = Theme.InkMuted;
        _loadingLabel.enableWordWrapping = false;

        var track = CreatePanel("ProgressTrack", card.transform, Theme.Rule);
        var trackLe = track.AddComponent<LayoutElement>();
        trackLe.preferredHeight = 4f;
        trackLe.minHeight = 4f;
        trackLe.flexibleWidth = 1f;
        track.GetComponent<Image>().raycastTarget = false;

        var fill = new GameObject("ProgressFill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(track.transform, false);
        _loadingBarFill = fill.GetComponent<RectTransform>();
        _loadingBarFill.anchorMin = new Vector2(0f, 0f);
        _loadingBarFill.anchorMax = new Vector2(0.35f, 1f);
        _loadingBarFill.offsetMin = Vector2.zero;
        _loadingBarFill.offsetMax = Vector2.zero;
        var fillImg = fill.GetComponent<Image>();
        fillImg.color = new Color(0.35f, 0.38f, 0.42f, 1f);
        fillImg.raycastTarget = false;

        SetPreviewLoading(false);
    }

    void SetPreviewLoading(bool loading, string message = null)
    {
        _previewLoading = loading;

        if (!string.IsNullOrEmpty(message))
            _loadingBaseMessage = message;
        else if (loading && string.IsNullOrEmpty(_loadingBaseMessage))
            _loadingBaseMessage = "Generating proposal preview…";

        if (loading)
            SetStatus(_loadingBaseMessage);
        else if (!string.IsNullOrEmpty(message))
            SetStatus(message);

        if (_loadingLabel != null)
            _loadingLabel.text = loading ? _loadingBaseMessage : (_loadingBaseMessage ?? "");

        if (_loadingOverlay != null)
            _loadingOverlay.SetActive(loading);

        // Block clicks on stale hotspots while a new PDF is building.
        if (_hotspotLayer != null)
            _hotspotLayer.gameObject.SetActive(!loading && _pageSprites.Count > 0);

        if (loading)
        {
            if (_loadingAnimRoutine == null && isActiveAndEnabled)
                _loadingAnimRoutine = StartCoroutine(LoadingAnimRoutine());
        }
        else
        {
            if (_loadingAnimRoutine != null)
            {
                StopCoroutine(_loadingAnimRoutine);
                _loadingAnimRoutine = null;
            }
            ApplyPageZoom(preserveScroll: true);
            UpdateZoomPercentLabel();
        }
    }

    IEnumerator LoadingAnimRoutine()
    {
        // Indeterminate bar + simple ellipsis — normal loading affordance, no spinner.
        float t = 0f;
        int dotStep = 0;
        float nextDot = 0f;
        while (_previewLoading)
        {
            t += Time.unscaledDeltaTime;
            if (_loadingBarFill != null)
            {
                // Slide a short segment back and forth across the track.
                float u = Mathf.PingPong(t * 0.85f, 1f);
                float width = 0.34f;
                float start = Mathf.Lerp(0f, 1f - width, u);
                _loadingBarFill.anchorMin = new Vector2(start, 0f);
                _loadingBarFill.anchorMax = new Vector2(start + width, 1f);
                _loadingBarFill.offsetMin = Vector2.zero;
                _loadingBarFill.offsetMax = Vector2.zero;
            }

            if (_loadingLabel != null && t >= nextDot)
            {
                nextDot = t + 0.4f;
                dotStep = (dotStep + 1) % 4;
                string dots = new string('.', dotStep);
                string pad = new string(' ', 3 - dotStep); // keep label width roughly stable
                string baseMsg = _loadingBaseMessage ?? "";
                // Strip trailing ellipsis / dots from the base message before appending.
                baseMsg = baseMsg.TrimEnd('.', '…', ' ');
                _loadingLabel.text = baseMsg + dots + pad;
            }

            yield return null;
        }
        _loadingAnimRoutine = null;
    }

    void BuildSideRail(Transform stage)
    {
        var rail = CreatePanel("SideRail", stage, Theme.Shell);
        var railLe = rail.AddComponent<LayoutElement>();
        railLe.preferredWidth = 148f;
        railLe.minWidth = 148f;
        railLe.flexibleWidth = 0f;
        railLe.flexibleHeight = 1f;
        var railLayout = rail.AddComponent<VerticalLayoutGroup>();
        railLayout.padding = new RectOffset(10, 10, 12, 12);
        railLayout.spacing = 8f;
        railLayout.childAlignment = TextAnchor.UpperCenter;
        railLayout.childControlWidth = true;
        railLayout.childControlHeight = true;
        railLayout.childForceExpandWidth = true;
        railLayout.childForceExpandHeight = false;

        CreateLabel(rail.transform, "More", 12f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, -1f, 18f)
            .color = Theme.InkMuted;
        CreateHint(rail.transform, "Use these when the page highlight isn’t enough.");

        CreateGhostButton(rail.transform, "Options", EditOptions, -1f, 34f);
        CreateGhostButton(rail.transform, "Sales Rep", EditSalesRep, -1f, 34f);
        CreateGhostButton(rail.transform, "Client Data", EditClientData, -1f, 34f);
        CreateGhostButton(rail.transform, "Refresh visuals", () => BeginPreviewGeneration(forceVisuals: true), -1f, 34f);

        CreateLabel(rail.transform, "Prices", 12f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, -1f, 18f)
            .color = Theme.InkMuted;
        CreateGhostButton(rail.transform, "Update prices", UI_PricingBridge.Open, -1f, 34f);

        var spacer = new GameObject("RailSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(rail.transform, false);
        spacer.GetComponent<LayoutElement>().flexibleHeight = 1f;
    }

    /// <summary>
    /// Soft click targets over the PDF, placed from real text bounds extracted
    /// from the generated preview PDF (see <see cref="ProposalPdfPreviewHotspotFinder"/>).
    /// </summary>
    void RebuildHotspots()
    {
        if (_hotspotLayer == null)
            return;

        for (int i = _hotspotLayer.childCount - 1; i >= 0; i--)
            Destroy(_hotspotLayer.GetChild(i).gameObject);

        for (int i = 0; i < _hotspots.Count; i++)
        {
            var spot = _hotspots[i];
            if (spot.PageIndex != _pageIndex)
                continue;
            CreateHotspot(spot);
        }
    }

    void CreateHotspot(ProposalPreviewHotspot spot)
    {
        var go = new GameObject("Hotspot_" + spot.Kind, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(_hotspotLayer, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(spot.X0, spot.Y0);
        rt.anchorMax = new Vector2(spot.X1, spot.Y1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.color = Theme.Hotspot;
        img.raycastTarget = true;

        // Don't use Button + ScrollRect together — tiny pointer movement cancels onClick.
        // Pause page scrolling for the press and fire on pointer-up within a small drag.
        var click = go.AddComponent<ProposalHotspotClick>();
        click.Initialize(
            img,
            Theme.Hotspot,
            Theme.HotspotHover,
            Theme.HotspotPressed,
            _pageScroll,
            () => OpenEditorFor(spot.Kind));

        if (!string.IsNullOrEmpty(spot.Tooltip))
        {
            var tip = go.AddComponent<UI_HoverTooltip>();
            tip.SetText(spot.Tooltip);
        }
    }

    /// <summary>
    /// Clickable PDF region that stays responsive inside a ScrollRect (Button onClick
    /// is often swallowed by drag).
    /// </summary>
    sealed class ProposalHotspotClick : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        const float ClickSlopPixels = 40f;

        Image _image;
        Color _normal;
        Color _hover;
        Color _pressed;
        ScrollRect _scroll;
        UnityAction _onActivated;
        bool _inside;
        bool _holding;
        bool _scrollWasEnabled;
        Vector2 _downScreen;

        public void Initialize(
            Image image,
            Color normal,
            Color hover,
            Color pressed,
            ScrollRect scroll,
            UnityAction onActivated)
        {
            _image = image;
            _normal = normal;
            _hover = hover;
            _pressed = pressed;
            _scroll = scroll;
            _onActivated = onActivated;
            if (_image != null)
                _image.color = _normal;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _inside = true;
            if (!_holding && _image != null)
                _image.color = _hover;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _inside = false;
            if (!_holding && _image != null)
                _image.color = _normal;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            _holding = true;
            _downScreen = eventData.position;
            if (_image != null)
                _image.color = _pressed;

            if (_scroll != null)
            {
                _scrollWasEnabled = _scroll.enabled;
                _scroll.enabled = false;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_holding)
                return;

            _holding = false;
            RestoreScroll();

            if (_image != null)
                _image.color = _inside ? _hover : _normal;

            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            float slop = ClickSlopPixels * ClickSlopPixels;
            if ((eventData.position - _downScreen).sqrMagnitude > slop)
                return;

            _onActivated?.Invoke();
        }

        void OnDisable()
        {
            if (_holding)
            {
                _holding = false;
                RestoreScroll();
            }
            if (_image != null)
                _image.color = _normal;
            _inside = false;
        }

        void RestoreScroll()
        {
            if (_scroll != null)
                _scroll.enabled = _scrollWasEnabled;
        }
    }

    void OpenEditorFor(ProposalPreviewEditKind kind)
    {
        switch (kind)
        {
            case ProposalPreviewEditKind.SalesRep:
                EditSalesRep();
                break;
            case ProposalPreviewEditKind.ClientData:
            case ProposalPreviewEditKind.Project:
                EditClientData();
                break;
            case ProposalPreviewEditKind.ConfigTitle:
                EditConfigTitle();
                break;
            case ProposalPreviewEditKind.Options:
                EditOptions();
                break;
            case ProposalPreviewEditKind.Discount:
                EditDiscount();
                break;
            case ProposalPreviewEditKind.Notes:
                EditNotes();
                break;
        }
    }

    #endregion

    #region Editors

    void EditSalesRep()
    {
        if (_model == null)
            _model = ProposalPreviewModel.Capture();

        string beforeName = _model.SalesRepName ?? "";
        string beforePhone = _model.SalesRepPhone ?? "";
        string beforeEmail = _model.SalesRepEmail ?? "";
        SetStatus("Editing sales rep…");

        OpenTextPopover("Sales rep",
            new[]
            {
                ("Name", beforeName),
                ("Phone", beforePhone),
                ("Email", beforeEmail),
            },
            values =>
            {
                string name = values[0] ?? "";
                string phone = values.Length > 1 ? values[1] ?? "" : "";
                string email = values.Length > 2 ? values[2] ?? "" : "";
                _model.SalesRepName = name;
                _model.SalesRepPhone = phone;
                _model.SalesRepEmail = email;
                _model.PersistEditableFields();
                if (!string.Equals(name, beforeName, StringComparison.Ordinal)
                    || !string.Equals(phone, beforePhone, StringComparison.Ordinal)
                    || !string.Equals(email, beforeEmail, StringComparison.Ordinal))
                    SchedulePreviewRefresh(forceVisuals: false);
                else
                    SetStatus("Preview up to date — click highlighted fields to edit");
            });
    }

    void EditClientData()
    {
        ClosePopover();
        // Client Metadata sorts above us (299 vs 280), but keep the proposal
        // from eating hover/scroll while that dialog is open.
        var cg = GetComponent<CanvasGroup>();
        if (cg != null)
            cg.interactable = false;
        UI_ClientMetaData.Open();
    }

    void EditConfigTitle()
    {
        if (_model == null)
            _model = ProposalPreviewModel.Capture();

        string before = _model.ConfigName ?? "";

        OpenTextPopover("Configuration title",
            new[] { ("Title", before) },
            values =>
            {
                if (string.IsNullOrWhiteSpace(values[0]))
                {
                    ProposalPreviewModel.ConfigNameOverride = null;
                    _model.ConfigName = ExportPaths.HasSavedRoomName()
                        ? ExportPaths.GetRoomExportName()
                        : "Configuration";
                }
                else
                {
                    _model.ConfigName = values[0].Trim();
                    ProposalPreviewModel.ConfigNameOverride = _model.ConfigName;
                }
                if (!string.Equals(_model.ConfigName ?? "", before, StringComparison.Ordinal))
                    SchedulePreviewRefresh(forceVisuals: false);
            },
            footerHint: "Export normally uses the saved room name. Clear the title to remove this override.");
    }

    void EditDiscount()
    {
        if (_model == null)
            _model = ProposalPreviewModel.Capture();

        float before = _model.DiscountPercentage;
        OpenTextPopover("Discount %",
            new[] { ("Percent", before.ToString("0.##", CultureInfo.InvariantCulture)) },
            values =>
            {
                if (float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float d))
                    _model.DiscountPercentage = Mathf.Max(0f, d);
                _model.PersistEditableFields();
                if (!Mathf.Approximately(_model.DiscountPercentage, before))
                    SchedulePreviewRefresh(forceVisuals: false);
            });
    }

    void EditNotes()
    {
        if (_model == null)
            _model = ProposalPreviewModel.Capture();

        string n1 = _model.Note1 ?? "";
        string n2 = _model.Note2 ?? "";
        string n3 = _model.Note3 ?? "";

        OpenTextPopover("Notes",
            new[]
            {
                ("Note 1", n1, true),
                ("Note 2", n2, true),
                ("Acceptance", n3, true),
            },
            values =>
            {
                _model.Note1 = values[0];
                _model.Note2 = values[1];
                _model.Note3 = values[2];
                _model.PersistEditableFields();
                if (!string.Equals(_model.Note1 ?? "", n1, StringComparison.Ordinal)
                    || !string.Equals(_model.Note2 ?? "", n2, StringComparison.Ordinal)
                    || !string.Equals(_model.Note3 ?? "", n3, StringComparison.Ordinal))
                    SchedulePreviewRefresh(forceVisuals: false);
            });
    }

    void EditOptions()
    {
        EnsurePricingOptionsInitialized();
        DropdownPopulator.RestoreAllPersistedSelections();
        if (_model == null)
            _model = ProposalPreviewModel.Capture();
        else
            _model.RefreshLive();

        ClosePopover();
        _popoverHost.gameObject.SetActive(true);
        CreatePopoverBackdrop();

        var panel = CreatePanel("OptionsPopover", _popoverHost, Theme.Popover);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(480f, 520f);
        // Raycast-blocking image only — never a Button/Selectable, or child
        // inputs and pickers lose focus / clicks to the parent.
        panel.GetComponent<Image>().raycastTarget = true;

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 16);
        layout.spacing = 8f;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        CreateLabel(panel.transform, "Light & boom options", 17f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 24f)
            .color = Theme.Ink;
        CreateHint(panel.transform, "Same accessory lists as the previous quote menu. Changes update live.");

        var scrollGo = new GameObject("OptScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollGo.transform.SetParent(panel.transform, false);
        scrollGo.GetComponent<Image>().color = Theme.InputFill;
        scrollGo.GetComponent<Image>().raycastTarget = true;
        var scrollLe = scrollGo.GetComponent<LayoutElement>();
        scrollLe.flexibleHeight = 1f;
        scrollLe.minHeight = 280f;
        scrollLe.preferredHeight = 360f;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        StretchFull(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        viewport.GetComponent<Image>().raycastTarget = true;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.sizeDelta = Vector2.zero;
        var cv = content.GetComponent<VerticalLayoutGroup>();
        cv.spacing = 8f;
        cv.padding = new RectOffset(10, 10, 10, 10);
        cv.childForceExpandWidth = true;
        cv.childControlHeight = true;
        cv.childControlWidth = true;
        cv.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var sr = scrollGo.GetComponent<ScrollRect>();
        sr.viewport = viewport.GetComponent<RectTransform>();
        sr.content = contentRt;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 30f;
        AttachVerticalOverflowScrollbar(sr);

        // Float open pickers above OptScroll so the mask doesn't clip the list.
        var dropdownLayerGo = new GameObject("DropdownLayer", typeof(RectTransform), typeof(LayoutElement));
        dropdownLayerGo.transform.SetParent(panel.transform, false);
        dropdownLayerGo.GetComponent<LayoutElement>().ignoreLayout = true;
        StretchFull(dropdownLayerGo.GetComponent<RectTransform>());
        var dropdownLayer = dropdownLayerGo.GetComponent<RectTransform>();

        // Click-away catcher — sized to the options scroll area when a list opens
        // (keeps Done clickable; closes the menu without closing Options).
        var dismiss = CreatePanel("DropdownDismiss", dropdownLayer, new Color(0f, 0f, 0f, 0f));
        var dismissRt = dismiss.GetComponent<RectTransform>();
        dismissRt.anchorMin = new Vector2(0.5f, 0.5f);
        dismissRt.anchorMax = new Vector2(0.5f, 0.5f);
        dismissRt.pivot = new Vector2(0.5f, 0.5f);
        dismiss.GetComponent<Image>().raycastTarget = true;
        var dismissBtn = dismiss.AddComponent<Button>();
        dismissBtn.transition = UnityEngine.UI.Selectable.Transition.None;
        dismissBtn.targetGraphic = dismiss.GetComponent<Image>();
        dismissBtn.onClick.AddListener(() => CloseOpenOptionDropdown());
        dismiss.SetActive(false);
        _optionDropdownDismiss = dismiss;

        // Scrolling the options list dismisses any open picker.
        sr.onValueChanged.AddListener(_ =>
        {
            if (!_suppressOptionDropdownClose)
                CloseOpenOptionDropdown();
        });

        CreatePrimaryButton(panel.transform, "Done", () =>
        {
            DropdownPopulator.PersistAllCurrentSelections();
            ClosePopover();
        }, -1f, 40f);
        // Layer stays above Done so lists draw over the scroll body; Done sits just below.
        dropdownLayer.SetAsLastSibling();

        var populators = DropdownPopulator.Instances?
            .Where(p => p != null)
            .OrderBy(p => p.isBoomExcelFileDropDown ? 1 : 0)
            .ThenBy(p => p.gameObject.name)
            .ToList() ?? new List<DropdownPopulator>();

        if (populators.Count == 0)
        {
            CreateLabel(content.transform, "Option dropdowns are not available yet. Load pricing data for this room, then try again.", 13f, FontStyles.Italic, TextAlignmentOptions.Left, -1f, 56f)
                .color = Theme.InkMuted;
        }
        else
        {
            bool wroteLightHeader = false;
            bool wroteBoomHeader = false;
            foreach (var pop in populators)
            {
                var dd = pop.GetComponent<TMP_Dropdown>() ?? pop.GetComponentInChildren<TMP_Dropdown>(true);
                if (dd == null) continue;

                if (!pop.isBoomExcelFileDropDown && !wroteLightHeader)
                {
                    CreateSectionHeader(content.transform, "LIGHT OPTIONS");
                    wroteLightHeader = true;
                }
                else if (pop.isBoomExcelFileDropDown && !wroteBoomHeader)
                {
                    AddSpacer(content.transform, 4f);
                    CreateSectionHeader(content.transform, "BOOM OPTIONS");
                    wroteBoomHeader = true;
                }

                string title = FriendlyOptionLabel(pop.gameObject.name, pop.isBoomExcelFileDropDown);
                CreateLabel(content.transform, title, 12f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 18f)
                    .color = Theme.InkMuted;

                CreateOptionPicker(content.transform, pop, dropdownLayer, sr);
            }
        }
    }

    static string FriendlyOptionLabel(string objectName, bool isBoom)
    {
        string cleaned = objectName.Replace("Dropdown_", "").Replace("_", " ");
        return (isBoom ? "Boom · " : "Light · ") + cleaned;
    }

    /// <summary>
    /// Custom option row — TMP_Dropdown captions are unreliable when built at runtime,
    /// so we draw the current selection ourselves and open a simple list on click.
    /// The open list floats on <paramref name="floatLayer"/> so OptScroll's mask can't clip it.
    /// </summary>
    GameObject CreateOptionPicker(Transform parent, DropdownPopulator populator, RectTransform floatLayer, ScrollRect hostScroll)
    {
        if (populator == null)
            return null;

        populator.RestorePersistedSelection();

        var source = populator.Dropdown
                     ?? populator.GetComponent<TMP_Dropdown>()
                     ?? populator.GetComponentInChildren<TMP_Dropdown>(true);
        if (source == null)
            return null;

        source.RefreshShownValue();

        var optionTexts = new List<string>();
        if (source.options != null)
        {
            for (int i = 0; i < source.options.Count; i++)
            {
                string t = source.options[i] != null ? source.options[i].text : null;
                optionTexts.Add(string.IsNullOrWhiteSpace(t) ? $"Option {i + 1}" : t.Trim());
            }
        }

        if (optionTexts.Count == 0)
            optionTexts.Add("None");

        int selected = Mathf.Clamp(source.value, 0, optionTexts.Count - 1);
        string selectedText = populator.GetCurrentSelectionText();
        if (string.IsNullOrWhiteSpace(selectedText))
            selectedText = optionTexts[selected];

        var root = new GameObject("OptionPicker", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        root.transform.SetParent(parent, false);
        var rootLe = root.GetComponent<LayoutElement>();
        rootLe.flexibleWidth = 1f;
        rootLe.minHeight = 34f;
        rootLe.preferredHeight = 34f;
        var rootLayout = root.GetComponent<VerticalLayoutGroup>();
        rootLayout.spacing = 0f;
        rootLayout.childAlignment = TextAnchor.UpperLeft;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = false;

        var row = CreatePanel("Row", root.transform, Theme.Ghost);
        var rowRt = row.GetComponent<RectTransform>();
        var rowLe = row.AddComponent<LayoutElement>();
        rowLe.preferredHeight = 34f;
        rowLe.minHeight = 34f;
        rowLe.flexibleWidth = 1f;
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(12, 8, 0, 0);
        rowLayout.spacing = 6f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = true;

        var valueLabel = CreateLabel(row.transform, selectedText, 13f, FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft, -1f, 22f);
        valueLabel.color = Theme.Ink;
        valueLabel.enableWordWrapping = false;
        valueLabel.overflowMode = TextOverflowModes.Ellipsis;
        valueLabel.raycastTarget = false;
        valueLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;

        var arrow = CreateLabel(row.transform, "▾", 12f, FontStyles.Bold, TextAlignmentOptions.Center, 22f, 22f);
        arrow.color = Theme.InkMuted;
        arrow.raycastTarget = false;

        // Build list under float layer (inactive) so it's never masked by OptScroll.
        var list = CreatePanel("List", floatLayer != null ? floatLayer : root.transform, Theme.Popover);
        list.SetActive(false);
        var listRt = list.GetComponent<RectTransform>();
        listRt.anchorMin = new Vector2(0.5f, 0.5f);
        listRt.anchorMax = new Vector2(0.5f, 0.5f);
        listRt.pivot = new Vector2(0f, 1f);
        var listOutline = list.AddComponent<Outline>();
        listOutline.effectColor = Theme.GhostBorder;
        listOutline.effectDistance = new Vector2(1f, -1f);

        var listScrollGo = new GameObject("ListScroll", typeof(RectTransform), typeof(ScrollRect));
        listScrollGo.transform.SetParent(list.transform, false);
        StretchFull(listScrollGo.GetComponent<RectTransform>(), 4f);
        var listScroll = listScrollGo.GetComponent<ScrollRect>();
        listScroll.horizontal = false;
        listScroll.vertical = true;
        listScroll.movementType = ScrollRect.MovementType.Clamped;

        var listViewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        listViewport.transform.SetParent(listScrollGo.transform, false);
        StretchFull(listViewport.GetComponent<RectTransform>());
        listViewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        listViewport.GetComponent<Image>().raycastTarget = true;

        var listContent = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        listContent.transform.SetParent(listViewport.transform, false);
        var listContentRt = listContent.GetComponent<RectTransform>();
        listContentRt.anchorMin = new Vector2(0, 1);
        listContentRt.anchorMax = new Vector2(1, 1);
        listContentRt.pivot = new Vector2(0.5f, 1f);
        listContentRt.sizeDelta = Vector2.zero;
        var listCv = listContent.GetComponent<VerticalLayoutGroup>();
        listCv.spacing = 2f;
        listCv.padding = new RectOffset(4, 4, 4, 4);
        listCv.childForceExpandWidth = true;
        listCv.childControlHeight = true;
        listCv.childControlWidth = true;
        listCv.childForceExpandHeight = false;
        listContent.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        listScroll.viewport = listViewport.GetComponent<RectTransform>();
        listScroll.content = listContentRt;
        listScroll.scrollSensitivity = 30f;
        AttachVerticalOverflowScrollbar(listScroll);

        void SetSelection(int index)
        {
            index = Mathf.Clamp(index, 0, optionTexts.Count - 1);
            source.SetValueWithoutNotify(index);
            source.RefreshShownValue();
            populator.PersistCurrentSelection();
            source.onValueChanged?.Invoke(index);

            string label = populator.GetCurrentSelectionText();
            if (string.IsNullOrWhiteSpace(label))
                label = optionTexts[index];
            valueLabel.text = label;
            CloseOpenOptionDropdown();

            RefreshLiveData();
            SchedulePreviewRefresh(forceVisuals: false);
        }

        for (int i = 0; i < optionTexts.Count; i++)
        {
            int index = i;
            string optionLabel = optionTexts[i];
            var itemBtn = CreateGhostButton(listContent.transform, optionLabel, () => SetSelection(index), -1f, 28f);
            var itemTmp = itemBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (itemTmp != null)
            {
                itemTmp.fontStyle = FontStyles.Normal;
                itemTmp.alignment = TextAlignmentOptions.MidlineLeft;
                itemTmp.fontSize = 12.5f;
                StretchFull(itemTmp.rectTransform, 8f);
            }
            if (index == selected)
                itemBtn.GetComponent<Image>().color = Theme.EditableHover;
        }

        var rowBtn = row.AddComponent<Button>();
        rowBtn.targetGraphic = row.GetComponent<Image>();
        rowBtn.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        var colors = rowBtn.colors;
        colors.normalColor = Theme.Ghost;
        colors.highlightedColor = Theme.Section;
        colors.pressedColor = Theme.Rule;
        rowBtn.colors = colors;
        rowBtn.onClick.AddListener(() =>
        {
            if (list.activeSelf)
            {
                CloseOpenOptionDropdown();
                return;
            }

            OpenOptionDropdown(list, listRt, rowRt, floatLayer, hostScroll, optionTexts.Count);
        });

        return root;
    }

    void OpenOptionDropdown(
        GameObject list,
        RectTransform listRt,
        RectTransform rowRt,
        RectTransform floatLayer,
        ScrollRect hostScroll,
        int optionCount)
    {
        if (list == null || listRt == null || rowRt == null || floatLayer == null)
            return;

        // Close any other open picker without clearing this open request.
        if (_openOptionDropdown != null && _openOptionDropdown != list)
            _openOptionDropdown.SetActive(false);

        _suppressOptionDropdownClose = true;
        try
        {
            if (hostScroll != null)
                EnsureScrollChildVisible(hostScroll, rowRt);

            Canvas.ForceUpdateCanvases();
            float desiredH = Mathf.Min(220f, 28f * Mathf.Min(optionCount, 8) + 8f);
            PositionFloatingDropdown(listRt, rowRt, floatLayer, desiredH);

            if (_optionDropdownDismiss != null)
            {
                AlignDropdownDismissToScroll(_optionDropdownDismiss.GetComponent<RectTransform>(), floatLayer, hostScroll);
                _optionDropdownDismiss.SetActive(true);
                _optionDropdownDismiss.transform.SetAsFirstSibling();
            }

            list.SetActive(true);
            list.transform.SetAsLastSibling();
            _openOptionDropdown = list;
        }
        finally
        {
            _suppressOptionDropdownClose = false;
        }
    }

    void AlignDropdownDismissToScroll(RectTransform dismissRt, RectTransform layerRt, ScrollRect hostScroll)
    {
        if (dismissRt == null || layerRt == null || hostScroll == null)
            return;

        var scrollRt = hostScroll.transform as RectTransform;
        if (scrollRt == null)
            return;

        var canvas = GetComponent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector3[] corners = new Vector3[4];
        scrollRt.GetWorldCorners(corners);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            layerRt, RectTransformUtility.WorldToScreenPoint(cam, corners[0]), cam, out Vector2 bl);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            layerRt, RectTransformUtility.WorldToScreenPoint(cam, corners[2]), cam, out Vector2 tr);

        dismissRt.anchorMin = new Vector2(0.5f, 0.5f);
        dismissRt.anchorMax = new Vector2(0.5f, 0.5f);
        dismissRt.pivot = new Vector2(0.5f, 0.5f);
        dismissRt.sizeDelta = new Vector2(Mathf.Abs(tr.x - bl.x), Mathf.Abs(tr.y - bl.y));
        dismissRt.anchoredPosition = (bl + tr) * 0.5f;
    }

    /// <summary>Returns true if a floating options dropdown was open and is now closed.</summary>
    bool CloseOpenOptionDropdown()
    {
        bool closed = false;
        if (_openOptionDropdown != null)
        {
            _openOptionDropdown.SetActive(false);
            _openOptionDropdown = null;
            closed = true;
        }

        if (_optionDropdownDismiss != null && _optionDropdownDismiss.activeSelf)
        {
            _optionDropdownDismiss.SetActive(false);
            closed = true;
        }

        return closed;
    }

    void HandleOpenOptionDropdownIdle()
    {
        if (_openOptionDropdown == null || _suppressOptionDropdownClose)
            return;

        // Wheel outside the open list dismisses it (scroll on OptScroll, empty panel, etc.).
        if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f && !IsPointerOverRect(_openOptionDropdown.transform as RectTransform))
            CloseOpenOptionDropdown();
    }

    bool IsPointerOverRect(RectTransform rt)
    {
        if (rt == null)
            return false;
        var canvas = GetComponent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam);
    }

    void PositionFloatingDropdown(RectTransform listRt, RectTransform rowRt, RectTransform layerRt, float desiredHeight)
    {
        if (listRt == null || rowRt == null || layerRt == null)
            return;

        var canvas = GetComponent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector3[] rowCorners = new Vector3[4];
        rowRt.GetWorldCorners(rowCorners);
        // 0=BL, 1=TL, 2=TR, 3=BR
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            layerRt, RectTransformUtility.WorldToScreenPoint(cam, rowCorners[0]), cam, out Vector2 localBL);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            layerRt, RectTransformUtility.WorldToScreenPoint(cam, rowCorners[1]), cam, out Vector2 localTL);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            layerRt, RectTransformUtility.WorldToScreenPoint(cam, rowCorners[3]), cam, out Vector2 localBR);

        float width = Mathf.Max(120f, localBR.x - localBL.x);
        Rect layerRect = layerRt.rect;
        float pad = 8f;
        float spaceBelow = localBL.y - (layerRect.yMin + pad);
        float spaceAbove = (layerRect.yMax - pad) - localTL.y;
        bool openDown = spaceBelow >= Mathf.Min(desiredHeight, 100f) || spaceBelow >= spaceAbove;
        float maxH = Mathf.Max(72f, openDown ? spaceBelow : spaceAbove);
        float listH = Mathf.Min(desiredHeight, maxH);

        listRt.anchorMin = new Vector2(0.5f, 0.5f);
        listRt.anchorMax = new Vector2(0.5f, 0.5f);
        listRt.sizeDelta = new Vector2(width, listH);

        if (openDown)
        {
            listRt.pivot = new Vector2(0f, 1f);
            listRt.anchoredPosition = localBL;
        }
        else
        {
            listRt.pivot = new Vector2(0f, 0f);
            listRt.anchoredPosition = localTL;
        }
    }

    static void EnsureScrollChildVisible(ScrollRect scroll, RectTransform child)
    {
        if (scroll == null || child == null || scroll.viewport == null || scroll.content == null)
            return;

        Canvas.ForceUpdateCanvases();
        var viewport = scroll.viewport;
        var content = scroll.content;

        Vector3[] childCorners = new Vector3[4];
        Vector3[] viewCorners = new Vector3[4];
        child.GetWorldCorners(childCorners);
        viewport.GetWorldCorners(viewCorners);

        float childBottom = childCorners[0].y;
        float childTop = childCorners[1].y;
        float viewBottom = viewCorners[0].y;
        float viewTop = viewCorners[1].y;

        float contentHeight = content.rect.height;
        float viewportHeight = viewport.rect.height;
        float scrollable = contentHeight - viewportHeight;
        if (scrollable <= 1f)
            return;

        float delta = 0f;
        if (childBottom < viewBottom)
            delta = viewBottom - childBottom;
        else if (childTop > viewTop)
            delta = viewTop - childTop;
        else
            return;

        // content moves with verticalNormalizedPosition; 1 = top.
        float next = scroll.verticalNormalizedPosition + delta / scrollable;
        scroll.verticalNormalizedPosition = Mathf.Clamp01(next);
        Canvas.ForceUpdateCanvases();
    }

    void OpenTextPopover(string title, (string label, string value, bool multiline)[] fields, Action<string[]> onSave, string footerHint = null)
    {
        ClosePopover();
        _popoverHost.gameObject.SetActive(true);
        CreatePopoverBackdrop();

        var panel = CreatePanel("TextPopover", _popoverHost, Theme.Popover);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(420f, 0f);
        // Block clicks from falling through without being a Selectable.
        panel.GetComponent<Image>().raycastTarget = true;

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 14);
        layout.spacing = 6f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateLabel(panel.transform, title, 16f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 24f)
            .color = Theme.Ink;

        var inputs = new List<TMP_InputField>();
        foreach (var (label, value, multiline) in fields)
        {
            CreateLabel(panel.transform, label, 11f, FontStyles.Normal, TextAlignmentOptions.Left, -1f, 16f)
                .color = Theme.InkMuted;
            var input = CreateInputField(panel.transform, value ?? "", multiline);
            if (input.placeholder is TMP_Text ph && string.IsNullOrWhiteSpace(value))
                ph.text = "Click to type " + label.ToLowerInvariant() + "…";
            inputs.Add(input);
        }

        if (!string.IsNullOrEmpty(footerHint))
            CreateHint(panel.transform, footerHint);

        var row = CreatePanel("Buttons", panel.transform, Color.clear);
        row.GetComponent<Image>().raycastTarget = false;
        row.AddComponent<LayoutElement>().preferredHeight = 36f;
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 8f;
        hl.childForceExpandWidth = true;
        hl.childControlHeight = true;
        hl.childControlWidth = true;

        CreateGhostButton(row.transform, "Cancel", ClosePopover, -1f, 34f);
        CreatePrimaryButton(row.transform, "Save", () =>
        {
            onSave(inputs.Select(i => i.text).ToArray());
            ClosePopover();
        }, -1f, 34f);

        if (inputs.Count > 0)
            StartCoroutine(FocusInputNextFrame(inputs[0]));
    }

    void CreatePopoverBackdrop()
    {
        var backdrop = CreatePanel("Backdrop", _popoverHost, new Color(0.08f, 0.09f, 0.11f, 0.45f));
        StretchFull(backdrop.GetComponent<RectTransform>());
        backdrop.transform.SetAsFirstSibling();
        var img = backdrop.GetComponent<Image>();
        img.raycastTarget = true;
        var btn = backdrop.AddComponent<Button>();
        btn.transition = UnityEngine.UI.Selectable.Transition.None;
        btn.targetGraphic = img;
        btn.onClick.AddListener(ClosePopover);
    }

    IEnumerator FocusInputNextFrame(TMP_InputField input)
    {
        // Wait one layout pass so ContentSizeFitter has non-zero text rects.
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (input == null || !input.isActiveAndEnabled)
            yield break;

        var parentRt = input.transform.parent as RectTransform;
        if (parentRt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);

        yield return null;
        if (input == null || !input.isActiveAndEnabled)
            yield break;

        ApplyVisibleCaretStyle(input);
        input.Select();
        input.ActivateInputField();
        int end = input.text != null ? input.text.Length : 0;
        input.caretPosition = end;
        input.selectionAnchorPosition = end;
        input.selectionFocusPosition = end;
        input.ForceLabelUpdate();
    }

    void OpenTextPopover(string title, (string label, string value)[] fields, Action<string[]> onSave, string footerHint = null)
    {
        var mapped = fields.Select(f => (f.label, f.value, false)).ToArray();
        OpenTextPopover(title, mapped, onSave, footerHint);
    }

    void ClosePopover()
    {
        if (_popoverHost == null)
            return;
        CloseOpenOptionDropdown();
        _optionDropdownDismiss = null;
        for (int i = _popoverHost.childCount - 1; i >= 0; i--)
            Destroy(_popoverHost.GetChild(i).gameObject);
        _popoverHost.gameObject.SetActive(false);
    }

    #endregion

    void ExportPdf()
    {
        if (_model == null)
            _model = ProposalPreviewModel.Capture();

        _model.PersistEditableFields();
        DropdownPopulator.PersistAllCurrentSelections();

        if (!ExportPaths.EnsureRoomSavedForExport(ExportPdf))
            return;

        var generator = FindAnyObjectByType<ProposalPDFGenerator>(FindObjectsInactive.Include);
        if (generator == null)
        {
            UI_DialogPrompt.Open(
                "Sales proposal generator is missing from the scene.",
                new ButtonAction("OK"));
            return;
        }

        _model.ApplyTo(generator);

        string folder = ExportPaths.ProposalsDir;
        try
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not create proposals folder: {e.Message}");
            UI_DialogPrompt.Open(
                "Could not create the proposals folder under AppData.",
                new ButtonAction("OK"));
            return;
        }

        string safeConfig = string.IsNullOrWhiteSpace(_model.ConfigName)
            ? ExportPaths.GetSuggestedExportFolderName()
            : string.Join("_", _model.ConfigName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeConfig))
            safeConfig = ExportPaths.GetSuggestedExportFolderName();
        string path = Path.Combine(folder, $"SalesProposal_{safeConfig}.pdf");

        CancelPreviewRefresh();
        SetPreviewLoading(true, "Exporting sales proposal…");
        Debug.Log($"[ProposalExport] writing {path}");
        generator.GeneratePDF(path, (ok, written, err) =>
        {
            SetPreviewLoading(false);
            if (ok)
            {
                SetStatus("Sales proposal exported.");
                Debug.Log($"[ProposalExport] done {written}");
            }
            else
            {
                SetStatus(string.IsNullOrWhiteSpace(err) ? "Export failed." : err);
                Debug.LogError($"[ProposalExport] failed: {err}");
            }
        });
    }

    #region UI helpers

    static void StretchFull(RectTransform rt, float pad = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
        rt.localScale = Vector3.one;
    }

    static GameObject CreatePanel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go;
    }

    static TMP_Text CreateLabel(Transform parent, string text, float size, FontStyles style,
        TextAlignmentOptions align, float width, float height = 24f)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = Theme.Ink;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        TMP_RuntimeFontRepair.Repair(tmp);
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = height;
        if (width > 0)
            le.preferredWidth = width;
        else
            le.flexibleWidth = 1f;
        return tmp;
    }

    static TMP_Text CreateHint(Transform parent, string text)
    {
        var t = CreateLabel(parent, text, 11f, FontStyles.Normal, TextAlignmentOptions.Left, -1f, 20f);
        t.color = Theme.InkFaint;
        t.fontStyle = FontStyles.Italic;
        return t;
    }

    static void CreateSectionHeader(Transform parent, string text)
    {
        var panel = CreatePanel("Section", parent, Theme.Section);
        panel.AddComponent<LayoutElement>().preferredHeight = 26f;
        var label = CreateLabel(panel.transform, text, 11f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, -1f, 20f);
        label.color = Theme.InkMuted;
        StretchFull(label.rectTransform, 10f);
        label.GetComponent<LayoutElement>().ignoreLayout = true;
    }

    static void AddSpacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
    }

    static Button CreateGhostButton(Transform parent, string label, UnityAction onClick, float width, float height)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = Theme.Ghost;
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = height;
        if (width > 0)
            le.preferredWidth = width;
        else
            le.flexibleWidth = 1f;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        StretchFull(textGo.GetComponent<RectTransform>());
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 13f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Theme.Ink;
        tmp.raycastTarget = false;
        TMP_RuntimeFontRepair.Repair(tmp);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        var colors = btn.colors;
        colors.highlightedColor = Theme.Section;
        colors.pressedColor = Theme.Rule;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);
        return btn;
    }

    static Button CreatePrimaryButton(Transform parent, string label, UnityAction onClick, float width, float height)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = Theme.Accent;
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = height;
        if (width > 0)
            le.preferredWidth = width;
        else
            le.flexibleWidth = 1f;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        StretchFull(textGo.GetComponent<RectTransform>());
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 15f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        TMP_RuntimeFontRepair.Repair(tmp);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        var colors = btn.colors;
        colors.highlightedColor = Theme.AccentDark;
        colors.pressedColor = Theme.AccentDark;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);
        return btn;
    }

    static TMP_InputField CreateInputField(Transform parent, string value, bool multiline = false)
    {
        float height = multiline ? 72f : 36f;
        var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement), typeof(Outline));
        go.transform.SetParent(parent, false);
        var bg = go.GetComponent<Image>();
        bg.color = Theme.InputFill;
        bg.raycastTarget = true;
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;

        var outline = go.GetComponent<Outline>();
        outline.effectColor = Theme.Accent;
        outline.effectDistance = new Vector2(1.6f, -1.6f);
        outline.useGraphicAlpha = false;
        outline.enabled = false;

        var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(go.transform, false);
        StretchFull(textArea.GetComponent<RectTransform>(), 8f);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textArea.transform, false);
        StretchFull(textGo.GetComponent<RectTransform>());
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 13f;
        tmp.color = Theme.Ink;
        tmp.enableWordWrapping = multiline;
        tmp.overflowMode = multiline ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        tmp.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;
        tmp.richText = false;
        TMP_RuntimeFontRepair.Repair(tmp);

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        placeholderGo.transform.SetParent(textArea.transform, false);
        StretchFull(placeholderGo.GetComponent<RectTransform>());
        var ph = placeholderGo.GetComponent<TextMeshProUGUI>();
        ph.text = "";
        ph.fontSize = 13f;
        ph.fontStyle = FontStyles.Italic;
        ph.color = Theme.InkFaint;
        ph.alignment = tmp.alignment;
        ph.raycastTarget = false;
        TMP_RuntimeFontRepair.Repair(ph);

        var input = go.GetComponent<TMP_InputField>();
        input.targetGraphic = bg;
        input.textViewport = textArea.GetComponent<RectTransform>();
        input.textComponent = tmp;
        input.placeholder = ph;
        input.text = value ?? "";
        input.pointSize = 13f;
        input.lineType = multiline
            ? TMP_InputField.LineType.MultiLineNewline
            : TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterValidation = TMP_InputField.CharacterValidation.None;
        input.richText = false;
        input.shouldHideMobileInput = true;
        input.interactable = true;
        input.navigation = new Navigation { mode = Navigation.Mode.None };

        ApplyVisibleCaretStyle(input);

        // Keep ColorBlock white so it doesn't multiply-darken Theme.InputFill.
        var colors = input.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.pressedColor = Color.white;
        colors.colorMultiplier = 1f;
        input.colors = colors;
        input.transition = UnityEngine.UI.Selectable.Transition.None;

        void SetFocused(bool focused)
        {
            bg.color = focused ? Color.white : Theme.InputFill;
            outline.enabled = focused;
            ApplyVisibleCaretStyle(input);
            if (focused)
            {
                if (!input.isFocused)
                    input.ActivateInputField();
                input.ForceLabelUpdate();
            }
        }

        // Clicking an unfocused field should show caret + accent ring immediately.
        input.onSelect.AddListener(_ => SetFocused(true));
        input.onDeselect.AddListener(_ => SetFocused(false));

        return input;
    }

    static void ApplyVisibleCaretStyle(TMP_InputField input)
    {
        if (input == null)
            return;

        // Runtime TMP fields often default to an invisible / zero caret.
        input.customCaretColor = true;
        input.caretColor = Theme.Ink;
        input.caretWidth = Mathf.Max(2, input.caretWidth);
        input.caretBlinkRate = 0.85f;
        input.selectionColor = new Color(0.35f, 0.55f, 0.95f, 0.35f);
    }

    #endregion
}
