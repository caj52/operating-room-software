using System;
using TMPro;
using UnityEngine;

public class PricingRowDataFill : MonoBehaviour
{
    public TMP_Text partNo;
    public TMP_Text modelName;
    public TMP_Text quantity;
    public TMP_Text listPrice;
    public SelectablePrice associatedObject;
    public event Action OnDestroyEvent;

    public void FillData(SelectablePrice selectablePrice)
    {
        this.associatedObject = selectablePrice;
        this.partNo.text = associatedObject.objectPricingData.PartNumber;
        this.modelName.text = associatedObject.objectPricingData.ObjectName;
        this.quantity.text = "1";
        if (associatedObject.objectPricingData.isSimFlexArmAvailable)
        {
            this.listPrice.text = (associatedObject.objectPricingData.ListPrice + associatedObject.objectPricingData.SimFlexPrice).ToString();
        }
        else
        {
            this.listPrice.text = associatedObject.objectPricingData.ListPrice.ToString();
        }   
    }
    public void DestroyRow()
    {
        Debug.Log(gameObject.name+ " Destroying UI row for: " + associatedObject.pricingObjectName);
        Destroy(gameObject);
    }
    private void OnDestroy()
    {
        OnDestroyEvent?.Invoke();
    }
}
