using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main settings menu opened with a "gear" button in the Main 
/// scene. Singleton
/// </summary>
public class UI_SettingsMenu : MonoBehaviour
{
    public static UI_SettingsMenu 
    Instance { get; private set; }

    [field: SerializeField] 
    private Button ButtonCloseMenu { get; set; }

    TextMeshProUGUI _pricingStatusLabel;

    private void Awake()
    {
        Instance = this;
        ButtonCloseMenu.onClick.AddListener(Close);
        EnsurePricingSection();

        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        PricingBridgeStore.Changed -= RefreshPricingStatus;
        PricingBridgeStore.Changed += RefreshPricingStatus;
        RefreshPricingStatus();
    }

    private void OnDisable()
    {
        PricingBridgeStore.Changed -= RefreshPricingStatus;
    }

    private void OnDestroy()
    {
        ButtonCloseMenu.onClick.RemoveListener(Close);
    }

    private static void Close()
    {
        Instance.gameObject.SetActive(false);
    }

    internal static void Open()
    {
        Instance.EnsurePricingSection();
        Instance.RefreshPricingStatus();
        Instance.gameObject.SetActive(true);
    }

    void EnsurePricingSection()
    {
        if (_pricingStatusLabel != null)
            return;

        Transform container = transform.Find("Container");
        if (container == null)
            container = transform;

        var section = new GameObject("PricingSheetSection", typeof(RectTransform));
        section.transform.SetParent(container, false);
        var sectionRt = section.GetComponent<RectTransform>();
        sectionRt.anchorMin = new Vector2(0f, 0f);
        sectionRt.anchorMax = new Vector2(0f, 0f);
        sectionRt.pivot = new Vector2(0f, 0f);
        sectionRt.anchoredPosition = new Vector2(23f, 230f);
        sectionRt.sizeDelta = new Vector2(420f, 120f);

        var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleGo.transform.SetParent(section.transform, false);
        var titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0f, 1f);
        titleRt.anchoredPosition = Vector2.zero;
        titleRt.sizeDelta = new Vector2(0f, 24f);
        var title = titleGo.GetComponent<TextMeshProUGUI>();
        title.text = "Prices";
        title.fontSize = 18f;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.raycastTarget = false;
        TMP_RuntimeFontRepair.Repair(title);

        var statusGo = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
        statusGo.transform.SetParent(section.transform, false);
        var statusRt = statusGo.GetComponent<RectTransform>();
        statusRt.anchorMin = new Vector2(0f, 1f);
        statusRt.anchorMax = new Vector2(1f, 1f);
        statusRt.pivot = new Vector2(0f, 1f);
        statusRt.anchoredPosition = new Vector2(0f, -28f);
        statusRt.sizeDelta = new Vector2(0f, 40f);
        _pricingStatusLabel = statusGo.GetComponent<TextMeshProUGUI>();
        _pricingStatusLabel.fontSize = 14f;
        _pricingStatusLabel.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        _pricingStatusLabel.raycastTarget = false;
        TMP_RuntimeFontRepair.Repair(_pricingStatusLabel);

        CreateSettingsButton(section.transform, "Pricing…", new Vector2(0f, 0f), () =>
        {
            Close();
            UI_PricingBridge.Open();
        });
    }

    void RefreshPricingStatus()
    {
        if (_pricingStatusLabel != null)
            _pricingStatusLabel.text = PricingSheetAdmin.StatusSummary;
    }

    static void CreateSettingsButton(Transform parent, string label, Vector2 anchoredPos, Action onClick)
    {
        var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(200f, 44f);

        var img = go.GetComponent<Image>();
        img.color = Color.white;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        tmp.raycastTarget = false;
        TMP_RuntimeFontRepair.Repair(tmp);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick?.Invoke());
    }
}
