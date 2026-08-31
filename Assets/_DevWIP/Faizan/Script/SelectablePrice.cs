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
        // During room/config load, TrackedObject.StoreValues + post-load rebuild own pricing.
        // Starting an async fetch with prefab/empty identity races restore and used to destroy components.
        if (ConfigurationManager.IsLoading)
            return;

        if (!_isInitialized && !string.IsNullOrEmpty(sheetName) && !string.IsNullOrEmpty(pricingObjectName))
            InitializePricing();
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

        PricingManager.Instance.RegisterExistingPricingComponent(this);
        _isInitialized = EnsurePricingDataLoaded(force: true);
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
    /// Fetch pricing data from Excel via the PricingManager.
    /// </summary>
    public void GetPricingDataFromExcel(string sheet)
    {
        if (!string.IsNullOrEmpty(sheet))
            sheetName = sheet;
        EnsurePricingDataLoaded(force: true);
    }

    /// <summary>
    /// Synchronously ensure <see cref="objectPricingData"/> is populated.
    /// Does not destroy the component on miss.
    /// </summary>
    /// <param name="force">When true, re-fetch even if data already exists (identity/size may have changed).</param>
    public bool EnsurePricingDataLoaded(bool force = false)
    {
        if (!force && objectPricingData != null)
            return true;
        if (string.IsNullOrEmpty(sheetName) || string.IsNullOrEmpty(pricingObjectName))
            return false;
        if (PricingManager.Instance == null)
            return false;

        if (string.IsNullOrEmpty(cachedSize))
            cachedSize = FindSelectableWithSize();

        PriceExcelData data = PricingManager.Instance.GetCachedPricingData(
            sheetName, pricingObjectName, cachedSize);
        if (data == null)
            return false;

        ApplyPricingData(data);
        _isInitialized = true;
        PricingManager.Instance.RegisterExistingPricingComponent(this);
        return true;
    }

    /// <summary>Drop cached Excel row so the next load can pick up imported prices.</summary>
    public void InvalidateLoadedPricing()
    {
        objectPricingData = null;
        _isInitialized = false;
    }

    // Determine pricing data with and without size (logic now handled by PricingManager.GetCachedPricingData)

    #region Size Handling
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

    private void UpdatePricingDataForSize(string newSize)
    {
        if (PricingManager.Instance == null)
            return;
        PriceExcelData updatedData = PricingManager.Instance.GetCachedPricingData(
            sheetName, pricingObjectName, newSize);
        if (updatedData != null)
            ApplyPricingData(updatedData);
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

        // Notify through PricingManager that this price data has been updated (UI will handle update)
        PricingManager.Instance.OnPriceUpdated?.Invoke(this);
        PricingManager.Instance.OnTotalPriceChanged?.Invoke();
    }

    private void HandlePricingDataNotFound(string objectName = null, string sheet = null)
    {
        // Miss is expected for accessories without sheet rows — keep the component for retry.
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
    }
    #endregion
}
