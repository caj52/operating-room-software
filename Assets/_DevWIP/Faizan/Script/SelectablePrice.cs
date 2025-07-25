using System.Threading.Tasks;
using System;
using UnityEngine;
using System.Linq;

public class SelectablePrice : MonoBehaviour
{
    [Header("Pricing Configuration")]
    public string pricingObjectName;
    public string UIObjectName { get; set; }
    public string rootParentName;

    [Header("Pricing Data")]
    public PriceExcelData objectPricingData;   // Loaded pricing data (includes part number, list price, etc.)
    [SerializeField] private string cachedPrice;
    [SerializeField] private string cachedSize;

    [Header("References")]
    public Selectable selectable;
    public PricingRowDataFill UIRefPricingRowDataFill;  // (UI linkage, if any)

    [Header("Configuration")]
    public bool isBoomObject = false;
    public string sheetName;

    // Private fields
    private Selectable _selectableObjectForSize;
    private bool _isInitialized = false;

    // ** Removed static ExcelReader and PricingCache – now use PricingManager for data and caching **

    // Properties
    public string Price
    {
        get => cachedPrice;
        set => cachedPrice = value;
    }
    public string Size
    {
        get => cachedSize;
        set => cachedSize = value;
    }
    public PriceExcelData PricingData
    {
        get => objectPricingData;
        set => objectPricingData = value;
    }
    public Selectable SelectableObjectForSize
    {
        get => _selectableObjectForSize;
        set
        {

            // Unsubscribe from old Selectable's scale event if exists
            if (_selectableObjectForSize != null)
            {
                _selectableObjectForSize.ScaleUpdated.RemoveListener(OnScaleUpdated);
            }
            _selectableObjectForSize = value;
            if (_selectableObjectForSize != null)
            {
                // Subscribe to scale changes to update pricing when size/scale changes
                _selectableObjectForSize.ScaleUpdated.AddListener(OnScaleUpdated);
            }
        }
    }

    private void Awake()
    {
        // Ensure the PricingManager (and ExcelReader) is initialized
        PricingManager.Instance.TryGetComponent<PricingManager>(out _);
        // Note: PricingManager.Instance access will create the manager if not present.
    }

