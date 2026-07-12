using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Near-fullscreen interactive sales-proposal mock. Replaces the tabbed Pricing/Quote panel.
/// </summary>
[RequireComponent(typeof(FullScreenMenu))]
public class UI_ProposalWorkspace : MonoBehaviour
{
    public static UI_ProposalWorkspace Instance { get; private set; }

    /// <summary>Live discount for PDF default pull while the workspace is open.</summary>
    public static float? LiveDiscountPercentage =>
        Instance != null && Instance.isActiveAndEnabled && Instance._model != null
            ? Instance._model.DiscountPercentage
            : (float?)null;

    // Visual language: soft document viewer, Imagine Unlimited orange accent.
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
        public static readonly Color Accent = new(0.91f, 0.47f, 0.13f, 1f); // Imagine orange
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
    RectTransform _pageHost;
    RectTransform _popoverHost;
    TMP_Text _pageLabel;
    readonly List<Image> _pageDots = new();

    GameObject _page1;
    GameObject _page2;
    GameObject _page3;

    TMP_Text _salesRepNameLabel;
    TMP_Text _salesRepEmailLabel;
    TMP_Text _submittedToLabel;
    TMP_Text _projectLabel;
    TMP_Text _configTitleLabel;
    TMP_Text _optionsSummaryLabel;
    TMP_Text _equipmentTotalLabel;
    RectTransform _configBlocksHost;

    RectTransform _pricingRowsHost;
    TMP_Text _discountLabel;
    TMP_Text _grandTotalLabel;
    TMP_Text _note1Label;
    TMP_Text _note2Label;
    TMP_Text _acceptanceLabel;

    // Binding moved to UI_OpenProposalWorkspaceButton (scene component + sceneLoaded).
    // Kept no-op Bootstrap so older references still compile if any remain.

