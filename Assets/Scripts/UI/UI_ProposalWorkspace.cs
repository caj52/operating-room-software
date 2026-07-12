using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Near-fullscreen sales-proposal viewer. Shows the real generated PDF as rasterized
/// page images, with popover editors for editable fields.
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
    AspectRatioFitter _pageAspect;
    TMP_Text _pageLabel;
    TMP_Text _statusLabel;
    readonly List<Image> _pageDots = new();

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
            Instance._model = ProposalPreviewModel.Capture();
            Instance.gameObject.SetActive(true);
            Instance.EnsureBlocksRaycasts();
            Instance._pageIndex = 0;
            Instance.ClosePopover();
            Instance.SetStatus("Rendering preview…");
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
        chromeRt.sizeDelta = new Vector2(900f, -48f);
        chromeRt.anchoredPosition = Vector2.zero;

        var chromeLayout = chrome.AddComponent<VerticalLayoutGroup>();
        chromeLayout.padding = new RectOffset(20, 20, 14, 14);
        chromeLayout.spacing = 10f;
        chromeLayout.childAlignment = TextAnchor.UpperCenter;
        chromeLayout.childControlHeight = true;
        chromeLayout.childControlWidth = true;
        chromeLayout.childForceExpandHeight = false;
        chromeLayout.childForceExpandWidth = true;

        // Header
        var header = CreatePanel("Header", chrome.transform, Color.clear);
        var headerLe = header.AddComponent<LayoutElement>();
        headerLe.preferredHeight = 48f;
        headerLe.flexibleHeight = 0f;
        headerLe.minHeight = 48f;
        header.GetComponent<Image>().raycastTarget = false;
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(4, 4, 4, 4);
        headerLayout.spacing = 12f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childControlWidth = false;

        var titleBlock = new GameObject("TitleBlock", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        titleBlock.transform.SetParent(header.transform, false);
        titleBlock.GetComponent<LayoutElement>().preferredWidth = 280f;
        titleBlock.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var tb = titleBlock.GetComponent<VerticalLayoutGroup>();
        tb.spacing = 0f;
        tb.childControlHeight = true;
        tb.childForceExpandHeight = false;
        CreateLabel(titleBlock.transform, "Sales proposal", 20f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, -1f, 26f)
            .color = Theme.Ink;
        CreateLabel(titleBlock.transform, "Review & edit before export", 12f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, -1f, 16f)
            .color = Theme.InkMuted;

        CreateGhostButton(header.transform, "Close", Close, 88f, 36f);

        // Toolbar — editors + refresh
        var toolbar = CreatePanel("Toolbar", chrome.transform, Color.clear);
        toolbar.GetComponent<Image>().raycastTarget = false;
        var toolbarLe = toolbar.AddComponent<LayoutElement>();
        toolbarLe.preferredHeight = 40f;
        toolbarLe.minHeight = 40f;
        toolbarLe.flexibleHeight = 0f;
        var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.padding = new RectOffset(0, 0, 0, 0);
        toolbarLayout.spacing = 6f;
        toolbarLayout.childAlignment = TextAnchor.MiddleLeft;
        toolbarLayout.childForceExpandWidth = false;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;

        CreateGhostButton(toolbar.transform, "Sales Rep", EditSalesRep, 96f, 32f);
        CreateGhostButton(toolbar.transform, "Client Data", EditClientData, 108f, 32f);
        CreateGhostButton(toolbar.transform, "Title", EditConfigTitle, 72f, 32f);
        CreateGhostButton(toolbar.transform, "Options", EditOptions, 88f, 32f);
        CreateGhostButton(toolbar.transform, "Discount", EditDiscount, 92f, 32f);
        CreateGhostButton(toolbar.transform, "Notes", EditNotes, 80f, 32f);

        var toolbarSpacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        toolbarSpacer.transform.SetParent(toolbar.transform, false);
        toolbarSpacer.GetComponent<LayoutElement>().flexibleWidth = 1f;

        CreateGhostButton(toolbar.transform, "Refresh visuals", () => BeginPreviewGeneration(forceVisuals: true), 132f, 32f);

        _statusLabel = CreateLabel(chrome.transform, "Rendering preview…", 12f, FontStyles.Italic,
            TextAlignmentOptions.MidlineLeft, -1f, 20f);
        _statusLabel.color = Theme.InkFaint;
        _statusLabel.GetComponent<LayoutElement>().flexibleHeight = 0f;

        // Desk + paper stage
        var stage = CreatePanel("Stage", chrome.transform, Theme.Desk);
        var stageLe = stage.AddComponent<LayoutElement>();
        stageLe.flexibleHeight = 1f;
        stageLe.minHeight = 420f;
        stageLe.preferredHeight = 700f;
        var stageLayout = stage.AddComponent<VerticalLayoutGroup>();
        stageLayout.padding = new RectOffset(28, 28, 22, 22);
        stageLayout.childAlignment = TextAnchor.UpperCenter;
        stageLayout.childControlHeight = true;
        stageLayout.childControlWidth = true;
        stageLayout.childForceExpandHeight = true;
        stageLayout.childForceExpandWidth = true;

        var pageScroll = new GameObject("PageScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        pageScroll.transform.SetParent(stage.transform, false);
        pageScroll.GetComponent<Image>().color = Theme.Paper;
        pageScroll.GetComponent<Image>().raycastTarget = true;
        var pageScrollLe = pageScroll.GetComponent<LayoutElement>();
        pageScrollLe.flexibleHeight = 1f;
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
        contentV.padding = new RectOffset(24, 24, 24, 24);
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

        _pageScroll = pageScroll.GetComponent<ScrollRect>();
        _pageScroll.viewport = viewport.GetComponent<RectTransform>();
        _pageScroll.content = contentRt;
        _pageScroll.horizontal = false;
        _pageScroll.vertical = true;
        _pageScroll.movementType = ScrollRect.MovementType.Clamped;
        _pageScroll.scrollSensitivity = 40f;

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
    }

    void BeginPreviewGeneration(bool forceVisuals)
    {
        if (!isActiveAndEnabled)
            return;

        int generationId = ++_previewGenerationId;
        SetStatus("Rendering preview…");

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
                SetStatus(string.IsNullOrWhiteSpace(err) ? "Preview failed." : err);
                return;
            }

            try
            {
                var textures = ProposalPdfPreviewRasterizer.RasterizePages(path);
                ApplyPageTextures(textures);
                SetStatus("Preview up to date");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Proposal preview rasterize failed: {e.Message}");
                SetStatus("Could not rasterize preview: " + e.Message);
            }
        }, reuseVisuals);
    }

    void ApplyPageTextures(List<Texture2D> textures)
    {
        ReleasePageTextures();

        if (textures == null || textures.Count == 0)
        {
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

        ClosePopover();
    }

    void SetStatus(string text)
    {
        if (_statusLabel != null)
            _statusLabel.text = text ?? "";
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

                CreateDropdownMirror(content.transform, dd);
            }
        }

        CreatePrimaryButton(panel.transform, "Done", () =>
        {
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

    GameObject CreateDropdownMirror(Transform parent, TMP_Dropdown source)
    {
        var go = new GameObject("MirrorDropdown", typeof(RectTransform), typeof(Image), typeof(TMP_Dropdown), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = Theme.Ghost;
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = 34f;
        le.minHeight = 34f;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        StretchFull(labelGo.GetComponent<RectTransform>(), 10f);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.offsetMax = new Vector2(-28f, labelRt.offsetMax.y);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.fontSize = 13f;
        label.color = Theme.Ink;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        TMP_RuntimeFontRepair.Repair(label);

        var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));
        arrowGo.transform.SetParent(go.transform, false);
        var art = arrowGo.GetComponent<RectTransform>();
        art.anchorMin = new Vector2(1, 0);
        art.anchorMax = new Vector2(1, 1);
        art.pivot = new Vector2(1, 0.5f);
        art.sizeDelta = new Vector2(26f, 0);
        art.anchoredPosition = Vector2.zero;
        var arrow = arrowGo.GetComponent<TextMeshProUGUI>();
        arrow.text = "▾";
        arrow.fontSize = 12f;
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.color = Theme.InkMuted;
        TMP_RuntimeFontRepair.Repair(arrow);

        var template = BuildDropdownTemplate(go.transform);
        var templateRefs = template.GetComponent<DropdownTemplateRefs>();

        var dropdown = go.GetComponent<TMP_Dropdown>();
        dropdown.targetGraphic = go.GetComponent<Image>();
        dropdown.captionText = label;
        dropdown.itemText = templateRefs != null ? templateRefs.ItemLabel : null;
        dropdown.template = template;
        dropdown.ClearOptions();
        dropdown.AddOptions(source.options.Select(o => o.text).ToList());
        dropdown.SetValueWithoutNotify(source.value);
        label.text = source.options.Count > source.value ? source.options[source.value].text : "";

        dropdown.onValueChanged.AddListener(idx =>
        {
            source.value = idx;
            source.RefreshShownValue();
            RefreshLiveData();
            SchedulePreviewRefresh(forceVisuals: false);
        });

        return go;
    }

    RectTransform BuildDropdownTemplate(Transform parent)
    {
        var template = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        template.transform.SetParent(parent, false);
        template.SetActive(false);
        var trt = template.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 0);
        trt.anchorMax = new Vector2(1, 0);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0, 2f);
        trt.sizeDelta = new Vector2(0, 160f);
        template.GetComponent<Image>().color = Theme.Popover;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(template.transform, false);
        StretchFull(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = Color.white;
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 1);
        crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.sizeDelta = new Vector2(0, 28f);

        var item = new GameObject("Item", typeof(RectTransform), typeof(Toggle), typeof(Image));
        item.transform.SetParent(content.transform, false);
        var irt = item.GetComponent<RectTransform>();
        irt.anchorMin = new Vector2(0, 0.5f);
        irt.anchorMax = new Vector2(1, 0.5f);
        irt.sizeDelta = new Vector2(0, 28f);
        item.GetComponent<Image>().color = Theme.InputFill;
        var toggle = item.GetComponent<Toggle>();
        toggle.targetGraphic = item.GetComponent<Image>();

        var itemLabel = new GameObject("Item Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        itemLabel.transform.SetParent(item.transform, false);
        StretchFull(itemLabel.GetComponent<RectTransform>(), 8f);
        var itemLabelTmp = itemLabel.GetComponent<TextMeshProUGUI>();
        itemLabelTmp.fontSize = 13f;
        itemLabelTmp.color = Theme.Ink;
        TMP_RuntimeFontRepair.Repair(itemLabelTmp);

        var holder = template.AddComponent<DropdownTemplateRefs>();
        holder.ItemLabel = itemLabelTmp;

        var sr = template.GetComponent<ScrollRect>();
        sr.content = crt;
        sr.viewport = viewport.GetComponent<RectTransform>();
        sr.horizontal = false;

        return trt;
    }

    sealed class DropdownTemplateRefs : MonoBehaviour
    {
        public TextMeshProUGUI ItemLabel;
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
        generator.GeneratePDF();
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
        go.GetComponent<Image>().color = Theme.InputFill;
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
        TMP_RuntimeFontRepair.Repair(ph);

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = textArea.GetComponent<RectTransform>();
        input.textComponent = tmp;
        input.placeholder = ph;
        input.text = value ?? "";
        input.pointSize = 13f;
        input.lineType = multiline
            ? TMP_InputField.LineType.MultiLineNewline
            : TMP_InputField.LineType.SingleLine;
        return input;
    }

    #endregion
}