    private void Start()
    {
        // If this component was added via scene loading or not yet initialized through PricingManager, initialize it now
        if (!_isInitialized && !string.IsNullOrEmpty(sheetName))
        {
            InitializePricing();
        }
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    /// <summary>Initializes pricing data retrieval for this object.</summary>
    private void InitializePricing()
    {
        if (string.IsNullOrEmpty(pricingObjectName))
        {
            Debug.LogWarning("Pricing object name is not set; skipping price initialization.");
            return;
        }

        // Register this price component with the PricingManager for tracking and UI updates
        PricingManager.Instance.RegisterExistingPricingComponent(this);

        // Begin fetching pricing data from Excel (async)
        GetPricingDataFromExcel(sheetName);
        _isInitialized = true;
    }

    /// <summary>
    /// Set up all initial properties of the SelectablePrice (used when added manually before centralizing in PricingManager).
    /// </summary>
    public void Initialize(string objectName, string uiName, string excelName, string parentName = null, string price = null, string size = null)
    {
        pricingObjectName = objectName;
        UIObjectName = uiName;
        sheetName = excelName;
        rootParentName = parentName;
        cachedPrice = price;
        cachedSize = size;

        if (selectable == null)
        {
            selectable = GetComponent<Selectable>();
        }
        InitializePricing();
    }

    /// <summary>
    /// Fetch pricing data from Excel via the PricingManager (async).
    /// </summary>
    public async void GetPricingDataFromExcel(string sheet)
    {
        if (PricingManager.Instance == null)
        {
            Debug.LogError("PricingManager is not available – cannot retrieve pricing data.");
            return;
        }

        sheetName = sheet;
        try
        {
            // If this is a boom object with no size determined yet, attempt to find size on main thread
            if (isBoomObject && string.IsNullOrEmpty(cachedSize))
            {
                // Try to find the current selectable's size (this runs on the main thread)
                cachedSize = await FindSelectableWithSizeAsync();
            }

            // Fetch data from PricingManager’s cache/Excel (runs in background thread)
            PriceExcelData data = await Task.Run(() =>
                PricingManager.Instance.GetCachedPricingData(sheetName, pricingObjectName, cachedSize)
            );

            if (data == null)
            {
                // Handle case where no pricing data is found for the given object (and size)
                HandlePricingDataNotFound();
            }
            else
            {
                ApplyPricingData(data);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load pricing data for {pricingObjectName}: {e.Message}");
            HandlePricingDataNotFound();
        }
    }

    // Determine pricing data with and without size (logic now handled by PricingManager.GetCachedPricingData)

    #region Size Handling
    private Task<string> FindSelectableWithSizeAsync()
    {
        // Ensure we query the Selectable’s size on the main thread
        var tcs = new TaskCompletionSource<string>();
        MainThreadDispatcher.Enqueue(() =>
        {
            string size = FindSelectableWithSize();  // Synchronous check on main thread
            tcs.SetResult(size);
        });
        return tcs.Task;
    }

    private string FindSelectableWithSize()
    {
        // Only consider the Selectable on *this* GameObject
        if (selectable == null) selectable = GetComponent<Selectable>();
        if (selectable == null)
        {
            Debug.LogWarning($"No Selectable component found on {gameObject.name} for size detection.");
            return null;
        }

        // If the Selectable has discrete scale levels, use the selected level’s size
        if (selectable.ScaleLevels != null && selectable.ScaleLevels.Count > 0)
        {
            var selectedLevel = selectable.ScaleLevels.Find(level => level.Selected);
            if (selectedLevel != null)
            {
                SelectableObjectForSize = selectable;  // track for future updates
                return FormatSize(selectedLevel.Size);
            }
            // If no level explicitly selected, fall back to current preview scale (if any)
            float size = selectable.CurrentPreviewScaleLevel?.Size ?? 0f;
            if (size > 0f)
            {
                SelectableObjectForSize = selectable;
                return FormatSize(size);
            }
        }
        // No size info available on this Selectable
        return null;
    }

    private void OnScaleUpdated()
    {

        float newSizeValue = _selectableObjectForSize.CurrentPreviewScaleLevel?.Size ?? 0f;
        if (newSizeValue > 0f)
        {
            cachedSize = FormatSize(newSizeValue);
            if (objectPricingData != null) objectPricingData.ObjectSize = cachedSize;
            // Fetch and apply updated pricing for the new size
            UpdatePricingDataForSize(cachedSize);
        }
    }

    private async void UpdatePricingDataForSize(string newSize)
    {
        // Get updated pricing for the new size (async)
        PriceExcelData updatedData = await Task.Run(() =>
            PricingManager.Instance.GetCachedPricingData(sheetName, pricingObjectName, newSize)
        );
        if (updatedData != null)
        {
            ApplyPricingData(updatedData);
        }
        // (If no data found for this size, we keep the last known price; alternatively, could HandlePricingDataNotFound)
    }

    private string FormatSize(float size)
    {
        return $"{size * 1000f}mm";  // e.g., 1.2 -> "1200mm"
    }
    #endregion

    #region Pricing Data Application & Cleanup
    /// <summary>Applies fetched pricing data to this SelectablePrice and updates the UI.</summary>
    private void ApplyPricingData(PriceExcelData data)
    {
        objectPricingData = data;
        // Determine if a SimFlex Arm exists in the hierarchy and mark the data accordingly
        objectPricingData.isSimFlexArmAvailable = HasSimFlexArmInHierarchy();

        // Calculate total price for this item (base price + optional SimFlex addon)
        double totalPrice = data.ListPrice;
        if (objectPricingData.isSimFlexArmAvailable)
        {
            totalPrice += data.SimFlexPrice;
        }
        cachedPrice = totalPrice.ToString("F2");

        Debug.Log($"✅ Pricing found for '{pricingObjectName}' – Total Price: ${cachedPrice}");

        // Notify through PricingManager that this price data has been updated (UI will handle update)
        PricingManager.Instance.OnPriceUpdated?.Invoke(this);
        PricingManager.Instance.OnTotalPriceChanged?.Invoke();
    }

    private void HandlePricingDataNotFound()
    {
        Debug.LogError($"❌ Excel pricing entry not found for \"{pricingObjectName}\" (sheet: {sheetName})");
        // Remove this component since it has no valid price data
        PricingManager.Instance.RemovePricingComponent(this);
    }

    /// <summary>
    /// Checks if any Selectable in this object’s hierarchy is a SimFlex Arm (affects pricing).
    /// </summary>
    private bool HasSimFlexArmInHierarchy()
    {
        var selectables = transform.root.GetComponentsInChildren<Selectable>();
        return selectables.Any(s => s.name.ToLower().Contains("simflexarm"));
    }

    /// <summary>
    /// Performs cleanup when this price component is destroyed.
    /// </summary>
    private void Cleanup()
    {
        // Detach event listener for scale changes
        if (_selectableObjectForSize != null)
        {
            _selectableObjectForSize.ScaleUpdated.RemoveListener(OnScaleUpdated);
            Destroy(UIRefPricingRowDataFill);
        }

        if (PricingManager.Instance != null)
        {
            PricingManager.Instance.RemovePricingComponent(this);
        }

        // 💡 Fallback UI cleanup if no one else destroyed the UI row yet
        if (UIRefPricingRowDataFill != null)
        {
            UIRefPricingRowDataFill.DestroyRow();  // safe: DestroyRow() uses a guard flag
        }
        Debug.Log($"SelectablePrice component on {gameObject.name} destroyed.");
    }
    #endregion
}
