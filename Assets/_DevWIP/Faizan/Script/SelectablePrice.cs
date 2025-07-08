using System;
using UnityEngine;

public class SelectablePrice : MonoBehaviour
{
    #region Vars
    public string pricingObjectName;
    public string UIObjectName { get; set; }
    [SerializeField]
    public PriceExcelData objectPricingData;
    public Selectable selectable;
    private Selectable _selectableObjectForSize;
    public string size;
    public string rootParentName;
    public PricingRowDataFill UIRefPricingRowDataFill;
    public Selectable selectableObjectForSize
    {
        get => _selectableObjectForSize;
        set
        {
            // Unsubscribe from the previous Selectable's ScaleUpdated event
            if (_selectableObjectForSize != null)
            {
                _selectableObjectForSize.ScaleUpdated.RemoveListener(OnScaleUpdated);
            }

            // Assign the new Selectable and subscribe to its ScaleUpdated event
            _selectableObjectForSize = value;
            if (_selectableObjectForSize != null)
            {
                Debug.Log("Selectable Object For Size " + _selectableObjectForSize.name, _selectableObjectForSize.gameObject);
                _selectableObjectForSize.ScaleUpdated.AddListener(OnScaleUpdated);
            }
        }
    }
    //public event Action OnDestroyed;
    public string sheetName;
    private PricingRowDataFill uiReferenceForSelectablePrice;
    public bool isBoomObject = false;
    #endregion

    private void Start()
    {
        //OnScaleUpdated();
    }

    public void GetPricingDataFromExcel(string sheetName)
    {
        ExcelReader excelReader = FindExcelReader();
        if (excelReader == null) return;

        this.sheetName = sheetName;
        string size = string.Empty;
        if (isBoomObject)//Currently Only Boom Object Required Size Checking. Boom Excel has size options.
        {
            size = FindSelectableWithSize();
        }

        if (string.IsNullOrEmpty(size))
        {
            //if the size is not available then we will use the no size in matching critera
            objectPricingData = excelReader.FetchPricingDataFromExcel(sheetName, pricingObjectName);
        }
        else
        {
            Debug.Log($"Object Name {pricingObjectName} and size {size} ");

            // Check if the object is a Boom Base Model or not! Boom Base Model is not required size at the moment! The boom based model name already contain the size like XL etc
            bool isBoomBaseModel = UINameToExcelKey.IsBoomBaseModelFromExcel(pricingObjectName);

            objectPricingData = FetchPricingDataSmart(sheetName, pricingObjectName, size);

        }

        if (objectPricingData == null)
        {
            string message = $"Excel File or Object Name not found in the excel. Object: {pricingObjectName}!";
            Debug.LogError(message);
            Destroy(this.GetComponent<SelectablePrice>());
        }
        else
        {
            Debug.Log($"Price found for {pricingObjectName} for {objectPricingData.ListPrice} ");
            objectPricingData.isSimFlexArmAvailable = HasSimFlexArmInTheHirarchey();
            PopulateUIWithPricingItems pricingItems = FindObjectOfType<PopulateUIWithPricingItems>(true);
            pricingItems.OnSetPriceData.Invoke(this,null);
        }
    }

    private void GetPricingDataFromExcelUpdate(string updateSize)
    {
        ExcelReader excelReader = FindExcelReader();
        if (excelReader == null) return;

        string size = updateSize;

        if (string.IsNullOrEmpty(size))
        {
            objectPricingData = excelReader.FetchPricingDataFromExcel(sheetName, pricingObjectName);
        }
        else
        {
            objectPricingData = excelReader.FetchPricingDataFromExcel(sheetName, pricingObjectName, size);
        }

        Debug.LogWarning("pricingObjectName " + pricingObjectName);
        if (objectPricingData == null)
        {
            string message = "Excel File or Object Name not found in the excel!";
            Debug.LogError(message);
            Debug.LogError("File or Object not Found");
        }
        else
        {
            Debug.Log($"Price found for {pricingObjectName} for {objectPricingData.ListPrice} ");
            objectPricingData.isSimFlexArmAvailable = HasSimFlexArmInTheHirarchey();
            //PopulateUIWithPricingItems pricingItems = FindObjectOfType<PopulateUIWithPricingItems>(true);
            //if (uiReferenceForSelectablePrice != null)
            //{
            //    uiReferenceForSelectablePrice.FillData(this);
            //    Debug.Log("Anas =>  Adding Excel Data In Quotation Panel");
            //}
            //else
            //{
            //    Debug.LogError("uiReferenceForSelectablePrice is null! Debug Please");
            //}
        }
    }

