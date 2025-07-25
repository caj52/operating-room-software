using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

public class PricingManager : MonoBehaviour
{
    public static PricingManager Instance { get; private set; }

    [Header("Events")]
    public UnityEvent<SelectablePrice> OnPriceAdded = new UnityEvent<SelectablePrice>();
    public UnityEvent<SelectablePrice> OnPriceRemoved = new UnityEvent<SelectablePrice>();
    public UnityEvent<SelectablePrice> OnPriceUpdated = new UnityEvent<SelectablePrice>();
    public UnityEvent OnTotalPriceChanged = new UnityEvent();
    public UnityEvent OnBatchPricesAdded = new UnityEvent();

    [Header("Configuration")]
    [SerializeField] private bool enableAsyncPricing = true;
    [SerializeField] private int maxConcurrentPricingOperations = 5;

    public bool EnableEvents { get; set; } = true;

    private readonly HashSet<SelectablePrice> _activePrices = new HashSet<SelectablePrice>();
    private readonly Dictionary<string, List<SelectablePrice>> _pricesByCategory = new Dictionary<string, List<SelectablePrice>>();
    private ExcelReader _excelReader;
    private readonly Dictionary<string, PriceExcelData> _priceCache = new Dictionary<string, PriceExcelData>();
    private const int MaxCacheEntries = 200;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeExcelReader();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void InitializeExcelReader()
    {
        _excelReader = FindObjectOfType<ExcelReader>();
        if (_excelReader == null)
        {
            Debug.LogError("ExcelReader not found! Pricing system will not work properly.");
        }
    }

    public async Task<SelectablePrice> AddPricingComponent(
        GameObject targetObject,
        bool isBoomObject,
        string objectName,
        string uiButtonName,
        string excelSheetName,
        string parentName = null,
        string fixedPrice = null,
        string objectSize = null)
    {
        var existingPrice = targetObject.GetComponent<SelectablePrice>();
        if (existingPrice != null)
        {
            Debug.LogWarning($"SelectablePrice already exists on {targetObject.name}");
            return existingPrice;
        }

        var selectablePrice = CreateSelectablePrice(targetObject, isBoomObject, objectName, uiButtonName, excelSheetName, parentName, objectSize);

        RegisterPrice(selectablePrice);

        if (!string.IsNullOrEmpty(fixedPrice))
        {
            selectablePrice.Price = fixedPrice;
        }
        else
        {
            var success = await LoadPricingDataForComponent(selectablePrice);
            if (!success)
            {
                RemovePricingComponent(selectablePrice);
                return null;
            }
        }

        return selectablePrice;
    }

    private SelectablePrice CreateSelectablePrice(GameObject targetObject, bool isBoomObject, string objectName,
        string uiButtonName, string excelSheetName, string parentName, string objectSize)
    {
        var selectablePrice = targetObject.AddComponent<SelectablePrice>();
        var selectable = targetObject.GetComponent<Selectable>();

        selectablePrice.isBoomObject = isBoomObject;
        selectablePrice.pricingObjectName = objectName;
        selectablePrice.UIObjectName = uiButtonName;
        selectablePrice.selectable = selectable;
        selectablePrice.rootParentName = parentName;
        selectablePrice.sheetName = excelSheetName;

        DetermineSizeForPricing(selectablePrice, targetObject, objectSize);

        return selectablePrice;
    }

    private void DetermineSizeForPricing(SelectablePrice selectablePrice, GameObject targetObject, string objectSize)
    {
        if (!string.IsNullOrEmpty(objectSize))
        {
            selectablePrice.Size = objectSize;
            return;
        }

        var scaleSelectable = targetObject
            .GetComponentsInChildren<Selectable>(true)
            .FirstOrDefault(s => s.ScaleLevels?.Count > 0);

        if (scaleSelectable == null) return;

        var selectedLevel = scaleSelectable.ScaleLevels.Find(level => level.Selected);
        var sizeLevel = selectedLevel ?? scaleSelectable.CurrentPreviewScaleLevel;

        if (sizeLevel?.Size > 0f)
        {
            selectablePrice.Size = $"{sizeLevel.Size * 1000f}mm";
            selectablePrice.SelectableObjectForSize = scaleSelectable;
        }
    }

    private async Task<bool> LoadPricingDataForComponent(SelectablePrice selectablePrice)
    {
        PriceExcelData data;

        if (enableAsyncPricing)
        {
            data = await Task.Run(() => GetCachedPricingData(selectablePrice.sheetName, selectablePrice.pricingObjectName, selectablePrice.Size));
        }
        else
        {
            data = GetCachedPricingData(selectablePrice.sheetName, selectablePrice.pricingObjectName, selectablePrice.Size);
        }

        if (data == null)
        {
            Debug.LogError($"Pricing not found for \"{selectablePrice.pricingObjectName}\" in sheet \"{selectablePrice.sheetName}\"!");
            return false;
        }

        ApplyPricingData(selectablePrice, data);
        return true;
    }

    private void ApplyPricingData(SelectablePrice selectablePrice, PriceExcelData data)
    {
        selectablePrice.objectPricingData = data;

        bool hasSimFlex = selectablePrice.transform.root
            .GetComponentsInChildren<Selectable>()
            .Any(s => s.name.ToLower().Contains("simflexarm"));

        data.isSimFlexArmAvailable = hasSimFlex;
        double totalPrice = data.ListPrice + (hasSimFlex ? data.SimFlexPrice : 0);
        selectablePrice.Price = totalPrice.ToString("F2");

        if (EnableEvents)
        {
            OnPriceUpdated?.Invoke(selectablePrice);
            OnTotalPriceChanged?.Invoke();
        }
    }

