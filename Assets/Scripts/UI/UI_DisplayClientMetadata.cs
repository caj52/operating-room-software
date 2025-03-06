using TMPro;
using UnityEngine;

public class UI_DisplayClientMetadata : MonoBehaviour
{
    [field: SerializeField] 
    private TextMeshProUGUI TextAccountName { get; set; }

    [field: SerializeField]
    private TextMeshProUGUI TextAccountAddressLine1 { get; set; }

    [field: SerializeField]
    private TextMeshProUGUI TextAccountAddressLine2 { get; set; }

    [field: SerializeField]
    private TextMeshProUGUI TextProjectName { get; set; }

    [field: SerializeField]
    private TextMeshProUGUI TextProjectNumber { get; set; }

    [field: SerializeField]
    private TextMeshProUGUI TextQuoteReferenceNumber { get; set; }

    private void Awake()
    {
        UI_ClientMetaData.OnClosed.AddListener(UpdateText);
    }

    private void OnEnable()
    {
        UpdateText();
    }

    private void OnDestroy()
    {
        UI_ClientMetaData.OnClosed.RemoveListener(UpdateText);
    }

    private void UpdateText()
    {
        TextAccountName.text = UI_ClientMetaData.AccountName;
        TextAccountAddressLine1.text = UI_ClientMetaData.AccountAddressLine1;
        TextAccountAddressLine2.text = UI_ClientMetaData.AccountAddressLine2;
        TextProjectName.text = UI_ClientMetaData.ProjectName;
        TextProjectNumber.text = UI_ClientMetaData.ProjectNumber;
        TextQuoteReferenceNumber.text = UI_ClientMetaData.OrderReferenceNumber;
    }
}
