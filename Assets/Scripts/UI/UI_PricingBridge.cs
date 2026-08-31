using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// In-game review of catalog prices vs supplier Excel matches, with manual edits for gaps.
/// </summary>
public class UI_PricingBridge : MonoBehaviour
{
    static UI_PricingBridge _instance;

    GameObject _root;
    Transform _listContent;
    TextMeshProUGUI _headerStatus;
    bool _gapsOnly = true;

    public static void Open()
    {
        if (_instance == null)
        {
            var go = new GameObject("UI_PricingBridge");
            _instance = go.AddComponent<UI_PricingBridge>();
            DontDestroyOnLoad(go);
            _instance.Build();
        }

        PricingBridgeStore.Load();
        _instance._root.SetActive(true);
        _instance.RebuildList();
    }

    public static void Close()
    {
        if (_instance?._root != null)
            _instance._root.SetActive(false);
    }

    void Build()
    {
        PricingBridgeStore.Changed -= OnBridgeChanged;
        PricingBridgeStore.Changed += OnBridgeChanged;

        _root = new GameObject("Root", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _root.transform.SetParent(transform, false);
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 320;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        var dim = Panel("Dim", _root.transform, new Color(0f, 0f, 0f, 0.55f));
        Stretch(dim.GetComponent<RectTransform>());

        var card = Panel("Card", _root.transform, new Color(0.16f, 0.17f, 0.2f, 1f));
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(1100, 720);
        cardRt.anchoredPosition = Vector2.zero;

        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 10;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var title = Label(card.transform, "Pricing", 22, FontStyles.Bold);
        title.color = Color.white;

        _headerStatus = Label(card.transform, "", 14, FontStyles.Normal);
        _headerStatus.color = new Color(0.85f, 0.85f, 0.85f);

        var hint = Label(card.transform,
            "Load Imagine’s new price spreadsheet. Fill anything still missing. Share the result with a coworker so they don’t need a new app build.",
            13, FontStyles.Normal);
        hint.color = new Color(0.75f, 0.75f, 0.75f);

        var toolbar = new GameObject("Toolbar", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        toolbar.transform.SetParent(card.transform, false);
        toolbar.GetComponent<LayoutElement>().preferredHeight = 40;
        var h = toolbar.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 8;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;

        GhostButton(toolbar.transform, "Load new spreadsheet", PricingSheetAdmin.BeginReplaceWithFilePicker, 200, 36);
        GhostButton(toolbar.transform, "Missing only", () => { _gapsOnly = true; RebuildList(); }, 130, 36);
        GhostButton(toolbar.transform, "Show all", () => { _gapsOnly = false; RebuildList(); }, 110, 36);
        GhostButton(toolbar.transform, "Send to coworker", PricingConfigPack.BeginExport, 160, 36);
        GhostButton(toolbar.transform, "Load coworker file", PricingConfigPack.BeginImport, 170, 36);
        GhostButton(toolbar.transform, "Close", Close, 90, 36);

        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollGo.transform.SetParent(card.transform, false);
        scrollGo.GetComponent<LayoutElement>().flexibleHeight = 1;
        scrollGo.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 1f);
        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollGo.transform, false);
        Stretch(viewport.GetComponent<RectTransform>());
        viewport.GetComponent<Image>().color = Color.white;
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1);
        contentRt.sizeDelta = new Vector2(0, 0);
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.spacing = 6;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRt;
        _listContent = content.transform;

