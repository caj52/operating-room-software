using System;
using UnityEngine;

public class SelectablePrice : MonoBehaviour
{

    public string pricingObjectName;
    public PriceExcelData objectPricingData;
    public Selectable[] selectable;

    public event Action OnDestroyed;

    /// <summary>
    /// Get Price From Excel
    /// </summary>69
    public void GetPricingDataFromExcel(string excelFileName)
    {
        ExcelReader excelReader = FindAnyObjectByType<ExcelReader>();

        if (excelReader == null)
        {
            Debug.LogError("ExcelReader not found in the scene");
            return;
        }

        excelReader.SetExceFileName(excelFileName);
        objectPricingData = excelReader.FindPrice(pricingObjectName);

        Debug.LogWarning("pricingObjectName " + pricingObjectName);
        if (objectPricingData == null)
        {
            string message = "Excel File or Object Name not found in the excel!";
            Debug.LogError(message);
            //UI_DialogPrompt.Open(message);

            Debug.LogError("File or Object not Found");
        }
        else
        {
            Debug.Log($"Price found for {pricingObjectName} for {objectPricingData.ListPrice} ");
            objectPricingData.isSimFlexArmAvailable = GetSimFlexArmObjectRefrenceInHierarchy();
            PopulateUIWithPricingItems pricingItems = FindObjectOfType<PopulateUIWithPricingItems>(true);
            if(pricingItems != null)
            {
                pricingItems.GenerateUIRow(this);
            }
            else
            {
                Debug.LogError("PopulateUIWithPricingItems not found in the scene");
            }
            
        }
    }

    /// <summary>
    /// Get SimFlexArm Object Reference in Hierarchy if it exists
    /// </summary>
    /// <returns>SimFlex exists?</returns>
    bool GetSimFlexArmObjectRefrenceInHierarchy()
    {
        Selectable[] selectable = transform.root.GetComponentsInChildren<Selectable>();//go to the parent and pick all the selectable objects
        for (int i = 0; i < selectable.Length; i++)
        {
            Debug.Log("Selectable Name " + selectable[i].name , selectable[i].gameObject);
            //actuall object name SimFLEXArm
            if (selectable[i].name.ToLower().Contains("simflexarm")){
                Debug.Log("Yes SimFlexArm Found");
                return true;
            }
        }
        return false;
    }

    private void OnDestroy()
    {
        OnDestroyed?.Invoke();
    }
}
