using System;
using TMPro;
using UnityEngine;

public class PricingRowDataFill : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text partNo;
    public TMP_Text modelName;
    public TMP_Text quantity;
    public TMP_Text listPrice;

    [Header("Data Reference")]
    public SelectablePrice associatedObject;

    public event Action OnDestroyEvent;

    /// <summary>
    /// Populates this row with pricing data.
    /// </summary>
    public void FillData(PriceExcelData data, int quantityValue, SelectablePrice primaryObject)
    {
        associatedObject = primaryObject;
        partNo.text = data.PartNumber;
        modelName.text = data.ObjectName;
        quantity.text = quantityValue.ToString();

        double totalPrice = data.ListPrice;
        if (data.isSimFlexArmAvailable)
            totalPrice += data.SimFlexPrice;

        listPrice.text = totalPrice.ToString("F2");
        primaryObject.UIRefPricingRowDataFill = this;
    }

    /// <summary>
    /// Triggers removal and notifies UI system.
    /// </summary>
    public void DestroyRow()
    {
        var pricingUI = GetComponentInParent<PopulateUIWithPricingItems>();
        pricingUI?.OnSetPriceData?.Invoke(null, gameObject);

        Debug.Log("Anas => Destroy Price Row Invoked");
    }

    private void OnDestroy()
    {
        OnDestroyEvent?.Invoke();
    }

    // Placeholder if needed later
    public void DestroyDelayCoroutine()
    {
        // Intentionally left blank
    }
}
