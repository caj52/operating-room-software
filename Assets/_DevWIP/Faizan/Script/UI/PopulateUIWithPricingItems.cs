using System;
using UnityEngine;

public class PopulateUIWithPricingItems : MonoBehaviour
{
    public PricingRowDataFill rowPrefab;



    public void GenerateUIRow(SelectablePrice selectablePrice)
    {
        PricingRowDataFill pricingRowDataFill = Instantiate(rowPrefab, transform);
        pricingRowDataFill.FillData(selectablePrice);

        //selectablePrice.OnDestroyed += pricingRowDataFill.DestroyRow;

         // Store the event subscription
        Action onDestroyedAction = pricingRowDataFill.DestroyRow;
        selectablePrice.OnDestroyed += onDestroyedAction;

        // Unsubscribe from the event when the UI row is destroyed
        pricingRowDataFill.OnDestroyEvent += () => selectablePrice.OnDestroyed -= onDestroyedAction;

    }
}