        _root.SetActive(false);
    }

    void OnDestroy()
    {
        PricingBridgeStore.Changed -= OnBridgeChanged;
    }

    void OnBridgeChanged()
    {
        if (_root != null && _root.activeInHierarchy)
            RebuildList();
    }

    void RebuildList()
    {
        if (_listContent == null) return;
        for (int i = _listContent.childCount - 1; i >= 0; i--)
            Destroy(_listContent.GetChild(i).gameObject);

        var state = PricingBridgeStore.State;
        _headerStatus.text = string.IsNullOrEmpty(state.supplierFileName)
            ? "No spreadsheet loaded yet. Room prices still come from the app’s built-in list."
            : PricingBridgeStore.SupplierStatusSummary;

        IEnumerable<PricingBridgeItem> items = state.items ?? new List<PricingBridgeItem>();
        if (_gapsOnly)
            items = items.Where(i => i != null && i.missingFromSupplier);

        var list = items.Where(i => i != null).OrderBy(i => i.sheetName).ThenBy(i => i.objectName).ToList();
        if (list.Count == 0)
        {
            Label(_listContent, _gapsOnly ? "Nothing missing — every item matched or was filled in." : "No rows yet. Load a spreadsheet first.",
                14, FontStyles.Normal).color = new Color(0.8f, 0.8f, 0.8f);
            return;
        }

        foreach (var item in list)
            CreateRow(item);
    }

    void CreateRow(PricingBridgeItem item)
    {
        var row = Panel("Row", _listContent, new Color(0.22f, 0.23f, 0.26f, 1f));
        row.AddComponent<LayoutElement>().preferredHeight = 72;
        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(10, 10, 8, 8);
        h.spacing = 10;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childForceExpandHeight = true;
        h.childForceExpandWidth = false;

        var textBlock = new GameObject("Text", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        textBlock.transform.SetParent(row.transform, false);
        textBlock.GetComponent<LayoutElement>().flexibleWidth = 1;
        textBlock.GetComponent<VerticalLayoutGroup>().spacing = 2;

        string sourceLabel = item.missingFromSupplier
            ? "needs price"
            : (item.source ?? "Catalog");
        Label(textBlock.transform, $"{item.objectName}{(string.IsNullOrEmpty(item.size) ? "" : " · " + item.size)}",
            15, FontStyles.Bold).color = Color.white;
        Label(textBlock.transform,
            $"{ShortSheet(item.sheetName)}  |  part {item.partNumber}  |  {sourceLabel}  |  catalog ${item.catalogPrice:N2}"
            + (string.IsNullOrEmpty(item.supplierMatch) ? "" : "  |  " + item.supplierMatch),
            12, FontStyles.Normal).color = new Color(0.75f, 0.78f, 0.8f);

        var inputGo = new GameObject("Price", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
        inputGo.transform.SetParent(row.transform, false);
        inputGo.GetComponent<LayoutElement>().preferredWidth = 120;
        inputGo.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f, 1f);

        var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(inputGo.transform, false);
        Stretch(textArea.GetComponent<RectTransform>(), 6, 4);

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textArea.transform, false);
        Stretch(textGo.GetComponent<RectTransform>());
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 15;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineRight;
        TMP_RuntimeFontRepair.Repair(tmp);

        var input = inputGo.GetComponent<TMP_InputField>();
        input.textViewport = textArea.GetComponent<RectTransform>();
        input.textComponent = tmp;
        input.contentType = TMP_InputField.ContentType.DecimalNumber;
        input.text = item.effectivePrice.ToString("F2", CultureInfo.InvariantCulture);

        GhostButton(row.transform, "Save", () =>
        {
            if (!double.TryParse(input.text, NumberStyles.Any, CultureInfo.InvariantCulture, out double price)
                && !double.TryParse(input.text, NumberStyles.Any, CultureInfo.GetCultureInfo("en-US"), out price))
            {
                UI_DialogPrompt.Open("Enter a valid number for the price.");
                return;
            }

            PricingBridgeStore.SetManualPrice(item, price);
            RebuildList();
        }, 70, 34);
    }

    static string ShortSheet(string sheet)
    {
        if (string.IsNullOrEmpty(sheet)) return "";
        if (sheet.Contains("Light")) return "Light";
        if (sheet.Contains("Boom") && sheet.Contains("3D")) return "Boom bundle";
        if (sheet.Contains("Boom")) return "Boom";
        return sheet;
    }

    static GameObject Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go;
    }

    static TextMeshProUGUI Label(Transform parent, string text, float size, FontStyles style)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        go.GetComponent<LayoutElement>().preferredHeight = size + 10;
        TMP_RuntimeFontRepair.Repair(tmp);
        return tmp;
    }

    static void GhostButton(Transform parent, string label, UnityAction onClick, float width, float height)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.32f, 0.34f, 0.38f, 1f);
        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        Stretch(textGo.GetComponent<RectTransform>());
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        TMP_RuntimeFontRepair.Repair(tmp);
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        btn.onClick.AddListener(onClick);
    }

    static void Stretch(RectTransform rt, float padX = 0, float padY = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }
}
