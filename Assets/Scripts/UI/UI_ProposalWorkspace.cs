using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using TriLibCore.SFB;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Near-fullscreen sales-proposal viewer. Shows the real generated PDF as rasterized
/// page images with clickable hotspots over editable regions (plus a slim side rail
/// for options / client data / refresh).
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
        public static readonly Color Hotspot = new(0.98f, 0.82f, 0.35f, 0.28f);
        public static readonly Color HotspotHover = new(0.98f, 0.72f, 0.22f, 0.42f);
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

    UnityAction _onPricingChanged;
    UnityAction<SelectablePrice> _onPriceEvent;

    /// <summary>Active workspace model, if any (used by PDF defaults).</summary>
    public ProposalPreviewModel ActiveModel => isActiveAndEnabled ? _model : null;

    RectTransform _root;
    RectTransform _popoverHost;
    RectTransform _dotsHost;
    ScrollRect _pageScroll;
    Image _pageImage;
    RectTransform _pageFrameRt;
    RectTransform _hotspotLayer;
    AspectRatioFitter _pageAspect;
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
    bool _pendingForceVisuals;

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
            DropdownPopulator.RestoreAllPersistedSelections();
            Instance._model = ProposalPreviewModel.Capture();
            Instance.gameObject.SetActive(true);
            Instance.EnsureBlocksRaycasts();
            Instance._pageIndex = 0;
            Instance.ClosePopover();
            Instance.SetPreviewLoading(true, "Generating proposal preview…");
            Instance.ShowPage(0);
            // First open: start immediately (no debounce).
            Instance.BeginPreviewGeneration(forceVisuals: true);
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
        var transforms = UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null || t.name != "Panel_PricingQuote")
                continue;
            if (t.gameObject.activeSelf)
                t.gameObject.SetActive(false);
        }
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

    static GameObject FindPricingQuotePanel()
    {
        var all = UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in all)
        {
            if (t != null && t.name == "Panel_PricingQuote")
                return t.gameObject;
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

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        if (_popoverHost != null && _popoverHost.gameObject.activeSelf)
        {
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
        RefreshLiveData(syncSalesRep: false);
        SchedulePreviewRefresh(forceVisuals: false);
    }

    void OnClientDataClosed()
    {
        RefreshLiveData(syncSalesRep: true);
        SchedulePreviewRefresh(forceVisuals: false);
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

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewport.transform.SetParent(pageScroll.transform, false);
        StretchFull(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
        viewport.GetComponent<Image>().raycastTarget = true;

        var content = new GameObject("PageHost", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0, 0);
        var contentV = content.GetComponent<VerticalLayoutGroup>();
        contentV.padding = new RectOffset(20, 20, 20, 20);
        contentV.spacing = 0f;
        contentV.childAlignment = TextAnchor.UpperCenter;
        contentV.childControlHeight = true;
        contentV.childControlWidth = true;
        contentV.childForceExpandWidth = true;
        contentV.childForceExpandHeight = false;
        var csf = content.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var pageFrame = CreatePanel("PageFrame", content.transform, Theme.Placeholder);
        _pageFrameRt = pageFrame.GetComponent<RectTransform>();
        var pageFrameLe = pageFrame.AddComponent<LayoutElement>();
        pageFrameLe.flexibleWidth = 1f;
        pageFrameLe.preferredHeight = 900f;
        pageFrameLe.minHeight = 400f;
        _pageAspect = pageFrame.AddComponent<AspectRatioFitter>();
        _pageAspect.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
        _pageAspect.aspectRatio = A4Aspect;

        _pageImage = pageFrame.GetComponent<Image>();
        _pageImage.color = Theme.Placeholder;
        _pageImage.preserveAspect = true;
        _pageImage.type = Image.Type.Simple;
        _pageImage.raycastTarget = false;

        var hotspotGo = new GameObject("Hotspots", typeof(RectTransform));
        hotspotGo.transform.SetParent(pageFrame.transform, false);
        _hotspotLayer = hotspotGo.GetComponent<RectTransform>();
        StretchFull(_hotspotLayer);

        _pageScroll = pageScroll.GetComponent<ScrollRect>();
        _pageScroll.viewport = viewport.GetComponent<RectTransform>();
        _pageScroll.content = contentRt;
        _pageScroll.horizontal = false;
        _pageScroll.vertical = true;
        _pageScroll.movementType = ScrollRect.MovementType.Clamped;
        _pageScroll.scrollSensitivity = 40f;

        BuildLoadingOverlay(pageScroll.transform);
        BuildSideRail(stage.transform);

        // Bottom nav: Previous / dots / N / pages / Next + Export
        var nav = CreatePanel("Nav", chrome.transform, Color.clear);
        nav.GetComponent<Image>().raycastTarget = false;
        var navLe = nav.AddComponent<LayoutElement>();
        navLe.preferredHeight = 52f;
        navLe.flexibleHeight = 0f;
        navLe.minHeight = 52f;
        var navLayout = nav.AddComponent<HorizontalLayoutGroup>();
        navLayout.padding = new RectOffset(4, 4, 2, 2);
        navLayout.spacing = 12f;
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

        var navSpacer = new GameObject("NavSpacer", typeof(RectTransform), typeof(LayoutElement));
        navSpacer.transform.SetParent(nav.transform, false);
        navSpacer.GetComponent<LayoutElement>().flexibleWidth = 1f;

        CreatePrimaryButton(nav.transform, "Export PDF", ExportPdf, 160f, 40f);

        _popoverHost = CreatePanel("PopoverHost", _root, new Color(0.08f, 0.09f, 0.11f, 0.45f)).GetComponent<RectTransform>();
        StretchFull(_popoverHost);
        _popoverHost.gameObject.SetActive(false);
        var popBtn = _popoverHost.gameObject.AddComponent<Button>();
        popBtn.transition = UnityEngine.UI.Selectable.Transition.None;
        popBtn.targetGraphic = _popoverHost.GetComponent<Image>();
        popBtn.onClick.AddListener(ClosePopover);

        RebuildPageDots(0);
        RebuildHotspots();
        EnsureBlocksRaycasts();
        TMP_RuntimeFontRepair.RepairAll();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(chromeRt);
    }

    #region PDF preview

    void SchedulePreviewRefresh(bool forceVisuals = false)
    {
        if (!isActiveAndEnabled)
            return;

        if (forceVisuals)
            _pendingForceVisuals = true;

        if (_previewDebounce != null)
            StopCoroutine(_previewDebounce);
        _previewDebounce = StartCoroutine(PreviewDebounceRoutine());
    }

    IEnumerator PreviewDebounceRoutine()
    {
        yield return new WaitForSecondsRealtime(PreviewDebounceSeconds);
        _previewDebounce = null;
        bool forceVisuals = _pendingForceVisuals;
        _pendingForceVisuals = false;
        BeginPreviewGeneration(forceVisuals);
    }

    void CancelPreviewRefresh()
    {
        if (_previewDebounce != null)
        {
            StopCoroutine(_previewDebounce);
            _previewDebounce = null;
        }
        _previewGenerationId++;
        _pendingForceVisuals = false;
        SetPreviewLoading(false);
    }

    void BeginPreviewGeneration(bool forceVisuals)
    {
        if (!isActiveAndEnabled)
            return;

        int generationId = ++_previewGenerationId;
        bool firstLoad = _pageSprites.Count == 0;
        SetPreviewLoading(true, firstLoad
            ? "Generating proposal preview…"
            : "Updating preview…");

        if (_model == null)
            _model = ProposalPreviewModel.Capture();
        else
            _model.RefreshLive();

        _model.PersistEditableFields();

        var generator = FindAnyObjectByType<ProposalPDFGenerator>(FindObjectsInactive.Include);
        if (generator == null)
        {
            if (generationId != _previewGenerationId)
                return;
            SetPreviewLoading(false);
            SetStatus("Sales proposal generator is missing from the scene.");
            return;
        }

        bool reuseVisuals = !forceVisuals;
        generator.GeneratePreviewPdf((ok, path, err) =>
        {
            if (generationId != _previewGenerationId || !isActiveAndEnabled)
                return;

            if (!ok || string.IsNullOrEmpty(path))
            {
                SetPreviewLoading(false);
                SetStatus(string.IsNullOrWhiteSpace(err) ? "Preview failed." : err);
                return;
            }

            try
            {
                SetPreviewLoading(true, "Preparing page images…");
                var textures = ProposalPdfPreviewRasterizer.RasterizePages(path);
                _hotspots.Clear();
                if (_model != null)
                    _hotspots.AddRange(ProposalPdfPreviewHotspotFinder.Find(path, _model));
                ApplyPageTextures(textures);
                SetPreviewLoading(false);
                SetStatus(_hotspots.Count > 0
                    ? "Preview up to date — click highlighted fields to edit"
                    : "Preview up to date");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Proposal preview rasterize failed: {e.Message}");
                SetPreviewLoading(false);
                SetStatus("Could not rasterize preview: " + e.Message);
            }
        }, reuseVisuals);
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
            _pageImage.preserveAspect = true;
        }

        if (_pageAspect != null && sprite != null && sprite.rect.height > 0.01f)
            _pageAspect.aspectRatio = sprite.rect.width / sprite.rect.height;
        else if (_pageAspect != null)
            _pageAspect.aspectRatio = A4Aspect;

        if (_pageLabel != null)
            _pageLabel.text = $"{_pageIndex + 1} / {pageCount}";

        for (int i = 0; i < _pageDots.Count; i++)
        {
            if (_pageDots[i] != null)
                _pageDots[i].color = i == _pageIndex ? Theme.Accent : Theme.NavIdle;
        }

        if (_pageScroll != null)
            _pageScroll.verticalNormalizedPosition = 1f;

        RebuildHotspots();
        ClosePopover();
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
        else if (_loadingAnimRoutine != null)
        {
            StopCoroutine(_loadingAnimRoutine);
            _loadingAnimRoutine = null;
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
        CreateGhostButton(rail.transform, "Client Data", EditClientData, -1f, 34f);
        CreateGhostButton(rail.transform, "Refresh visuals", () => BeginPreviewGeneration(forceVisuals: true), -1f, 34f);

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
        var go = new GameObject("Hotspot_" + spot.Kind, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(_hotspotLayer, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(spot.X0, spot.Y0);
        rt.anchorMax = new Vector2(spot.X1, spot.Y1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.color = Color.white;
        img.raycastTarget = true;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        var colors = btn.colors;
        colors.normalColor = Theme.Hotspot;
        colors.highlightedColor = Theme.HotspotHover;
        colors.pressedColor = Theme.HotspotHover;
        colors.selectedColor = Theme.HotspotHover;
        colors.disabledColor = new Color(Theme.Hotspot.r, Theme.Hotspot.g, Theme.Hotspot.b, 0.12f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        btn.onClick.AddListener(() => OpenEditorFor(spot.Kind));

        if (!string.IsNullOrEmpty(spot.Tooltip))
        {
            var tip = go.AddComponent<UI_HoverTooltip>();
            tip.SetText(spot.Tooltip);
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

        OpenTextPopover("Sales rep",
            new[]
            {
                ("Name", _model.SalesRepName),
                ("Email", _model.SalesRepEmail),
            },
            values =>
            {
                _model.SalesRepName = values[0];
                _model.SalesRepEmail = values[1];
                _model.PersistEditableFields();
                SchedulePreviewRefresh(forceVisuals: false);
            });
    }

    void EditClientData()
    {
        ClosePopover();
        UI_ClientMetaData.Open();
    }

    void EditConfigTitle()
    {
        if (_model == null)
            _model = ProposalPreviewModel.Capture();

        OpenTextPopover("Configuration title",
            new[] { ("Title", _model.ConfigName) },
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
                SchedulePreviewRefresh(forceVisuals: false);
            },
            footerHint: "Export normally uses the saved room name. Clear the title to remove this override.");
    }

    void EditDiscount()
    {
        if (_model == null)
            _model = ProposalPreviewModel.Capture();

        OpenTextPopover("Discount %",
            new[] { ("Percent", _model.DiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture)) },
            values =>
            {
                if (float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float d))
                    _model.DiscountPercentage = Mathf.Max(0f, d);
                _model.PersistEditableFields();
                SchedulePreviewRefresh(forceVisuals: false);
            });
    }

    void EditNotes()
    {
        if (_model == null)
            _model = ProposalPreviewModel.Capture();

        OpenTextPopover("Notes",
            new[]
            {
                ("Note 1", _model.Note1, true),
                ("Note 2", _model.Note2, true),
                ("Acceptance", _model.Note3, true),
            },
            values =>
            {
                _model.Note1 = values[0];
                _model.Note2 = values[1];
                _model.Note3 = values[2];
                _model.PersistEditableFields();
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

        var panel = CreatePanel("OptionsPopover", _popoverHost, Theme.Popover);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(480f, 520f);
        var stop = panel.AddComponent<Button>();
        stop.transition = UnityEngine.UI.Selectable.Transition.None;
        stop.targetGraphic = panel.GetComponent<Image>();
        stop.onClick.AddListener(() => { });

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
        var scrollLe = scrollGo.GetComponent<LayoutElement>();
        scrollLe.flexibleHeight = 1f;
        scrollLe.minHeight = 280f;
        scrollLe.preferredHeight = 360f;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        StretchFull(viewport.GetComponent<RectTransform>());

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

                CreateOptionPicker(content.transform, pop);
            }
        }

        CreatePrimaryButton(panel.transform, "Done", () =>
        {
            DropdownPopulator.PersistAllCurrentSelections();
            ClosePopover();
            RefreshLiveData();
            SchedulePreviewRefresh(forceVisuals: false);
        }, -1f, 40f);
    }

    static string FriendlyOptionLabel(string objectName, bool isBoom)
    {
        string cleaned = objectName.Replace("Dropdown_", "").Replace("_", " ");
        return (isBoom ? "Boom · " : "Light · ") + cleaned;
    }

    /// <summary>
    /// Custom option row — TMP_Dropdown captions are unreliable when built at runtime,
    /// so we draw the current selection ourselves and open a simple list on click.
    /// </summary>
    GameObject CreateOptionPicker(Transform parent, DropdownPopulator populator)
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
        var rootLayout = root.GetComponent<VerticalLayoutGroup>();
        rootLayout.spacing = 4f;
        rootLayout.childAlignment = TextAnchor.UpperLeft;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = false;

        var row = CreatePanel("Row", root.transform, Theme.Ghost);
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

        var list = CreatePanel("List", root.transform, Theme.Popover);
        list.SetActive(false);
        var listLe = list.AddComponent<LayoutElement>();
        listLe.flexibleWidth = 1f;
        listLe.preferredHeight = Mathf.Min(180f, 28f * Mathf.Min(optionTexts.Count, 6) + 8f);
        listLe.minHeight = 36f;
        var listOutline = list.AddComponent<Outline>();
        listOutline.effectColor = Theme.GhostBorder;
        listOutline.effectDistance = new Vector2(1f, -1f);

        var listScrollGo = new GameObject("ListScroll", typeof(RectTransform), typeof(ScrollRect), typeof(LayoutElement));
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
            list.SetActive(false);
            rootLe.minHeight = 34f;
            rootLe.preferredHeight = -1f;

            RefreshLiveData();
            SchedulePreviewRefresh(forceVisuals: false);
        }

        for (int i = 0; i < optionTexts.Count; i++)
        {
            int index = i;
            string optionLabel = optionTexts[i];
            var itemBtn = CreateGhostButton(listContent.transform, optionLabel, () => SetSelection(index), -1f, 28f);
            // Ghost buttons are bold/centered — soften for a list look.
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
            bool open = !list.activeSelf;
            list.SetActive(open);
            if (open)
            {
                float listH = Mathf.Min(180f, 28f * Mathf.Min(optionTexts.Count, 6) + 8f);
                listLe.preferredHeight = listH;
                rootLe.minHeight = 34f + 4f + listH;
            }
            else
            {
                rootLe.minHeight = 34f;
            }
            Canvas.ForceUpdateCanvases();
            var parentRt = parent as RectTransform;
            if (parentRt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);
        });

        return root;
    }

    void OpenTextPopover(string title, (string label, string value, bool multiline)[] fields, Action<string[]> onSave, string footerHint = null)
    {
        ClosePopover();
        _popoverHost.gameObject.SetActive(true);

        var panel = CreatePanel("TextPopover", _popoverHost, Theme.Popover);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(420f, 0f);

        panel.AddComponent<Button>().transition = UnityEngine.UI.Selectable.Transition.None;
        var panelBtn = panel.GetComponent<Button>();
        panelBtn.targetGraphic = panel.GetComponent<Image>();
        panelBtn.onClick.AddListener(() => { });

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
            inputs.Add(CreateInputField(panel.transform, value ?? "", multiline));
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

        // Focus the first field so the caret is visible immediately.
        if (inputs.Count > 0 && EventSystem.current != null)
        {
            var first = inputs[0];
            EventSystem.current.SetSelectedGameObject(first.gameObject);
            first.ActivateInputField();
            first.caretPosition = first.text != null ? first.text.Length : 0;
            first.selectionAnchorPosition = first.caretPosition;
            first.selectionFocusPosition = first.caretPosition;
            first.ForceLabelUpdate();
        }
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

        if (!ExportPaths.EnsureRoomSavedForExport())
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

        string defaultDir = ExportPaths.ProposalsDir;
        try
        {
            if (!Directory.Exists(defaultDir))
                Directory.CreateDirectory(defaultDir);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not ensure proposals folder: {e.Message}");
            defaultDir = ExportPaths.GetExportBasePath();
        }

        string safeConfig = string.IsNullOrWhiteSpace(_model.ConfigName)
            ? "Configuration"
            : string.Join("_", _model.ConfigName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        string defaultName = $"SalesProposal_{safeConfig}";

        try
        {
            StandaloneFileBrowser.SaveFilePanelAsync(
                "Export sales proposal PDF",
                defaultDir,
                defaultName,
                new[] { new ExtensionFilter("PDF", "pdf") },
                item =>
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Name))
                        return;

                    string path = item.Name;
                    if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        path += ".pdf";

                    generator.GeneratePDF(path);
                });
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to open save dialog: {e}");
            UI_DialogPrompt.Open(
                "Could not open the system save dialog.",
                new ButtonAction("OK"));
        }
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
        float height = multiline ? 72f : 32f;
        var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var bg = go.GetComponent<Image>();
        bg.color = Theme.InputFill;
        bg.raycastTarget = true;
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;

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

        // Visible caret + selection (runtime TMP fields default to an invisible caret).
        input.customCaretColor = true;
        input.caretColor = Theme.Ink;
        input.caretWidth = 2;
        input.caretBlinkRate = 0.85f;
        input.selectionColor = new Color(0.35f, 0.55f, 0.95f, 0.35f);

        // Keep ColorBlock white so it doesn't multiply-darken Theme.InputFill.
        var colors = input.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.pressedColor = Color.white;
        colors.colorMultiplier = 1f;
        input.colors = colors;
        input.transition = UnityEngine.UI.Selectable.Transition.ColorTint;

        return input;
    }

    #endregion
}
