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
    private readonly HashSet<string> _missCache = new HashSet<string>();
    private readonly object _excelLock = new object();
    private const int MaxCacheEntries = 500;

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
        if (targetObject == null)
            return null;

        var existingPrice = targetObject.GetComponent<SelectablePrice>();
        if (existingPrice != null)
        {
            // Update identity + ensure Excel data (do not bare-return a hollow component).
            ApplyIdentity(existingPrice, isBoomObject, objectName, uiButtonName, excelSheetName, parentName, objectSize);
            existingPrice.EnsurePricingDataLoaded(force: true);
            if (!string.IsNullOrEmpty(fixedPrice) && existingPrice.objectPricingData == null)
                existingPrice.Price = fixedPrice;
            RegisterExistingPricingComponent(existingPrice);
            return existingPrice;
        }

        var selectablePrice = CreateSelectablePrice(
            targetObject, isBoomObject, objectName, uiButtonName, excelSheetName, parentName, objectSize);

        RegisterPrice(selectablePrice);

        // Always try Excel first so objectPricingData is populated for proposals.
        // fixedPrice only fills the display cache when Excel misses — never skips the lookup.
        bool loaded = await LoadPricingDataForComponent(selectablePrice);
        if (!loaded && !string.IsNullOrEmpty(fixedPrice))
            selectablePrice.Price = fixedPrice;

        // Keep the component even on Excel miss so restore/export can retry.
        return selectablePrice;
    }

    /// <summary>
    /// Create or refresh a <see cref="SelectablePrice"/> from saved identity and sync-load Excel.
    /// Used by room/config load — never destroys on miss.
    /// </summary>
    public SelectablePrice EnsurePricingFromIdentity(
        GameObject targetObject,
        string sheetName,
        string pricingObjectName,
        string uiObjectName,
        string size,
        bool isBoomObject,
        bool forceReload = false)
    {
        if (targetObject == null
            || string.IsNullOrEmpty(sheetName)
            || string.IsNullOrEmpty(pricingObjectName))
            return null;

        var price = targetObject.GetComponent<SelectablePrice>();
        if (price == null)
        {
            price = CreateSelectablePrice(
                targetObject, isBoomObject, pricingObjectName, uiObjectName, sheetName, null, size);
            RegisterExistingPricingComponent(price);
            price.EnsurePricingDataLoaded(force: true);
            return price;
        }

        bool identityChanged =
            !string.Equals(price.sheetName, sheetName, StringComparison.Ordinal)
            || !string.Equals(price.pricingObjectName, pricingObjectName, StringComparison.Ordinal)
            || (!string.IsNullOrEmpty(size) && !string.Equals(price.Size, size, StringComparison.Ordinal))
            || price.isBoomObject != isBoomObject;
        ApplyIdentity(price, isBoomObject, pricingObjectName, uiObjectName, sheetName, price.rootParentName, size);
        RegisterExistingPricingComponent(price);
        price.EnsurePricingDataLoaded(force: forceReload || identityChanged || price.objectPricingData == null);
        return price;
    }

    /// <summary>
    /// Walk every TrackedObject and recreate/load SelectablePrice from saved identity
    /// and/or catalog UI button names (legacy saves often omit sheet/price fields).
    /// Call after room/config load (and before proposal export as a safety net).
    /// </summary>
    /// <param name="forceReload">
    /// When true, drop already-loaded Excel data and re-fetch so imported prices apply
    /// to objects already in the scene.
    /// </param>
    public static int RebuildPricingFromTrackedObjects(bool forceReload = false)
    {
        if (Instance == null)
            return 0;

        if (forceReload)
        {
            Instance.ClearCache();
            var existing = CollectActiveSelectablePrices();
            foreach (var sp in existing)
            {
                if (sp == null) continue;
                sp.InvalidateLoadedPricing();
            }
        }

        var tracked = UnityEngine.Object.FindObjectsByType<TrackedObject>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int ensured = 0;
        foreach (var to in tracked)
        {
            if (to == null || !to.gameObject.activeInHierarchy)
                continue;
            var d = to.data;

            if (!string.IsNullOrEmpty(d.sheetName) && !string.IsNullOrEmpty(d.priceObjectName))
            {
                bool isBoom = d.hasIsBoomObject
                    ? d.isBoomObject
                    : InferIsBoomObject(d.sheetName, d.priceObjectName, d.UIObjectName);

                var price = Instance.EnsurePricingFromIdentity(
                    to.gameObject,
                    d.sheetName,
                    d.priceObjectName,
                    d.UIObjectName,
                    d.size,
                    isBoom,
                    forceReload: forceReload);
                if (price != null && price.objectPricingData != null)
                    ensured++;
                continue;
            }

            // Legacy / Carlyn-style saves: pricing identity was never serialized — only UIButtonname.
            string uiBtn = d.UIButtonname;
            if (string.IsNullOrEmpty(uiBtn) && to.TryGetComponent(out Selectable sel))
                uiBtn = sel.UIButtonName;

            if (string.IsNullOrEmpty(uiBtn))
                continue;

            if (ObjectMenu.Instance != null)
                ObjectMenu.Instance.EnsureCatalogPricing(to.gameObject, uiBtn);

            if (to.TryGetComponent(out SelectablePrice attached) && attached.objectPricingData != null)
                ensured++;
        }

        var orphans = CollectActiveSelectablePrices();
        foreach (var sp in orphans)
        {
            if (sp == null)
                continue;
            if (!forceReload && sp.objectPricingData != null)
                continue;
            if (sp.EnsurePricingDataLoaded(force: true) && sp.objectPricingData != null)
                ensured++;
        }

        return ensured;
    }

    /// <summary>
    /// SelectablePrice on objects that are actually in the room (not leftover
    /// inactive configs still sitting in the hierarchy).
    /// </summary>
    public static SelectablePrice[] CollectActiveSelectablePrices()
    {
        var found = UnityEngine.Object.FindObjectsByType<SelectablePrice>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            ?? Array.Empty<SelectablePrice>();

        int keep = 0;
        for (int i = 0; i < found.Length; i++)
        {
            var sp = found[i];
            if (sp == null || !sp.gameObject.activeInHierarchy)
                continue;
            found[keep++] = sp;
        }

        if (keep != found.Length)
            Array.Resize(ref found, keep);
        return found;
    }

    public static bool AnyMissingLoadedPricing(SelectablePrice[] prices)
    {
        if (prices == null)
            return false;
        for (int i = 0; i < prices.Length; i++)
        {
            var sp = prices[i];
            if (sp == null || sp.objectPricingData != null)
                continue;
            if (sp.isBoomObject)
                return true;
            if (!string.IsNullOrEmpty(sp.sheetName) && !string.IsNullOrEmpty(sp.pricingObjectName))
                return true;
        }
        return false;
    }

    public static bool InferIsBoomObject(string sheetName, string pricingObjectName, string uiObjectName)
    {
        if (!string.IsNullOrEmpty(sheetName))
        {
            if (string.Equals(sheetName, DataFilePaths.sheetNameBoomIndividual, StringComparison.OrdinalIgnoreCase)
                || string.Equals(sheetName, DataFilePaths.sheetNameBoomCombined, StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(sheetName, DataFilePaths.sheetNameLight, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        string blob = $"{pricingObjectName} {uiObjectName}".ToLowerInvariant();
        return blob.Contains("boom") || blob.Contains("service head") || blob.Contains("ceiling flange")
               || blob.Contains("duplex") || blob.Contains("outlet");
    }

    static void ApplyIdentity(
        SelectablePrice price,
        bool isBoomObject,
        string objectName,
        string uiButtonName,
        string excelSheetName,
        string parentName,
        string objectSize)
    {
        price.isBoomObject = isBoomObject;
        price.pricingObjectName = objectName;
        price.UIObjectName = uiButtonName;
        price.sheetName = excelSheetName;
        if (!string.IsNullOrEmpty(parentName))
            price.rootParentName = parentName;
        if (price.selectable == null)
            price.selectable = price.GetComponent<Selectable>();
        if (!string.IsNullOrEmpty(objectSize))
            price.Size = objectSize;
        else
            Instance?.DetermineSizeForPricing(price, price.gameObject, null);
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
        // Prefer sync Excel lookup (locked). Keep a yield so callers can remain async
        // without flooding the frame when many components are added.
        await Task.Yield();
        PriceExcelData data = GetCachedPricingData(
            selectablePrice.sheetName, selectablePrice.pricingObjectName, selectablePrice.Size);

        if (data == null)
            return false;

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

        lock (_excelLock)
        {
            if (_priceCache.TryGetValue(cacheKey, out var cachedData))
                return cachedData;

            if (_missCache.Contains(cacheKey))
                return null;

            PriceExcelData data = null;

            if (!string.IsNullOrEmpty(size))
            {
                data = _excelReader?.FetchPricingDataFromExcel(sheetName, objectName, size);
                if (data != null) data.ObjectSize = size;
            }

            if (data == null)
            {
                string noSizeKey = GenerateCacheKey(sheetName, objectName, null);
                if (!string.IsNullOrEmpty(size) && _missCache.Contains(noSizeKey))
                {
                    _missCache.Add(cacheKey);
                    return null;
                }

                data = _excelReader?.FetchPricingDataFromExcel(sheetName, objectName);
                if (data != null) data.ObjectSize = null;
            }

            if (data != null
                && PricingBridgeStore.TryGetOverride(sheetName, objectName, size, out double overridePrice))
            {
                data.ListPrice = overridePrice;
            }

            if (data != null)
            {
                string finalKey = GenerateCacheKey(sheetName, objectName, data.ObjectSize);
                CachePricingData(finalKey, data);
                if (!string.Equals(finalKey, cacheKey, StringComparison.Ordinal))
                    CachePricingData(cacheKey, data);
            }
            else
            {
                _missCache.Add(cacheKey);
                if (!string.IsNullOrEmpty(size))
                    _missCache.Add(GenerateCacheKey(sheetName, objectName, null));
            }

            return data;
        }
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
        // Keep Excel I/O on the main thread — FileStream + Unity objects are not thread-safe.
        foreach (var price in prices.Where(p => p != null))
        {
            price.EnsurePricingDataLoaded(force: true);
            await Task.Yield();
        }
    }

    private async Task LoadPricingDataAsync(SelectablePrice price)
    {
        if (price == null) return;
        await Task.Yield();
        price.EnsurePricingDataLoaded(force: true);
    }

    public void ClearCache()
    {
        _priceCache.Clear();
        _missCache.Clear();
    }

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