    public static void Open()
    {
        try
        {
            HideLegacyPricingPanel();
            EnsureInstance();
            // Re-open while already visible: keep last persisted edits, then recapture live room data.
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
            Instance.RefreshAll();
            Instance.ShowPage(0);
            Instance.ClosePopover();
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
        Instance.gameObject.SetActive(false);
    }

    static void EnsureInstance()
    {
        if (Instance != null)
            return;

        var go = new GameObject(nameof(UI_ProposalWorkspace), typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image), typeof(CanvasGroup));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 280; // Below Client Data / dialogs (299), above room UI (100)
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

        // Awake on DropdownPopulator only runs when the panel (or children) become active.
        var panel = FindPricingQuotePanel();
        if (panel == null)
            return;

        // Hide before enabling so the legacy tabbed UI never flashes.
        var cg = panel.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = panel.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.interactable = false;
        cg.blocksRaycasts = false;

        panel.SetActive(true);

        // Dropdowns live on Additional Price tabs that may start inactive.
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

        // Keep the old panel out of the way; Instances remain after Awake.
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

        // Overlays above the workspace own Escape first.
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

    void OnPricingChanged() => RefreshLiveData(syncSalesRep: false);
    void OnClientDataClosed() => RefreshLiveData(syncSalesRep: true);

    /// <summary>Refresh room/client-driven fields without wiping in-progress edits.</summary>
    void RefreshLiveData(bool syncSalesRep = false)
    {
        if (_model == null)
            _model = ProposalPreviewModel.Capture();
        else
            _model.RefreshLive(syncSalesRep);
        RefreshAll();
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

        // Light shell — reads as a document viewer, not a dark tool panel.
        var chrome = CreatePanel("Chrome", _root, Theme.Shell);
        var chromeRt = chrome.GetComponent<RectTransform>();
        chromeRt.anchorMin = new Vector2(0.5f, 0f);
        chromeRt.anchorMax = new Vector2(0.5f, 1f);
        chromeRt.pivot = new Vector2(0.5f, 0.5f);
        chromeRt.sizeDelta = new Vector2(900f, -48f);
        chromeRt.anchoredPosition = Vector2.zero;

        var chromeLayout = chrome.AddComponent<VerticalLayoutGroup>();
        chromeLayout.padding = new RectOffset(20, 20, 14, 14);
        chromeLayout.spacing = 12f;
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

        _pageHost = new GameObject("PageHost", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter))
            .GetComponent<RectTransform>();
        _pageHost.SetParent(viewport.transform, false);
        _pageHost.anchorMin = new Vector2(0, 1);
        _pageHost.anchorMax = new Vector2(1, 1);
        _pageHost.pivot = new Vector2(0.5f, 1f);
        _pageHost.anchoredPosition = Vector2.zero;
        _pageHost.sizeDelta = new Vector2(0, 900f);
        var vlg = _pageHost.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(40, 40, 36, 40);
        vlg.spacing = 6f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var csf = _pageHost.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var scroll = pageScroll.GetComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = _pageHost;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        BuildPages();

        // Page nav — quiet pills + dots
        var nav = CreatePanel("Nav", chrome.transform, Color.clear);
        nav.GetComponent<Image>().raycastTarget = false;
        var navLe = nav.AddComponent<LayoutElement>();
        navLe.preferredHeight = 40f;
        navLe.flexibleHeight = 0f;
        navLe.minHeight = 40f;
        var navLayout = nav.AddComponent<HorizontalLayoutGroup>();
        navLayout.padding = new RectOffset(4, 4, 2, 2);
        navLayout.spacing = 14f;
        navLayout.childAlignment = TextAnchor.MiddleCenter;
        navLayout.childForceExpandWidth = false;

        CreateGhostButton(nav.transform, "Previous", () => ShowPage(_pageIndex - 1), 100f, 32f);

        var dots = new GameObject("Dots", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        dots.transform.SetParent(nav.transform, false);
        dots.GetComponent<LayoutElement>().preferredWidth = 72f;
        var dotsLayout = dots.GetComponent<HorizontalLayoutGroup>();
        dotsLayout.spacing = 8f;
        dotsLayout.childAlignment = TextAnchor.MiddleCenter;
        dotsLayout.childForceExpandWidth = false;
        _pageDots.Clear();
        for (int i = 0; i < 3; i++)
        {
            int page = i;
            var dot = new GameObject("Dot" + i, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            dot.transform.SetParent(dots.transform, false);
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

        _pageLabel = CreateLabel(nav.transform, "1 / 3", 13f, FontStyles.Normal, TextAlignmentOptions.Center, 48f, 20f);
        _pageLabel.color = Theme.InkMuted;

        CreateGhostButton(nav.transform, "Next", () => ShowPage(_pageIndex + 1), 88f, 32f);

        // Footer
        var footer = CreatePanel("Footer", chrome.transform, Color.clear);
        footer.GetComponent<Image>().raycastTarget = false;
        var footerLe = footer.AddComponent<LayoutElement>();
        footerLe.preferredHeight = 52f;
        footerLe.flexibleHeight = 0f;
        footerLe.minHeight = 52f;
        var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.padding = new RectOffset(4, 4, 4, 4);
        footerLayout.spacing = 12f;
        footerLayout.childAlignment = TextAnchor.MiddleCenter;
        footerLayout.childForceExpandWidth = true;

        CreatePrimaryButton(footer.transform, "Export PDF", ExportPdf, -1f, 44f);

        _popoverHost = CreatePanel("PopoverHost", _root, new Color(0.08f, 0.09f, 0.11f, 0.45f)).GetComponent<RectTransform>();
        StretchFull(_popoverHost);
        _popoverHost.gameObject.SetActive(false);
        var popBtn = _popoverHost.gameObject.AddComponent<Button>();
        popBtn.transition = UnityEngine.UI.Selectable.Transition.None;
        popBtn.targetGraphic = _popoverHost.GetComponent<Image>();
        popBtn.onClick.AddListener(ClosePopover);

        EnsureBlocksRaycasts();
        TMP_RuntimeFontRepair.RepairAll();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(chromeRt);
    }

    void BuildPages()
    {
        _page1 = BuildPage1();
        _page2 = BuildPage2();
        _page3 = BuildPage3();
    }

    GameObject BuildPage1()
    {
        var page = CreatePageRoot("Page1_Summary");

        // Document masthead: title + logo
        var masthead = new GameObject("Masthead", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        masthead.transform.SetParent(page.transform, false);
        masthead.GetComponent<LayoutElement>().preferredHeight = 72f;
        var mh = masthead.GetComponent<HorizontalLayoutGroup>();
        mh.childAlignment = TextAnchor.UpperLeft;
        mh.childForceExpandWidth = false;
        mh.spacing = 12f;

        var left = new GameObject("Left", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        left.transform.SetParent(masthead.transform, false);
        left.GetComponent<LayoutElement>().flexibleWidth = 1f;
        var lv = left.GetComponent<VerticalLayoutGroup>();
        lv.spacing = 2f;
        lv.childControlHeight = true;
        lv.childForceExpandHeight = false;
        CreateLabel(left.transform, "Proposal", 28f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 34f).color = Theme.Ink;
        AddHairline(left.transform);

        TryAddLogo(masthead.transform);

        AddSpacer(page.transform, 10f);
        _salesRepNameLabel = CreateEditableRow(page.transform, "Sales Rep", "—", EditSalesRep);
        _salesRepEmailLabel = CreateEditableRow(page.transform, "Email", "—", EditSalesRep);

        CreateLabel(page.transform, "Imagine Unlimited", 13f, FontStyles.Normal, TextAlignmentOptions.Left, -1f, 20f).color = Theme.Ink;
        CreateLabel(page.transform, "9155 Sterling St Suite 120 · Irving, TX 75063", 12f, FontStyles.Normal, TextAlignmentOptions.Left, -1f, 18f).color = Theme.InkMuted;
        CreateLabel(page.transform, "Tel: 1 877 789 8106", 12f, FontStyles.Normal, TextAlignmentOptions.Left, -1f, 18f).color = Theme.InkMuted;

        AddSpacer(page.transform, 14f);
        _submittedToLabel = CreateEditableBanner(page.transform, "Submitted To: —", EditClientData);
        AddSpacer(page.transform, 6f);
        _projectLabel = CreateEditableRow(page.transform, "Project", "—", EditClientData);

        AddSpacer(page.transform, 10f);
        _configTitleLabel = CreateEditableRow(page.transform, "Configuration", "—", EditConfigTitle);

        _configBlocksHost = new GameObject("ConfigBlocks", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement))
            .GetComponent<RectTransform>();
        _configBlocksHost.SetParent(page.transform, false);
        var cbV = _configBlocksHost.GetComponent<VerticalLayoutGroup>();
        cbV.spacing = 8f;
        cbV.childForceExpandWidth = true;
        cbV.childControlHeight = false;
        _configBlocksHost.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _configBlocksHost.gameObject.AddComponent<LayoutElement>().minHeight = 40f;

        AddSpacer(page.transform, 8f);
        CreateSectionHeader(page.transform, "OPTION / ACCESSORY DESCRIPTION");
        _optionsSummaryLabel = CreateEditableMultiline(
            page.transform,
            "Click to edit light & boom options…",
            EditOptions);

        AddSpacer(page.transform, 16f);
        AddHairline(page.transform);
        // Page 1 PDF shows selectable equipment only (no dropdown/install/ship) as EQUIPMENT TOTAL.
        _equipmentTotalLabel = CreateLabel(page.transform, "EQUIPMENT TOTAL LIST PRICE  —", 14f, FontStyles.Bold, TextAlignmentOptions.Right, -1f, 28f);
        _equipmentTotalLabel.color = Theme.Ink;

        CreateHint(page.transform, "Highlighted areas are editable. Visuals fill in on export · pricing is on page 3.");

        return page;
    }

    GameObject BuildPage2()
    {
        var page = CreatePageRoot("Page2_Visuals");
        CreateLabel(page.transform, "Visuals", 22f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 30f).color = Theme.Ink;
        CreateHint(page.transform,
            "Ceiling plan and elevations are captured when you export. This preview shows placeholders only.");

        CreatePlaceholderBox(page.transform, "Ceiling plan\nCaptured on export");
        AddSpacer(page.transform, 14f);
        CreatePlaceholderBox(page.transform, "Elevations\nCaptured on export");
        return page;
    }

    GameObject BuildPage3()
    {
        var page = CreatePageRoot("Page3_Pricing");
        CreateLabel(page.transform, "Pricing", 22f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 30f).color = Theme.Ink;

        var header = CreatePanel("PriceHeader", page.transform, Theme.TableHead);
        header.AddComponent<LayoutElement>().preferredHeight = 30f;
        var hl = header.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(10, 10, 4, 4);
        hl.childForceExpandWidth = true;
        CreateLabel(header.transform, "PART #", 11f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 20f).color = Color.white;
        CreateLabel(header.transform, "DESCRIPTION", 11f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 20f).color = Color.white;
        CreateLabel(header.transform, "QTY", 11f, FontStyles.Bold, TextAlignmentOptions.Center, -1f, 20f).color = Color.white;
        CreateLabel(header.transform, "LIST", 11f, FontStyles.Bold, TextAlignmentOptions.Right, -1f, 20f).color = Color.white;
        CreateLabel(header.transform, "EXT", 11f, FontStyles.Bold, TextAlignmentOptions.Right, -1f, 20f).color = Color.white;

        _pricingRowsHost = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter))
            .GetComponent<RectTransform>();
        _pricingRowsHost.SetParent(page.transform, false);
        var rowsV = _pricingRowsHost.GetComponent<VerticalLayoutGroup>();
        rowsV.spacing = 0f;
        rowsV.childForceExpandWidth = true;
        rowsV.childControlHeight = false;
        _pricingRowsHost.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _pricingRowsHost.gameObject.AddComponent<LayoutElement>().minHeight = 40f;

        AddSpacer(page.transform, 14f);
        _discountLabel = CreateEditableRow(page.transform, "DISCOUNT %", "0.0%", EditDiscount);
        _grandTotalLabel = CreateLabel(page.transform, "GRAND TOTAL  —", 16f, FontStyles.Bold, TextAlignmentOptions.Right, -1f, 30f);
        _grandTotalLabel.color = Theme.Ink;

        AddSpacer(page.transform, 16f);
        CreateLabel(page.transform, "Notes", 14f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 22f).color = Theme.Ink;
        _note1Label = CreateEditableMultiline(page.transform, "Note 1", EditNotes);
        AddSpacer(page.transform, 6f);
        _note2Label = CreateEditableMultiline(page.transform, "Note 2", EditNotes);

        AddSpacer(page.transform, 14f);
        CreateSectionHeader(page.transform, "ACCEPTANCE");
        _acceptanceLabel = CreateEditableMultiline(page.transform, "Acceptance / client fields…", EditClientData);

        return page;
    }

    GameObject CreatePageRoot(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
        go.transform.SetParent(_pageHost, false);
        var v = go.GetComponent<VerticalLayoutGroup>();
        v.spacing = 4f;
        v.childForceExpandWidth = true;
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childForceExpandHeight = false;
        go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 400f;
        le.preferredHeight = -1f;
        le.flexibleWidth = 1f;
        return go;
    }

    void ShowPage(int index)
    {
        _pageIndex = Mathf.Clamp(index, 0, 2);
        if (_page1 != null) _page1.SetActive(_pageIndex == 0);
        if (_page2 != null) _page2.SetActive(_pageIndex == 1);
        if (_page3 != null) _page3.SetActive(_pageIndex == 2);
        if (_pageLabel != null)
            _pageLabel.text = $"{_pageIndex + 1} / 3";
        for (int i = 0; i < _pageDots.Count; i++)
        {
            if (_pageDots[i] != null)
                _pageDots[i].color = i == _pageIndex ? Theme.Accent : Theme.NavIdle;
        }
        ClosePopover();
    }

    void RefreshAll()
    {
        if (_model == null)
            _model = ProposalPreviewModel.Capture();

        if (_salesRepNameLabel != null)
            _salesRepNameLabel.text = string.IsNullOrWhiteSpace(_model.SalesRepName) ? "Click to set" : _model.SalesRepName;
        if (_salesRepEmailLabel != null)
            _salesRepEmailLabel.text = string.IsNullOrWhiteSpace(_model.SalesRepEmail) ? "Click to set" : _model.SalesRepEmail;
        if (_submittedToLabel != null)
            _submittedToLabel.text = "Submitted To: " + (string.IsNullOrWhiteSpace(_model.ClientName) ? "Click to open Client Data" : _model.ClientName);
        if (_projectLabel != null)
            _projectLabel.text = string.IsNullOrWhiteSpace(_model.ProjectName) ? "From Client Data" : _model.ProjectName;
        if (_configTitleLabel != null)
            _configTitleLabel.text = _model.ConfigName;

        RebuildConfigBlocksUi();

        if (_optionsSummaryLabel != null)
        {
            var parts = new List<string>();
            foreach (var block in _model.ConfigBlocks)
            {
                if (!string.IsNullOrWhiteSpace(block.LightOptionsText))
                    parts.Add("LIGHT: " + block.LightOptionsText);
                if (!string.IsNullOrWhiteSpace(block.BoomOptionsText))
                    parts.Add("BOOM: " + block.BoomOptionsText);
            }
            if (parts.Count == 0)
                parts.Add("No priced equipment yet. Place boom/light objects, then set options here.");
            _optionsSummaryLabel.text = string.Join("\n\n", parts);
        }

        if (_equipmentTotalLabel != null)
            _equipmentTotalLabel.text = "EQUIPMENT TOTAL LIST PRICE  " + _model.Page1EquipmentTotal.ToString("C", CultureInfo.CurrentCulture);

        RebuildPricingRows();

        if (_discountLabel != null)
            _discountLabel.text = _model.DiscountPercentage.ToString("F1", CultureInfo.InvariantCulture) + "%";
        if (_grandTotalLabel != null)
            _grandTotalLabel.text = "GRAND TOTAL  " + _model.GrandTotal.ToString("C", CultureInfo.CurrentCulture);
        if (_note1Label != null)
            _note1Label.text = _model.Note1;
        if (_note2Label != null)
            _note2Label.text = _model.Note2;
        if (_acceptanceLabel != null)
        {
            _acceptanceLabel.text =
                _model.Note3 + "\n\n" +
                "Account Name: " + _model.AccountName + "\n" +
                "Account Address: " + _model.AccountAddressLine1 + "\n" +
                " " + _model.AccountAddressLine2 + "\n" +
                "Project Name: " + _model.ProjectName + "\n" +
                "Project Number: " + _model.ProjectNumber + "\n" +
                "Order Reference #: " + _model.OrderReference + "\n\n" +
                "Click to edit Client Data";
        }
    }

    void RebuildConfigBlocksUi()
    {
        if (_configBlocksHost == null || _model == null)
            return;

        for (int i = _configBlocksHost.childCount - 1; i >= 0; i--)
            Destroy(_configBlocksHost.GetChild(i).gameObject);

        bool any = false;
        foreach (var block in _model.ConfigBlocks)
        {
            if (!block.HasLights && !block.HasBooms)
                continue;
            any = true;

            if (block.HasLights)
            {
                CreateModelQtyRow(_configBlocksHost, "LIGHT", block.LightQty);
            }
            if (block.HasBooms)
            {
                CreateModelQtyRow(_configBlocksHost, "ARTICULATING BOOM", block.BoomQty);
            }
        }

        if (!any)
            CreateLabel(_configBlocksHost, "No boom/light configuration in the room yet.", 12f, FontStyles.Italic, TextAlignmentOptions.Left, -1f, 22f);
    }

    static void CreateModelQtyRow(Transform parent, string model, int qty)
    {
        var header = CreatePanel("ModelHeader", parent, Theme.TableHead);
        header.AddComponent<LayoutElement>().preferredHeight = 26f;
        var hl = header.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(10, 10, 2, 2);
        hl.childForceExpandWidth = true;
        CreateLabel(header.transform, "MODEL DESCRIPTION", 11f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 20f).color = Color.white;
        CreateLabel(header.transform, "QTY", 11f, FontStyles.Bold, TextAlignmentOptions.Center, -1f, 20f).color = Color.white;

        var row = CreatePanel("ModelRow", parent, Theme.RowAlt);
        row.AddComponent<LayoutElement>().preferredHeight = 26f;
        var rl = row.AddComponent<HorizontalLayoutGroup>();
        rl.padding = new RectOffset(10, 10, 2, 2);
        rl.childForceExpandWidth = true;
        CreateLabel(row.transform, model, 12f, FontStyles.Normal, TextAlignmentOptions.Left, -1f, 20f).color = Theme.Ink;
        CreateLabel(row.transform, qty.ToString(), 12f, FontStyles.Normal, TextAlignmentOptions.Center, -1f, 20f).color = Theme.Ink;
    }

    void RebuildPricingRows()
    {
        if (_pricingRowsHost == null || _model == null)
            return;

        for (int i = _pricingRowsHost.childCount - 1; i >= 0; i--)
            Destroy(_pricingRowsHost.GetChild(i).gameObject);

        if (_model.PricingLines.Count == 0)
        {
            CreateLabel(_pricingRowsHost, "No line items yet.", 13f, FontStyles.Italic, TextAlignmentOptions.Left, -1f, 24f);
            return;
        }

        foreach (var line in _model.PricingLines)
        {
            if (line.IsSectionHeader)
            {
                CreateSectionHeader(_pricingRowsHost, line.SectionTitle);
                continue;
            }

            var row = CreatePanel("Row", _pricingRowsHost, Theme.RowAlt);
            row.AddComponent<LayoutElement>().preferredHeight = 24f;
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(10, 10, 2, 2);
            hl.childForceExpandWidth = true;
            CreateLabel(row.transform, line.PartNumber ?? "", 11f, FontStyles.Normal, TextAlignmentOptions.Left, -1f, 18f).color = Theme.Ink;
            CreateLabel(row.transform, line.Description ?? "", 11f, FontStyles.Normal, TextAlignmentOptions.Left, -1f, 18f).color = Theme.Ink;
            CreateLabel(row.transform, line.Qty.ToString(), 11f, FontStyles.Normal, TextAlignmentOptions.Center, -1f, 18f).color = Theme.Ink;
            CreateLabel(row.transform, line.UnitPrice.ToString("C", CultureInfo.CurrentCulture), 11f, FontStyles.Normal, TextAlignmentOptions.Right, -1f, 18f).color = Theme.Ink;
            CreateLabel(row.transform, line.ExtPrice.ToString("C", CultureInfo.CurrentCulture), 11f, FontStyles.Normal, TextAlignmentOptions.Right, -1f, 18f).color = Theme.Ink;
        }
    }

    #region Editors

    void EditSalesRep()
    {
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
                RefreshAll();
            });
    }

