using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Shows which Excel file quotes use, and lets you slot in a replacement.
/// </summary>
public class UI_PricingBridge : MonoBehaviour
{
    const int UiBuildVersion = 13;

    static UI_PricingBridge _instance;
    static int _loadedUiBuildVersion;

    GameObject _root;
    RectTransform _card;
    TextMeshProUGUI _fileLabel;
    Image _statusBg;
    TextMeshProUGUI _statusLabel;

    static class C
    {
        public static readonly Color Dimmer = new(0.12f, 0.14f, 0.16f, 0.55f);
        public static readonly Color Card = new(0.96f, 0.96f, 0.97f, 1f);
        public static readonly Color Paper = new(1f, 1f, 0.995f, 1f);
        public static readonly Color Ink = new(0.12f, 0.13f, 0.15f, 1f);
        public static readonly Color Muted = new(0.42f, 0.44f, 0.48f, 1f);
        public static readonly Color Line = new(0.86f, 0.87f, 0.89f, 1f);
        public static readonly Color Warn = new(0.72f, 0.28f, 0.08f, 1f);
        public static readonly Color WarnFill = new(0.99f, 0.93f, 0.86f, 1f);
        public static readonly Color Ok = new(0.22f, 0.42f, 0.30f, 1f);
        public static readonly Color OkFill = new(0.91f, 0.95f, 0.92f, 1f);
        public static readonly Color Accent = new(0.91f, 0.47f, 0.13f, 1f);
        public static readonly Color AccentDark = new(0.78f, 0.38f, 0.08f, 1f);
        public static readonly Color Ghost = new(1f, 1f, 1f, 1f);
        public static readonly Color Input = new(0.94f, 0.94f, 0.95f, 1f);
    }

    public static void Open()
    {
        if (_instance != null && _loadedUiBuildVersion != UiBuildVersion)
        {
            Destroy(_instance.gameObject);
            _instance = null;
        }

        if (_instance == null)
        {
            var go = new GameObject("UI_PricingBridge");
            _instance = go.AddComponent<UI_PricingBridge>();
            DontDestroyOnLoad(go);
            _instance.Build();
            _loadedUiBuildVersion = UiBuildVersion;
        }

        PricingBridgeStore.EnsureBundledPriceList();
        _instance._root.SetActive(true);
        _instance.Refresh();
    }

    public static void Close()
    {
        if (_instance?._root != null)
            _instance._root.SetActive(false);
    }

    void OnDestroy()
    {
        PricingBridgeStore.Changed -= OnBridgeChanged;
        if (_instance == this)
            _instance = null;
    }

    void OnBridgeChanged()
    {
        if (_root != null && _root.activeInHierarchy)
            Refresh();
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
        scaler.matchWidthOrHeight = 0.5f;

        var dim = Solid("Dim", _root.transform, C.Dimmer);
        Stretch(dim);
        var dimBtn = dim.gameObject.AddComponent<Button>();
        dimBtn.transition = UnityEngine.UI.Selectable.Transition.None;
        dimBtn.targetGraphic = dim.GetComponent<Image>();
        dimBtn.onClick.AddListener(Close);

        _card = Solid("Card", _root.transform, C.Card);
        _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
        _card.sizeDelta = new Vector2(520, 248);

        const float pad = 22f;
        Text(_card, "Price sheet", 20, FontStyles.Bold, C.Ink, pad, -16, 26);
        Btn(_card, "Close", false, -pad - 72, -16, 72, 28, Close);
        Hairline(_card, pad, -50);

        var caption = Text(_card, "Quotes use this spreadsheet.", 12, FontStyles.Normal, C.Muted, pad, -62, 18);
        caption.enableWordWrapping = false;

        var fileBox = Solid("File", _card, C.Paper);
        fileBox.anchorMin = new Vector2(0, 1);
        fileBox.anchorMax = new Vector2(1, 1);
        fileBox.pivot = new Vector2(0.5f, 1);
        fileBox.anchoredPosition = new Vector2(0, -88);
        fileBox.sizeDelta = new Vector2(-(pad * 2), 44);
        var boxOutline = fileBox.gameObject.AddComponent<Outline>();
        boxOutline.effectColor = C.Line;
        boxOutline.effectDistance = new Vector2(1, -1);

        var fileGo = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
        fileGo.transform.SetParent(fileBox, false);
        Stretch(fileGo.GetComponent<RectTransform>(), 12, 6);
        _fileLabel = fileGo.GetComponent<TextMeshProUGUI>();
        _fileLabel.fontSize = 14;
        _fileLabel.fontStyle = FontStyles.Bold;
        _fileLabel.color = C.Ink;
        _fileLabel.alignment = TextAlignmentOptions.MidlineLeft;
        _fileLabel.enableWordWrapping = false;
        _fileLabel.overflowMode = TextOverflowModes.Ellipsis;
        _fileLabel.raycastTarget = false;
        TMP_RuntimeFontRepair.Repair(_fileLabel);

        var status = Solid("Status", _card, C.OkFill);
        status.anchorMin = new Vector2(0, 1);
        status.anchorMax = new Vector2(1, 1);
        status.pivot = new Vector2(0.5f, 1);
        status.anchoredPosition = new Vector2(0, -140);
        status.sizeDelta = new Vector2(-(pad * 2), 44);
        _statusBg = status.GetComponent<Image>();

        var statusGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        statusGo.transform.SetParent(status, false);
        Stretch(statusGo.GetComponent<RectTransform>(), 12, 6);
        _statusLabel = statusGo.GetComponent<TextMeshProUGUI>();
        _statusLabel.fontSize = 13;
        _statusLabel.fontStyle = FontStyles.Bold;
        _statusLabel.color = C.Ok;
        _statusLabel.alignment = TextAlignmentOptions.MidlineLeft;
        _statusLabel.enableWordWrapping = true;
        _statusLabel.overflowMode = TextOverflowModes.Ellipsis;
        _statusLabel.raycastTarget = false;
        TMP_RuntimeFontRepair.Repair(_statusLabel);

        BottomBtn(_card, "Change spreadsheet", pad, 22, 36, PricingSheetAdmin.BeginReplaceWithFilePicker);
        _root.SetActive(false);
    }