    private void OnScaleUpdated()
    {
        var size = selectableObjectForSize.CurrentPreviewScaleLevel.Size;
        var scaleString = $"{size * 1000f}mm";
        Debug.Log($"Scale {scaleString} Updated for", selectableObjectForSize.gameObject);
        objectPricingData.ObjectSize = scaleString;
        GetPricingDataFromExcelUpdate(scaleString);
        this.size = scaleString;
        PopulateUIWithPricingItems pricingUI = FindObjectOfType<PopulateUIWithPricingItems>(true);
        pricingUI.OnSetPriceData.Invoke(null,null);

    }
    public PriceExcelData FetchPricingDataSmart(string sheetName, string objectName, string size = null)
    {
        PriceExcelData data = null;
        ExcelReader excelReader = FindExcelReader();
        // First try to find with size (if size is provided)
        if (!string.IsNullOrEmpty(size))
        {
            data = excelReader.FetchPricingDataFromExcel(sheetName, objectName, size);

            if (data != null)
            {
                Debug.Log($"✅ Found pricing for '{objectName}' with size '{size}' in sheet '{sheetName}'");
                data.ObjectSize = size;
                return data;
            }
            else
            {
                Debug.LogWarning($"⚠️ No pricing found for '{objectName}' with size '{size}' in sheet '{sheetName}'. Trying without size...");
            }
        }

        // Fallback to no-size version
        data = excelReader.FetchPricingDataFromExcel(sheetName, objectName);

        if (data != null)
        {
            Debug.Log($"✅ Found pricing for '{objectName}' without size in sheet '{sheetName}'");
            data.ObjectSize = null; // indicate it's not size-based
        }
        else
        {
            Debug.LogError($"❌ Pricing not found for '{objectName}' in sheet '{sheetName}' (with or without size)");
        }

        return data;
    }

    /// <summary>
    /// Get SimFlexArm Object Reference in Hierarchy if it exists
    /// </summary>
    /// <returns>SimFlex exists?</returns>
    public bool HasSimFlexArmInTheHirarchey()
    {
        Selectable[] selectable = transform.root.GetComponentsInChildren<Selectable>();//go to the parent and pick all the selectable objects
        for (int i = 0; i < selectable.Length; i++)
        {
            //Debug.Log("Selectable Name " + selectable[i].name , selectable[i].gameObject);
            //actuall object name SimFLEXArm
            if (selectable[i].name.ToLower().Contains("simflexarm"))
            {
                Debug.Log("Yes SimFlexArm Found");
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Get the Selectable who has size available. In one object there are multiple selectable scripts attached. So we need to identify that which script has
    /// size. This function will return the size of the object. This function is only required first time when the object is instantiated. After that we dont need to use it instead
    /// we have alternate method which is given in OnScaleUpdated
    /// </summary>
    /// <returns></returns>
    string FindSelectableWithSize()
    {
        // Check if the current object has a valid size
        float size = selectable.CurrentPreviewScaleLevel?.Size ?? 0f;

        //Selectable childSelectableForSize = null;
        // If the size is zero, check the children:
        // It turns out each object (Selectable) you instantiate has multiple Selectable components. And the one component you have 
        //is not have the size value, it in the other component. So we need to check the current object and if it is not found then check the children of the object.
        if (size == 0f)
        {
            foreach (Transform child in selectable.gameObject.GetComponentsInChildren<Transform>())
            {
                Selectable childSelectable = child.GetComponent<Selectable>();

                //if (childSelectable != null && childSelectable.CurrentPreviewScaleLevel?.Size > 0f)
                if (childSelectable != null && childSelectable.ScaleLevels.Count > 0)
                {
                    for (int i = 0; i < childSelectable.ScaleLevels.Count; i++)
                    {
                        if (childSelectable.ScaleLevels[i].Selected)
                        {
                            Debug.Log("Child Selectable Found who has size: Name: " + childSelectable.name, childSelectable.gameObject);
                            size = childSelectable.ScaleLevels[i].Size;
                            selectableObjectForSize = childSelectable;
                            var scaleString = $"{size * 1000f}mm";
                            Debug.Log($"Object Object: {pricingObjectName} scale is  {scaleString} ");
                            return scaleString; // Return the size as a string
                        }
                    }
                }
                else
                {
                    //Debug.Log("Child Name: " + child.name, child.gameObject);
                }
            }
            return null;
        }
        Debug.Log($"Object Object: {pricingObjectName} scale is  {size} found from same selectable", selectable.gameObject);
        this.size = size.ToString();
        return size.ToString();
    }

    private ExcelReader FindExcelReader()
    {
        ExcelReader excelReader = FindAnyObjectByType<ExcelReader>();
        if (excelReader == null)
        {
            Debug.LogError("ExcelReader not found in the scene");
        }
        return excelReader;
    }

    private void OnDestroy()
    {
        // Unsubscribe from the ScaleUpdated event to avoid memory leaks
        if (_selectableObjectForSize != null)
        {
            _selectableObjectForSize.ScaleUpdated.RemoveListener(OnScaleUpdated);
        }
        PopulateUIWithPricingItems pricingUI = FindObjectOfType<PopulateUIWithPricingItems>(true);
        pricingUI.OnClearSp(this);
        //OnDestroyed?.Invoke();//Triggering event on destory so that relvant Objects e.g., UI should be destroy as well.
        Debug.Log("Anas Destroy SelectablePrice");
    }

   
}