    void EditClientData()
    {
        ClosePopover();
        UI_ClientMetaData.Open();
    }

    void EditConfigTitle()
    {
        OpenTextPopover("Configuration title",
            new[] { ("Title", _model.ConfigName) },
            values =>
            {
                if (string.IsNullOrWhiteSpace(values[0]))
                {
                    // Empty title clears the session override and falls back to saved room name.
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
                RefreshAll();
            },
            footerHint: "Export normally uses the saved room name. Clear the title to remove this override.");
    }

    void EditDiscount()
    {
        OpenTextPopover("Discount %",
            new[] { ("Percent", _model.DiscountPercentage.ToString("0.##", CultureInfo.InvariantCulture)) },
            values =>
            {
                if (float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float d))
                    _model.DiscountPercentage = Mathf.Max(0f, d);
                _model.PersistEditableFields();
                RefreshAll();
            });
    }

    void EditNotes()
    {
        OpenTextPopover("Notes",
            new[]
            {
                ("Note 1", _model.Note1),
                ("Note 2", _model.Note2),
                ("Acceptance", _model.Note3),
            },
            values =>
            {
                _model.Note1 = values[0];
                _model.Note2 = values[1];
                _model.Note3 = values[2];
                _model.PersistEditableFields();
                RefreshAll();
            });
    }