    void Refresh()
    {
        string name = PricingBridgeStore.State.supplierFileName;
        if (string.IsNullOrEmpty(name))
            name = PricingBridgeStore.BundledV6FileName;
        _fileLabel.text = DisplayFileName(name);

        int missing = CountRoomPartsWithoutPrice();
        if (missing > 0)
        {
            _statusBg.color = C.WarnFill;
            _statusLabel.color = C.Warn;
            _statusLabel.text = missing == 1
                ? "1 part in this room has no price."
                : $"{missing} parts in this room have no price.";
        }
        else
        {
            _statusBg.color = C.OkFill;
            _statusLabel.color = C.Ok;
            _statusLabel.text = "Parts in this room have prices.";
        }
    }

    static int CountRoomPartsWithoutPrice()
    {
        var live = PricingBridgeStore.State.items;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int missing = 0;
        var parts = PricingManager.CollectActiveSelectablePrices();
        if (parts == null)
            return 0;

        foreach (var sp in parts)
        {
            if (sp == null) continue;
            string name = sp.pricingObjectName;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (PricingBridgeImporter.IsPackageSkip(name, out _)) continue;
            if (PricingBridgeImporter.IsPackageSkip(sp.UIObjectName ?? "", out _)) continue;

            string sheet = sp.sheetName ?? "";
            string size = sp.Size ?? "";
            string key = PricingBridgeStore.Key(sheet, name, size);
            if (!seen.Add(key)) continue;

            if (PricingBridgeImporter.TryPrice3D(sheet, name, size, live, out _))
                continue;
            if (PricingBridgeStore.TryGetOverride(sheet, name, size, out double typed) && typed > 0)
                continue;

            missing++;
        }

        return missing;
    }

    public static string DisplayFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "None";
        string stem = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ');
        while (stem.Contains("  "))
            stem = stem.Replace("  ", " ");
        stem = stem.Trim();
        string ext = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(ext) ? stem : stem + ext;
    }

    static RectTransform Solid(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go.GetComponent<RectTransform>();
    }

    static TextMeshProUGUI Text(
        RectTransform parent, string text, float size, FontStyles style, Color color,
        float left, float top, float height)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(left, top);
        rt.sizeDelta = new Vector2(-(left + 96), height);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        TMP_RuntimeFontRepair.Repair(tmp);
        return tmp;
    }

    static void Hairline(RectTransform parent, float inset, float top)
    {
        var rt = Solid("Line", parent, C.Line);
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, top);
        rt.sizeDelta = new Vector2(-inset * 2, 1);
        rt.GetComponent<Image>().raycastTarget = false;
    }

    static void Btn(RectTransform parent, string label, bool primary, float x, float top, float w, float h, UnityAction onClick)
    {
        var go = MakeButton(parent, label, primary, onClick);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(x < 0 ? 1 : 0, 1);
        rt.pivot = new Vector2(x < 0 ? 1 : 0, 1);
        rt.anchoredPosition = new Vector2(x < 0 ? x + w : x, top);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void BottomBtn(RectTransform parent, string label, float pad, float bottom, float h, UnityAction onClick)
    {
        var go = MakeButton(parent, label, true, onClick);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.offsetMin = new Vector2(pad, bottom);
        rt.offsetMax = new Vector2(-pad, bottom + h);
    }

    static GameObject MakeButton(RectTransform parent, string label, bool primary, UnityAction onClick)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = primary ? C.Accent : C.Ghost;

        if (!primary)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = C.Line;
            outline.effectDistance = new Vector2(1, -1);
        }

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        Stretch(textGo.GetComponent<RectTransform>());
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 13;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = primary ? Color.white : C.Ink;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        TMP_RuntimeFontRepair.Repair(tmp);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = primary ? C.AccentDark : C.Input;
        colors.pressedColor = primary ? C.AccentDark : C.Line;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);
        return go;
    }

    static void Stretch(RectTransform rt, float padX = 0, float padY = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }
}