    public void RegisterExistingPricingComponent(SelectablePrice price)
    {
        if (price != null && !_activePrices.Contains(price))
        {
            RegisterPrice(price);
        }
    }

    public void RemovePricingComponent(SelectablePrice price)
    {
        if (price == null) return;
        UnregisterPrice(price);
        Destroy(price);
    }

    public PriceExcelData GetCachedPricingData(string sheetName, string objectName, string size = null)
    {
        string cacheKey = GenerateCacheKey(sheetName, objectName, size);

        if (_priceCache.TryGetValue(cacheKey, out var cachedData))
            return cachedData;

        PriceExcelData data = null;

        if (!string.IsNullOrEmpty(size))
        {
            data = _excelReader?.FetchPricingDataFromExcel(sheetName, objectName, size);
            if (data != null) data.ObjectSize = size;
        }

        if (data == null)
        {
            data = _excelReader?.FetchPricingDataFromExcel(sheetName, objectName);
            if (data != null) data.ObjectSize = null;
        }

        if (data != null)
        {
            string finalKey = GenerateCacheKey(sheetName, objectName, data.ObjectSize);
            CachePricingData(finalKey, data);
        }

        return data;
    }

    private void RegisterPrice(SelectablePrice price)
    {
        _activePrices.Add(price);

        if (!string.IsNullOrEmpty(price.sheetName))
        {
            if (!_pricesByCategory.TryGetValue(price.sheetName, out var categoryList))
            {
                categoryList = new List<SelectablePrice>();
                _pricesByCategory[price.sheetName] = categoryList;
            }
            categoryList.Add(price);
        }

        if (EnableEvents)
        {
            OnPriceAdded?.Invoke(price);
            OnTotalPriceChanged?.Invoke();
        }
    }

    private void UnregisterPrice(SelectablePrice price)
    {
        _activePrices.Remove(price);

        if (!string.IsNullOrEmpty(price.sheetName) && _pricesByCategory.TryGetValue(price.sheetName, out var categoryList))
        {
            categoryList.Remove(price);
            if (categoryList.Count == 0)
                _pricesByCategory.Remove(price.sheetName);
        }

        if (EnableEvents)
        {
            OnPriceRemoved?.Invoke(price);
            OnTotalPriceChanged?.Invoke();
        }
    }

    private void CachePricingData(string key, PriceExcelData data)
    {
        if (_priceCache.Count >= MaxCacheEntries)
        {
            var oldestKey = _priceCache.Keys.First();
            _priceCache.Remove(oldestKey);
        }
        _priceCache[key] = data;
    }

    private string GenerateCacheKey(string sheet, string objName, string size) =>
        string.IsNullOrEmpty(size) ? $"{sheet}:{objName}" : $"{sheet}:{objName}:{size}";

    public double CalculateTotalPrice()
    {
        return _activePrices
            .Where(price => price?.PricingData != null)
            .Sum(price => price.PricingData.ListPrice + (price.PricingData.isSimFlexArmAvailable ? price.PricingData.SimFlexPrice : 0));
    }

    public async Task BatchUpdatePrices(IEnumerable<SelectablePrice> prices)
    {
        var semaphore = new System.Threading.SemaphoreSlim(maxConcurrentPricingOperations);
        var tasks = prices.Where(p => p != null).Select(async price =>
        {
            await semaphore.WaitAsync();
            try
            {
                await LoadPricingDataAsync(price);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task LoadPricingDataAsync(SelectablePrice price)
    {
        if (_excelReader == null || price == null) return;

        await Task.Run(() =>
        {
            try
            {
                price.GetPricingDataFromExcel(price.sheetName);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load pricing data for {price.pricingObjectName}: {e.Message}");
            }
        });
    }

    public void ClearCache() => _priceCache.Clear();

    public string ExportPricingDataToCSV()
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Object Name,UI Name,Part Number,Size,List Price,SimFlex Price,Total Price");

        foreach (var price in _activePrices.Where(p => p?.PricingData != null))
        {
            var data = price.PricingData;
            double totalPrice = data.ListPrice + (data.isSimFlexArmAvailable ? data.SimFlexPrice : 0);
            csv.AppendLine($"{price.pricingObjectName},{price.UIObjectName},{data.PartNumber},{data.ObjectSize},{data.ListPrice},{data.SimFlexPrice},{totalPrice}");
        }

        return csv.ToString();
    }

    public IReadOnlyCollection<SelectablePrice> GetAllPrices() => _activePrices;

    public PricingSummary GeneratePricingSummary()
    {
        var summary = new PricingSummary
        {
            TotalItems = _activePrices.Count,
            TotalPrice = CalculateTotalPrice(),
            Categories = new Dictionary<string, CategorySummary>()
        };

        foreach (var category in _pricesByCategory)
        {
            var categorySummary = new CategorySummary
            {
                ItemCount = category.Value.Count,
                TotalPrice = category.Value
                    .Where(p => p?.PricingData != null)
                    .Sum(p => p.PricingData.ListPrice + (p.PricingData.isSimFlexArmAvailable ? p.PricingData.SimFlexPrice : 0))
            };

            summary.Categories[category.Key] = categorySummary;
        }

        return summary;
    }

    [Serializable]
    public class PricingSummary
    {
        public int TotalItems;
        public double TotalPrice;
        public Dictionary<string, CategorySummary> Categories;
    }

    [Serializable]
    public class CategorySummary
    {
        public int ItemCount;
        public double TotalPrice;
    }
}