    void EditOptions()
    {
        EnsurePricingOptionsInitialized();
        // Re-capture after init so OptionSelections populate.
        _model.RefreshLive();

        ClosePopover();
        _popoverHost.gameObject.SetActive(true);

        var panel = CreatePanel("OptionsPopover", _popoverHost, Theme.Popover);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(500f, 440f);
        var stop = panel.AddComponent<Button>();
        stop.transition = UnityEngine.UI.Selectable.Transition.None;
        stop.targetGraphic = panel.GetComponent<Image>();
        stop.onClick.AddListener(() => { });

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 20, 18);
        layout.spacing = 10f;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;

        CreateLabel(panel.transform, "Light & boom options", 18f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 28f)
            .color = Theme.Ink;
        CreateHint(panel.transform, "Same lists as before. Changes update the proposal immediately.");

        var scrollGo = new GameObject("OptScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollGo.transform.SetParent(panel.transform, false);
        scrollGo.GetComponent<Image>().color = Theme.InputFill;
        scrollGo.GetComponent<LayoutElement>().flexibleHeight = 1f;
        scrollGo.GetComponent<LayoutElement>().minHeight = 240f;

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
        cv.spacing = 10f;
        cv.padding = new RectOffset(8, 8, 8, 8);
        cv.childForceExpandWidth = true;
        cv.childControlHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var sr = scrollGo.GetComponent<ScrollRect>();
        sr.viewport = viewport.GetComponent<RectTransform>();
        sr.content = contentRt;
        sr.horizontal = false;

        var populators = DropdownPopulator.Instances;
        if (populators == null || populators.Count == 0)
        {
            CreateLabel(content.transform, "Option dropdowns are not available yet. Open the room with pricing data loaded, then try again.", 13f, FontStyles.Italic, TextAlignmentOptions.Left, -1f, 60f)
                .color = Theme.InkMuted;
        }
        else
        {
            foreach (var pop in populators.ToList())
            {
                if (pop == null) continue;
                var dd = pop.GetComponent<TMP_Dropdown>() ?? pop.GetComponentInChildren<TMP_Dropdown>(true);
                if (dd == null) continue;

                string title = FriendlyOptionLabel(pop.gameObject.name, pop.isBoomExcelFileDropDown);
                CreateLabel(content.transform, title, 13f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 20f)
                    .color = Theme.Ink;

                CreateDropdownMirror(content.transform, dd);
            }
        }

        CreatePrimaryButton(panel.transform, "Done", () =>
        {
            ClosePopover();
            RefreshLiveData();
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
        go.GetComponent<LayoutElement>().preferredHeight = 38f;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        StretchFull(labelGo.GetComponent<RectTransform>(), 12f);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.fontSize = 14f;
        label.color = Theme.Ink;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        TMP_RuntimeFontRepair.Repair(label);

        var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));
        arrowGo.transform.SetParent(go.transform, false);
        var art = arrowGo.GetComponent<RectTransform>();
        art.anchorMin = new Vector2(1, 0);
        art.anchorMax = new Vector2(1, 1);
        art.pivot = new Vector2(1, 0.5f);
        art.sizeDelta = new Vector2(28f, 0);
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

        // Stash for caller via GetComponentInChildren after return — use a holder.
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

    void OpenTextPopover(string title, (string label, string value)[] fields, Action<string[]> onSave, string footerHint = null)
    {
        ClosePopover();
        _popoverHost.gameObject.SetActive(true);

        var panel = CreatePanel("TextPopover", _popoverHost, Theme.Popover);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        float height = 130f + fields.Length * 72f + (footerHint != null ? 44f : 0f);
        rt.sizeDelta = new Vector2(460f, height);
        panel.AddComponent<Button>().transition = UnityEngine.UI.Selectable.Transition.None;
        var panelBtn = panel.GetComponent<Button>();
        panelBtn.targetGraphic = panel.GetComponent<Image>();
        panelBtn.onClick.AddListener(() => { });

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 20, 18);
        layout.spacing = 8f;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;

        CreateLabel(panel.transform, title, 18f, FontStyles.Bold, TextAlignmentOptions.Left, -1f, 28f)
            .color = Theme.Ink;

        var inputs = new List<TMP_InputField>();
        foreach (var (label, value) in fields)
        {
            CreateLabel(panel.transform, label, 12f, FontStyles.Normal, TextAlignmentOptions.Left, -1f, 18f)
                .color = Theme.InkMuted;
            inputs.Add(CreateInputField(panel.transform, value ?? ""));
        }

        if (!string.IsNullOrEmpty(footerHint))
            CreateHint(panel.transform, footerHint);

        var row = CreatePanel("Buttons", panel.transform, Color.clear);
        row.GetComponent<Image>().raycastTarget = false;
        row.AddComponent<LayoutElement>().preferredHeight = 40f;
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 10f;
        hl.childForceExpandWidth = true;

        CreateGhostButton(row.transform, "Cancel", ClosePopover, -1f, 36f);
        CreatePrimaryButton(row.transform, "Save", () =>
        {
            onSave(inputs.Select(i => i.text).ToArray());
            ClosePopover();
        }, -1f, 36f);
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

        // GeneratePDF → ApplyExportDefaults → workspace ActiveModel.ApplyTo (parity).
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

    static void AddHairline(Transform parent)
    {
        var line = CreatePanel("Hairline", parent, Theme.Rule);
        line.AddComponent<LayoutElement>().preferredHeight = 1f;
    }

    static void TryAddLogo(Transform parent)
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Data/quotes/IMAGINE-UNLIMITED_FullLogo_orange.png");
            if (!File.Exists(path))
                return;

            byte[] bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
                return;

            var go = new GameObject("Logo", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 140f;
            le.preferredHeight = 56f;
            le.flexibleWidth = 0f;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Could not load proposal logo: {e.Message}");
        }
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
        var t = CreateLabel(parent, text, 12f, FontStyles.Normal, TextAlignmentOptions.Left, -1f, 36f);
        t.color = Theme.InkFaint;
        t.fontStyle = FontStyles.Italic;
        return t;
    }

