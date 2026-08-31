using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Legacy Pricing/Quote → Export Quote tab.
/// Job/client fields come from Client Metadata + the room; this panel only
/// keeps sales rep so users are not re-entering the same data.
/// </summary>
public class PDFGeneratorUIHandler : MonoBehaviour
{
    public TMP_InputField inputClientName;
    public TMP_InputField inputProjectName;
    public TMP_InputField inputSaleRepName;
    public TMP_InputField inputsalesRepEmail;
    public Button btnGeneratePDF;
    public ProposalPDFGenerator pdfGenerator;

    public TMP_InputField inputProjectNumber;
    public TMP_InputField inputAccountName;
    public TMP_InputField inputAccountAddress;
    public TMP_InputField inputReferenceNumber;

    public TMP_InputField inputNote1;
    public TMP_InputField inputNote2;
    public TMP_InputField inputNote3;

    public TMP_InputField inputDiscountPercentage;
    public TMP_InputField configurationName;

    private TMP_Text _autoFillInfo;

    // These rows live on Panel_ExportQuote. Notes/discount fields are wired to
    // Panel_AdditionalPrice and must NOT be hidden from here.
    private static readonly string[] RowsToHide =
    {
        "Row_ClientName",
        "Row_ProjectName",
        "Row_ProjectNumber",
        "Row_AccountName",
        "Row_AccountAddress",
        "Row_Reference#",
        "Row_ConfigurationName",
    };

    private void Start()
    {
        pdfGenerator = FindAnyObjectByType<ProposalPDFGenerator>();
        if (btnGeneratePDF != null)
        {
            btnGeneratePDF.onClick.RemoveListener(OnGeneratePdfButtonClicked);
            btnGeneratePDF.onClick.AddListener(OnGeneratePdfButtonClicked);
        }

        SimplifyExportForm();
        PrefillVisibleFields();
        RefreshAutoFillInfo();
    }

    private void OnEnable()
    {
        SimplifyExportForm();
        PrefillVisibleFields();
        RefreshAutoFillInfo();
    }

    private void SimplifyExportForm()
    {
        // Re-hide every time in case something re-enables rows.
        Transform root = transform;
        foreach (string rowName in RowsToHide)
        {
            var row = FindDeep(root, rowName);
            if (row != null)
                row.gameObject.SetActive(false);
        }

        EnsureAutoFillInfo(root);
    }

    private void EnsureAutoFillInfo(Transform root)
    {
        if (_autoFillInfo != null)
            return;

        var anchor = FindDeep(root, "Row_SalesRepName") ?? transform;
        Transform parent = anchor.parent != null ? anchor.parent : transform;

        var go = new GameObject("AutoFillInfo", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.layer = gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.SetSiblingIndex(Mathf.Max(0, anchor.GetSiblingIndex()));

        _autoFillInfo = go.GetComponent<TextMeshProUGUI>();
        _autoFillInfo.fontSize = 15;
        _autoFillInfo.color = new Color(0.25f, 0.25f, 0.25f, 1f);
        _autoFillInfo.alignment = TextAlignmentOptions.Left;
        _autoFillInfo.enableWordWrapping = true;
        _autoFillInfo.overflowMode = TextOverflowModes.Overflow;
        _autoFillInfo.text = "Hospital and project details come from Client Data.\nEnter your sales rep name below, then export.";

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 44f;
        le.preferredHeight = 48f;
        le.flexibleWidth = 1f;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(400f, 48f);

        var sample = GetComponentInChildren<TextMeshProUGUI>(true);
        if (sample != null)
        {
            _autoFillInfo.font = sample.font;
            _autoFillInfo.fontSharedMaterial = sample.fontSharedMaterial;
        }
    }

    private void PrefillVisibleFields()
    {
        // Only the fields still shown on Export Quote.
        if (inputSaleRepName != null)
            inputSaleRepName.text = UI_ClientMetaData.SalesRepName;
        if (inputsalesRepEmail != null)
            inputsalesRepEmail.text = UI_ClientMetaData.SalesRepEmail;

        if (inputDiscountPercentage != null
            && string.IsNullOrWhiteSpace(inputDiscountPercentage.text))
        {
            float d = ProposalPDFGenerator.GetSavedDiscountPercentage();
            if (d > 0f)
                inputDiscountPercentage.text = d.ToString("0.##");
        }
    }

    private void RefreshAutoFillInfo()
    {
        if (_autoFillInfo == null)
            return;

        // Keep this short — no jammed status dumps.
        _autoFillInfo.text =
            "Hospital and project details come from Client Data.\nEnter your sales rep name below, then export.";
    }

    private void OnGeneratePdfButtonClicked()
    {
        if (pdfGenerator == null)
            pdfGenerator = FindAnyObjectByType<ProposalPDFGenerator>();
        if (pdfGenerator == null)
        {
            UI_DialogPrompt.Open(
                "Sales proposal generator is missing from the scene.",
                new ButtonAction("OK"));
            return;
        }

        if (!ExportPaths.EnsureRoomSavedForExport())
            return;

        // Capture sales rep before generate (GeneratePDF also applies room/client defaults).
        if (inputSaleRepName != null)
            UI_ClientMetaData.SalesRepName = inputSaleRepName.text?.Trim() ?? "";
        if (inputsalesRepEmail != null)
            UI_ClientMetaData.SalesRepEmail = inputsalesRepEmail.text?.Trim() ?? "";

        if (inputDiscountPercentage != null
            && float.TryParse(inputDiscountPercentage.text, out float discount))
        {
            pdfGenerator.discountPercentage = discount;
            ProposalPDFGenerator.SaveDiscountPercentage(discount);
        }

        pdfGenerator.GeneratePDF();
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }
}