    static void CreateSectionHeader(Transform parent, string text)
    {
        var panel = CreatePanel("Section", parent, Theme.Section);
        panel.AddComponent<LayoutElement>().preferredHeight = 28f;
        var label = CreateLabel(panel.transform, text, 11f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, -1f, 22f);
        label.color = Theme.InkMuted;
        StretchFull(label.rectTransform, 10f);
        label.GetComponent<LayoutElement>().ignoreLayout = true;
    }

    static void CreatePlaceholderBox(Transform parent, string text)
    {
        var panel = CreatePanel("Placeholder", parent, Theme.Placeholder);
        panel.AddComponent<LayoutElement>().preferredHeight = 168f;
        var label = CreateLabel(panel.transform, text, 13f, FontStyles.Normal, TextAlignmentOptions.Center, -1f, 40f);
        StretchFull(label.rectTransform);
        label.GetComponent<LayoutElement>().ignoreLayout = true;
        label.color = Theme.InkFaint;
    }

    static void AddSpacer(Transform parent, float height)
    {
        var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
    }

    TMP_Text CreateEditableRow(Transform parent, string caption, string value, UnityAction onClick)
    {
        var row = CreatePanel("Editable_" + caption, parent, Theme.Editable);
        row.AddComponent<LayoutElement>().preferredHeight = 32f;
        var hl = row.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(12, 12, 4, 4);
        hl.spacing = 10f;
        hl.childForceExpandWidth = false;
        hl.childControlWidth = false;

        var accent = CreatePanel("Accent", row.transform, Theme.Accent);
        var ale = accent.AddComponent<LayoutElement>();
        ale.preferredWidth = 3f;
        ale.preferredHeight = 18f;
        ale.flexibleWidth = 0f;

        CreateLabel(row.transform, caption, 11f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, 110f, 20f)
            .color = Theme.InkMuted;
        var valueLabel = CreateLabel(row.transform, value, 13f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, -1f, 20f);
        valueLabel.color = Theme.Ink;
        valueLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;

        var btn = row.AddComponent<Button>();
        btn.targetGraphic = row.GetComponent<Image>();
        var colors = btn.colors;
        colors.normalColor = Theme.Editable;
        colors.highlightedColor = Theme.EditableHover;
        colors.pressedColor = Theme.EditableBorder;
        colors.selectedColor = Theme.EditableHover;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);
        return valueLabel;
    }

    TMP_Text CreateEditableBanner(Transform parent, string text, UnityAction onClick)
    {
        var row = CreatePanel("Banner", parent, Theme.Banner);
        row.AddComponent<LayoutElement>().preferredHeight = 34f;
        var label = CreateLabel(row.transform, text, 13f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, -1f, 28f);
        label.color = Theme.Ink;
        StretchFull(label.rectTransform, 12f);
        label.GetComponent<LayoutElement>().ignoreLayout = true;

        var btn = row.AddComponent<Button>();
        btn.targetGraphic = row.GetComponent<Image>();
        var colors = btn.colors;
        colors.highlightedColor = Theme.EditableHover;
        colors.pressedColor = Theme.EditableBorder;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);
        return label;
    }

    TMP_Text CreateEditableMultiline(Transform parent, string text, UnityAction onClick)
    {
        var row = CreatePanel("EditableMulti", parent, Theme.Editable);
        row.AddComponent<LayoutElement>().preferredHeight = 78f;
        var label = CreateLabel(row.transform, text, 12f, FontStyles.Normal, TextAlignmentOptions.TopLeft, -1f, 68f);
        label.color = Theme.Ink;
        StretchFull(label.rectTransform, 12f);
        label.GetComponent<LayoutElement>().ignoreLayout = true;

        var btn = row.AddComponent<Button>();
        btn.targetGraphic = row.GetComponent<Image>();
        var colors = btn.colors;
        colors.normalColor = Theme.Editable;
        colors.highlightedColor = Theme.EditableHover;
        colors.pressedColor = Theme.EditableBorder;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);
        return label;
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

    static Button CreateButton(Transform parent, string label, Color color, UnityAction onClick,
        float width, float height, float fontSize = 15f)
    {
        // Legacy helper — route accent fills to primary styling when possible.
        if (color.r > 0.7f && color.g < 0.55f)
            return CreatePrimaryButton(parent, label, onClick, width, height);
        return CreateGhostButton(parent, label, onClick, width, height);
    }

    static TMP_InputField CreateInputField(Transform parent, string value)
    {
        var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = Theme.InputFill;
        go.GetComponent<LayoutElement>().preferredHeight = 38f;

        var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(go.transform, false);
        StretchFull(textArea.GetComponent<RectTransform>(), 10f);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textArea.transform, false);
        StretchFull(textGo.GetComponent<RectTransform>());
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 14f;
        tmp.color = Theme.Ink;
        tmp.enableWordWrapping = false;
        TMP_RuntimeFontRepair.Repair(tmp);

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        placeholderGo.transform.SetParent(textArea.transform, false);
        StretchFull(placeholderGo.GetComponent<RectTransform>());
        var ph = placeholderGo.GetComponent<TextMeshProUGUI>();
        ph.text = "";
        ph.fontSize = 14f;
        ph.fontStyle = FontStyles.Italic;
        ph.color = Theme.InkFaint;
        TMP_RuntimeFontRepair.Repair(ph);

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = textArea.GetComponent<RectTransform>();
        input.textComponent = tmp;
        input.placeholder = ph;
        input.text = value ?? "";
        input.pointSize = 14f;
        return input;
    }

    #endregion
}
