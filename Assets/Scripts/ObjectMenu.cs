using FuzzySharp;
using Newtonsoft.Json;
using SplenSoft.UnityUtilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Singleton object that displays a menu to instantiate selectables.
/// </summary>
public class ObjectMenu : MonoBehaviour
{
    public class ObjectMenuItem
    {
        public SelectableData SelectableData { get; set; }
        public SelectableMetaData SelectableMetaData { get; set; }
        public GameObject GameObject { get; set; }
        public string CustomFile { get; set; }
        public bool ValidForSearch { get; set; } = true;
        public double LevenshteinRatio { get; set; }
    }

    private static bool _selectCompatibleObjectsMode;
    public static ObjectMenu Instance { get; private set; }
    public static UnityEvent ActiveStateChanged { get; } = new();
    public static UnityEvent LastOpenedSelectableChanged { get; } = new();
    private static bool _initialized;

    private static List<string> _activeAssetBundleNames = new();
    private List<GameObject> _instantiatedCategories = new();

    private const int _minFuzzyRatio = 50;

    [field: SerializeField]
    private GameObject ItemTemplate { get; set; }

    [field: SerializeField]
    private TextMeshProUGUI ItemTemplateTextObjectName
    { get; set; }

    [field: SerializeField]
    private GameObject TemplateCategory { get; set; }

    [field: SerializeField]
    private TMP_InputField InputField_Search
    { get; set; }

    [field: SerializeField]
    private GameObject Categories { get; set; }

    private AttachmentPoint _attachmentPoint;

    public List<ObjectMenuItem> ObjectMenuItems
    { get; private set; } = new();

    public static Selectable LastOpenedSelectable
    { get; private set; }

    public static SelectableData LastOpenedSelectableData
    { get; private set; }

    private bool SearchIsActive =>
        !string.IsNullOrWhiteSpace(InputField_Search.text);

    private List<string> _currentCategoryFilters = new();
    private List<string> _allCategories = new();

    #region Monobehaviour

    private void Awake()
    {
        Instance = this;

        InputField_Search.onValueChanged
            .AddListener(UpdateSearchFilter);

        SelectableAssetBundles.CatalogUpdated += OnCatalogUpdated;

        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        SelectableAssetBundles.CatalogUpdated -= OnCatalogUpdated;

        _initialized = false;
        _selectCompatibleObjectsMode = false;

        InputField_Search.onValueChanged
            .RemoveListener(UpdateSearchFilter);
    }

    private IEnumerator Start()
    {

        var loadingToken = Loading.GetLoadingToken();

        yield return new WaitUntil
            (() => SelectableAssetBundles.Initialized);

        Initialize();
        loadingToken.Done();
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        _selectCompatibleObjectsMode = false;
        ActiveStateChanged?.Invoke();
    }

    private void OnEnable()
    {
        ActiveStateChanged?.Invoke();
    }

    private static bool _initializing;
    private static bool _pendingCatalogRefresh;

    private void OnCatalogUpdated()
    {
        if (!_initialized || _initializing)
        {
            _pendingCatalogRefresh = true;
            return;
        }

        Regenerate();
    }

    private async Task DeferredCatalogRefreshRegenerate()
    {
        await Task.Yield();
        if (!Application.isPlaying || !_initialized || _initializing)
            return;

        Regenerate();
    }

    #endregion

    private void GenerateCategories()
    {
        _instantiatedCategories
            .ForEach(x => Destroy(x));

        _instantiatedCategories.Clear();

        _allCategories = ObjectMenuItems
            .Where(x => x.SelectableMetaData != null)
            .SelectMany(x => x.SelectableMetaData.Categories)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        Debug.Log($"Found {_allCategories.Count} object menu categories");

        if (SceneManager.GetActiveScene().name != "ObjectEditor")
            _allCategories.Add("Save Data");

        _currentCategoryFilters.Clear();

        TemplateCategory.SetActive(true);

        _allCategories.ForEach(AddCategory);

        TemplateCategory.SetActive(false);
    }

    private void AddCategory(string category)
    {
        var newObj = Instantiate(TemplateCategory,
                TemplateCategory.transform.parent);

        var toggle = newObj
            .GetComponentInChildren<Toggle>();

        toggle.SetIsOnWithoutNotify(false);

        newObj.GetComponentInChildren
            <TextMeshProUGUI>().text = category;

        _instantiatedCategories.Add(newObj);

        toggle.onValueChanged.AddListener(isOn =>
        {
            if (isOn &&
            !_currentCategoryFilters.Contains(category))
            {
                _currentCategoryFilters.Add(category);
                RefilterItems();
            }
            else if (!isOn &&
            _currentCategoryFilters.Contains(category))
            {
                _currentCategoryFilters.Remove(category);
                RefilterItems();
            }
        });
    }

    private void ResetAllCategoryToggles()
    {
        foreach (var item in _instantiatedCategories)
        {
            var toggle = item.GetComponentInChildren<Toggle>();
            if (!toggle.isOn)
            {
                toggle.SetIsOnWithoutNotify(false);
            }
        }
    }

    private void ClearSearchValidity()
    {
        //Debug.Log("Items cleared for search validity");
        InputField_Search.text = string.Empty;
        ObjectMenuItems.ForEach(x => x.ValidForSearch = true);
    }

    private void UpdateCategoryVisibility(bool onlyShowStandalones)
    {
        if (!onlyShowStandalones)
        {
            _instantiatedCategories.ForEach(x =>
            {
                string text = x.GetComponentInChildren<TextMeshProUGUI>(true).text;
                bool isFile = text == "Save Data";

                if (isFile)
                {
                    x.SetActive(SceneManager.GetActiveScene().name != "ObjectEditor");
                    return;
                }

                x.SetActive(true);

            });
            return;
        }

        var validCats = ObjectMenuItems
            .Where(x => x.SelectableMetaData != null &&
                x.SelectableData.MetaData.IsStandalone)
            .SelectMany(x => x.SelectableMetaData.Categories)
            .Distinct()
            .ToList();

        Debug.Log("Valid categories:");
        validCats.ForEach(x => Debug.Log(x));

        _instantiatedCategories
            .ForEach(x =>
            {
                string text = x.GetComponentInChildren<TextMeshProUGUI>(true).text;
                bool textIsMatch = validCats.Contains(text);
                bool isFile = text == "Save Data";

                if (isFile)
                {
                    x.SetActive(SceneManager.GetActiveScene().name != "ObjectEditor");
                    return;
                }

                x.SetActive(textIsMatch);
            });
    }

    private void UpdateSearchFilter(string searchText)
    {
        if (!gameObject.activeSelf) return;

        if (string.IsNullOrWhiteSpace(searchText.ToLower()))
        {
            //Debug.Log("Search filter nullified");
            ObjectMenuItems.ForEach(x => x.ValidForSearch = true);
            RefilterItems();
            return;
        }

        ObjectMenuItems.ForEach(x =>
        {
            // Name
            string search = searchText.ToLower();
            string nameToCompare = (x.SelectableMetaData?.Name ?? x.GameObject.GetComponentInChildren<TextMeshProUGUI>().text).ToLower();
            double ratio = Fuzz.PartialRatio(nameToCompare, search);

            if (x.SelectableMetaData != null)
            {
                // Categories
                foreach (var category in x.SelectableMetaData.Categories)
                {
                    ratio = Math.Max(ratio, Fuzz.Ratio(searchText, category.ToLower()));
                }

                // Keywords
                foreach (var keyword in x.SelectableMetaData.KeyWords)
                {
                    ratio = Math.Max(ratio, Fuzz.Ratio(searchText, keyword.ToLower()));
                }
            }

            x.LevenshteinRatio = ratio;

            if (ratio >= _minFuzzyRatio)
            {
                x.ValidForSearch = true;
            }
            else
            {
                x.LevenshteinRatio = 0;
                x.ValidForSearch = false;
            }
        });

        ObjectMenuItems = ObjectMenuItems.OrderByDescending(x => x.LevenshteinRatio).ToList();

        for (int i = 0; i < ObjectMenuItems.Count; i++)
        {
            //Debug.Log(ObjectMenuItems[i].LevenshteinRatio);
            ObjectMenuItems[i].GameObject.transform.SetSiblingIndex(i);
        }

        RefilterItems();
    }

    private void RefilterItems()
    {
        if (_selectCompatibleObjectsMode)
        {
            FilterMenuItems(_activeAssetBundleNames);
        }
        else if (_attachmentPoint != null)
        {
            FilterMenuItems(_attachmentPoint);
        }
        else
        {
            ClearMenuFilter();
        }
    }

    public static void Regenerate()
    {
        foreach (var item in Instance.ObjectMenuItems)
        {
            Destroy(item.GameObject);
        }

        Instance.ObjectMenuItems.Clear();
        Instance.Initialize();
    }
    #region Initialization
    private async void Initialize()
    {
        _initializing = true;
        var loadingToken = Loading.GetLoadingToken();

        try
        {
            while (!Database.Initialized || !SelectableAssetBundles.Initialized)
            {
                await Task.Yield();
                if (!Application.isPlaying) return;
            }

            int activeTasks = 0;
            var dataList = SelectableAssetBundles.GetSelectableData().ToList();

            foreach (var data in dataList)
            {
                activeTasks++;
                _ = SetupObjectMenuItem(data, () => activeTasks--);
            }

            while (activeTasks > 0)
            {
                await Task.Yield();
                if (!Application.isPlaying)
                    throw new AppQuitInTaskException();
            }

            ObjectMenuItems = ObjectMenuItems
                .OrderBy(x => x.GameObject.GetComponentInChildren<TextMeshProUGUI>().text)
                .ToList();

            for (int i = 0; i < ObjectMenuItems.Count; i++)
                ObjectMenuItems[i].GameObject.transform.SetSiblingIndex(i);

            AssetPipelineDiagnostics.Log("ObjectMenu", $"Initialize complete — {ObjectMenuItems.Count} menu items");

            AddSavedRoomConfigs();
            ItemTemplate.SetActive(false);
            GenerateCategories();
            _initialized = true;
            Database.SetIsUpToDate();

            if (_pendingCatalogRefresh)
            {
                _pendingCatalogRefresh = false;
                _ = DeferredCatalogRefreshRegenerate();
            }
        }
        finally
        {
            _initializing = false;
            loadingToken.Done();
        }
    }

    private async Task SetupObjectMenuItem(SelectableData data, Action onComplete)
    {
        try
        {
            string objectName = data.PrefabName;
            var task = Database.GetMetaData(data.AssetBundleName, data.MetaData);
            await task;

            if (!Application.isPlaying)
                throw new AppQuitInTaskException();

            SelectableMetaData metadata = data.MetaData;
            if (task.Result.ResultType == Database.MetaDataOpertaionResultType.Success)
            {
                objectName = task.Result.MetaData.Name;
                metadata = task.Result.MetaData;
            }

            ItemTemplateTextObjectName.text = objectName;
            var newMenuItem = Instantiate(ItemTemplate, ItemTemplate.transform.parent);

            newMenuItem.GetComponentInChildren<Button>().onClick.AddListener(() => OnObjectMenuItemClicked(data, newMenuItem, objectName));

            ObjectMenuItems.Add(new ObjectMenuItem
            {
                SelectableData = data,
                GameObject = newMenuItem,
                SelectableMetaData = metadata
            });
        }
        finally
        {
            onComplete?.Invoke();
        }
    }

    private async void OnObjectMenuItemClicked(SelectableData data, GameObject newMenuItem, string objectName)
    {
        if (_selectCompatibleObjectsMode)
        {
            UI_ObjectEditor.AddCompatibleObject(data.AssetBundleName);
            return;
        }

        gameObject.SetActive(false);
        AssetPipelineDiagnostics.LogSelectableData("ObjectMenu.Click", data, $"menuLabel='{objectName}'");
        DebugMetaData(data, newMenuItem);

        var task = data.GetPrefab();
        await task;
        if (!Application.isPlaying) return;

        GameObject prefab = task.Result;
        if (prefab == null)
        {
            AssetPipelineDiagnostics.Log("ObjectMenu.Click", $"GetPrefab returned null for '{objectName}'");
            return;
        }

        AssetPipelineDiagnostics.LogPrefabSnapshot("ObjectMenu.Click", prefab, "prefab before Instantiate");

        var instantiateTimer = System.Diagnostics.Stopwatch.StartNew();
        GameObject newObj = Instantiate(prefab);
        instantiateTimer.Stop();
        AssetPipelineDiagnostics.LogElapsed("ObjectMenu.Click", "Instantiate", instantiateTimer);

        AssetPipelineDiagnostics.LogPrefabSnapshot("ObjectMenu.Click", newObj, "instance after Instantiate");
        var selectable = newObj.GetComponent<Selectable>();
        selectable.UIButtonName = newMenuItem.GetComponentInChildren<TextMeshProUGUI>().text;
        LastOpenedSelectable = selectable;
        LastOpenedSelectableData = data;
        LastOpenedSelectableChanged?.Invoke();

        if (SceneManager.GetActiveScene().name != "ObjectEditor")
            PlaceAndInitializeSelectable(newObj, selectable, objectName, newMenuItem);
    }

    private void DebugMetaData(SelectableData data, GameObject newMenuItem)
    {
        if (data.MetaData != null)
        {
            string json = JsonConvert.SerializeObject(data.MetaData, Formatting.Indented);
            Debug.Log($"Metadata properties of {newMenuItem.GetComponentInChildren<TextMeshProUGUI>().text}: {json}");
        }
        else
        {
            Debug.LogWarning($"MetaData is null for {newMenuItem.GetComponentInChildren<TextMeshProUGUI>().text}");
        }
    }

    private void PlaceAndInitializeSelectable(GameObject obj, Selectable selectable, string objectName, GameObject newMenuItem)
    {
        if (_attachmentPoint != null)
        {
            // Compensate the parent selectable's APs before parenting so Unity does not
            // bake 1/coverZ into the new object's local scale.
            Selectable.EnsureAttachChainForAttachmentPoint(_attachmentPoint);

            _attachmentPoint.SetAttachedSelectable(selectable);
            selectable.ParentAttachmentPoint = _attachmentPoint;
            obj.transform.SetPositionAndRotation(_attachmentPoint.transform.position, _attachmentPoint.transform.rotation);
            obj.transform.SetParent(_attachmentPoint.transform, true);

            // If anything still baked an inverse onto the new root, strip it now that it is a child.
            Selectable.EnsureAttachChainForAttachmentPoint(_attachmentPoint);

            selectable.EnsureLengthOwnerParentShellNormalized();
            if (selectable.RelatedSelectables != null)
            {
                foreach (var rel in selectable.RelatedSelectables)
                {
                    if (rel == null || rel == selectable)
                        continue;
                    rel.EnsureLengthOwnerParentShellNormalized();
                }
            }

            PlacementLoadOptimizer.FinalizeInstanceColliders(obj);
            PlacementLoadOptimizer.RestoreInstanceCollidersAfterLoad(obj);

            var recorder = obj.transform.root.GetComponent<RecordHirarcheySelectables>();
            recorder?.AddAttachedSelectables(selectable, objectName);
        }
        else
        {
            var recorder = obj.AddComponent<RecordHirarcheySelectables>();
            recorder.AddAttachedSelectables(selectable, objectName);
            selectable.StartRaycastPlacementMode();
        }

        string uiBtnName = newMenuItem.GetComponentInChildren<TextMeshProUGUI>().text;
        Debug.Log($"Object Instantiated :: Menu Name: {uiBtnName} and GameObject Name: {obj.name}", obj.transform);

        HandleBoomRestrictions(obj);
        HandleOutletAndPricing(obj, uiBtnName);
    }

    private void HandleBoomRestrictions(GameObject obj)
    {
        if (obj.name.Contains("CeilingMount_Double") ||
            obj.name.Contains("BoomDropTube") ||
            obj.name.Contains("ArmDropTube") ||
            obj.name.Contains("SimFlexTube"))
        {
            var restriction = obj.GetComponentInParent<TandomRestrictions>();
            restriction?.CheckTandemRestrictions(restriction.gameObject);
        }
    }

    // Replace the HandleOutletAndPricing method in ObjectMenu.cs with this optimized version:

    public void HandleOutletAndPricing(GameObject obj, string uiBtnName, TrackedObject.Data? trackedObject = null)
    {
        if (ConfigurationManager.IsLoading)
            return;

        EnsureCatalogPricing(obj, uiBtnName);
    }

    /// <summary>
    /// Sync attach/refresh of SelectablePrice from a catalog UI button name.
    /// Safe during room load — does not early-out on <see cref="ConfigurationManager.IsLoading"/>.
    /// </summary>
    public void EnsureCatalogPricing(GameObject obj, string uiBtnName)
    {
        if (obj == null || string.IsNullOrWhiteSpace(uiBtnName))
            return;

        uiBtnName = uiBtnName.Trim();

        string name = obj.name;
        if (IsOutlet(name))
            ValidateOutletConfiguration(obj);

        var pricingConfig = GetPricingConfiguration(obj, name, uiBtnName);
        if (!pricingConfig.ShouldAddPrice)
            return;

        if (pricingConfig.ShouldRemoveExistingPrice && pricingConfig.OutletParent != null)
        {
            var existingPrice = pricingConfig.OutletParent.GetComponentInChildren<SelectablePrice>();
            if (existingPrice != null)
                Destroy(existingPrice);
        }

        if (PricingManager.Instance != null)
        {
            PricingManager.Instance.EnsurePricingFromIdentity(
                obj,
                pricingConfig.ExcelFileName,
                pricingConfig.PricingObjectName,
                pricingConfig.UIButtonName,
                size: null,
                isBoomObject: pricingConfig.IsBoomObject);
        }

        if (pricingConfig.RequiresDuplexWatcher)
            AddDuplexWatcher(obj, uiBtnName);
    }
    // Helper methods to support the optimized HandleOutletAndPricing:



    private bool IsOutlet(string objName)
    {
        return objName.Equals("Outlet_HV_Power(Clone)") ||
               objName.Contains("GasOutlet") ||
               objName.Equals("EthernetOutlet") ||
               objName.Equals("BlankOutlet(Clone)");
    }

    private void ValidateOutletConfiguration(GameObject obj)
    {
        var outletParent = obj.transform.parent?.parent?.gameObject;
        var grandParent = outletParent?.transform.parent?.parent?.gameObject;

        if (grandParent != null)
        {
            obj.GetComponentInParent<BoomOutletValidator>()?.ValidateBoomConfiguration(grandParent);
        }
    }

    private PricingConfiguration GetPricingConfiguration(GameObject obj, string objName, string uiBtnName)
    {
        var config = new PricingConfiguration
        {
            GameObject = obj,
            UIButtonName = uiBtnName,
            PricingObjectName = uiBtnName,
            IsBoomObject = uiBtnName.StartsWith("Boom", StringComparison.OrdinalIgnoreCase),
            // Only catalog boom/light (and outlet) rows map to the pricing workbook.
            ShouldAddPrice = uiBtnName.StartsWith("Boom", StringComparison.OrdinalIgnoreCase)
                             || uiBtnName.StartsWith("Lights", StringComparison.OrdinalIgnoreCase)
                             || uiBtnName.StartsWith("Light", StringComparison.OrdinalIgnoreCase)
                             || IsOutlet(objName)
        };

        // Special handling for boom objects
        if (config.IsBoomObject)
        {
            config.ExcelFileName = DataFilePaths.sheetNameBoomIndividual;

            // Special handling for HV outlets
            if (objName.Equals("Outlet_HV_Power(Clone)"))
            {
                var duplexConfig = GetDuplexConfiguration(obj);
                config.UIButtonName = duplexConfig.UIName;
                config.PricingObjectName = duplexConfig.PricingName;
                config.ShouldAddPrice = !string.IsNullOrEmpty(duplexConfig.UIName);
                config.RequiresDuplexWatcher = config.ShouldAddPrice;
                config.ShouldRemoveExistingPrice = duplexConfig.RemoveExisting;
                config.OutletParent = duplexConfig.Parent;
            }
        }
        else
        {
            // Non-boom objects (lights, etc.)
            config.ExcelFileName = DataFilePaths.sheetNameLight;
            config.PricingObjectName = uiBtnName;
            config.UIButtonName = config.PricingObjectName;
        }

        return config;
    }

    private DuplexConfiguration GetDuplexConfiguration(GameObject hvOutlet)
    {
        var config = new DuplexConfiguration();
        var outletParent = hvOutlet.transform.parent?.parent?.gameObject;
        config.Parent = outletParent;

        if (outletParent != null)
        {
            int duplexCount = outletParent.GetComponentsInChildren<Selectable>()
                .Count(s => s.MetaData?.Name == "HV Power Outlet");

            switch (duplexCount)
            {
                case 2:
                    config.UIName = "Electrical (2 Duplex)";
                    config.PricingName = config.UIName;
                    break;
                case 3:
                    config.UIName = "Electrical (3 Duplex)";
                    config.PricingName = config.UIName;
                    config.RemoveExisting = true;
                    break;
                default:
                    config.UIName = "";
                    config.PricingName = "";
                    break;
            }
        }

        return config;
    }

    private async Task AddPricingComponent(GameObject obj, PricingConfiguration config)
    {
        // Remove an existing price component on the parent (for duplex outlet cases) if needed
        if (config.ShouldRemoveExistingPrice && config.OutletParent != null)
        {
            var existingPrice = config.OutletParent.GetComponentInChildren<SelectablePrice>();
            if (existingPrice != null)
            {
                Destroy(existingPrice);
            }
        }

        // Delegate all pricing setup to the PricingManager
        await PricingManager.Instance.AddPricingComponent(
            obj,
            config.IsBoomObject,
            config.PricingObjectName,
            config.UIButtonName,
            config.ExcelFileName,
            config.RootParentName,
            config.Price
        );
    }
    private void AddDuplexWatcher(GameObject obj, string uiButtonName)
    {
        var outletParent = obj.transform.parent?.parent?.gameObject;
        if (outletParent != null && outletParent.GetComponent<DuplexWatcher>() == null)
        {
            var watcher = outletParent.AddComponent<DuplexWatcher>();
            watcher.UIObjectName = uiButtonName;
        }
    }
    // Supporting data structures
    private class PricingConfiguration
    {
        public GameObject GameObject { get; set; }
        public string UIButtonName { get; set; }
        public string PricingObjectName { get; set; }
        public string ExcelFileName { get; set; }
        public bool IsBoomObject { get; set; }
        public bool ShouldAddPrice { get; set; }
        public bool RequiresDuplexWatcher { get; set; }
        public bool ShouldRemoveExistingPrice { get; set; }
        public GameObject OutletParent { get; set; }
        public string RootParentName { get; set; }
        public string Price { get; set; }
    }
    private class DuplexConfiguration
    {
        public string UIName { get; set; }
        public string PricingName { get; set; }
        public bool RemoveExisting { get; set; }
        public GameObject Parent { get; set; }
    }

    // Also update the AddSelectablePrice method to be more efficient:
    public void AddSelectablePrice(GameObject newSelectableGameObject, bool isBoomObject, string objectName, string uiBtnName, string excelName, string parent = null, string price = null,string size=null)
    {
        // Check if component already exists
        var existingPrice = newSelectableGameObject.GetComponent<SelectablePrice>();
        if (existingPrice != null)
        {
            Debug.LogWarning($"SelectablePrice already exists on {newSelectableGameObject.name}");
            return;
        }

        Selectable currentSelectable = newSelectableGameObject.GetComponent<Selectable>();
        if (currentSelectable == null)
        {
            Debug.LogError($"No Selectable component found on {newSelectableGameObject.name}");
            return;
        }

        SelectablePrice selectablePrice = newSelectableGameObject.AddComponent<SelectablePrice>();

        // Initialize all properties at once
        selectablePrice.Initialize(
            objectName: objectName,
            uiName: uiBtnName,
            excelName: excelName,
            parentName: parent,
            price: price,
            size
        );

        selectablePrice.isBoomObject = isBoomObject;
        selectablePrice.selectable = currentSelectable;

/*        // Let the component handle its own Excel data retrieval
        if (string.IsNullOrEmpty(price))
        {
            selectablePrice.GetPricingDataFromExcel(excelName);
        }*/
    }

    #endregion


    private void AddSavedRoomConfigs()
    {
        string configsFolder = ConfigurationManager.GetSavedConfigsFolder();
        if (Directory.Exists(configsFolder))
        {
            string[] files = Directory.GetFiles(configsFolder);
            foreach (string f in files.Where(x => x.EndsWith(".json")))
            {
                AddCustomMenuItem(f);
            }
        }

        AddCustomMenuItem(Application.streamingAssetsPath + "/Sample_Arm_Config.json");
    }

    public void AddCustomMenuItem(string f)
    {
        if (string.IsNullOrWhiteSpace(f) || ItemTemplate == null)
            return;

        // Avoid duplicate entries when overwriting an existing config.
        string configName = Path.GetFileName(f).Replace(".json", "").Replace("_", " ");
        for (int i = 0; i < ObjectMenuItems.Count; i++)
        {
            var existing = ObjectMenuItems[i];
            if (existing == null)
                continue;
            if (!string.IsNullOrEmpty(existing.CustomFile)
                && Path.GetFullPath(existing.CustomFile).Equals(Path.GetFullPath(f), StringComparison.OrdinalIgnoreCase))
                return;
            if (existing.GameObject != null)
            {
                var label = existing.GameObject.GetComponentInChildren<TMP_Text>(true);
                if (label != null && label.text == configName && !string.IsNullOrEmpty(existing.CustomFile))
                    return;
            }
        }

        ItemTemplate.SetActive(true);
        ItemTemplateTextObjectName.text = configName;
        GameObject newMenuItem = Instantiate(ItemTemplate, ItemTemplate.transform.parent);
        newMenuItem.GetComponentInChildren<Button>().onClick.AddListener(async () =>
        {
            gameObject.SetActive(false);
            GameObject newSelectable = await ConfigurationManager.Instance.LoadArmAssembly(f);
            if (newSelectable == null)
            {
                Debug.LogError("Something went wrong with LoadConfig!!");
                return;
            }

            Selectable selectable = newSelectable.GetComponent<Selectable>();
            selectable.StartRaycastPlacementMode();
        });

        ObjectMenuItems.Add(new ObjectMenuItem { GameObject = newMenuItem, CustomFile = f });
        ItemTemplate.SetActive(false);
    }

    private async void FilterMenuItems(AttachmentPoint attachmentPoint)
    {
        // Resolve the appropriate metadata for this attachment point in a robust way
        AttachmentPointMetaData apMeta = null;

        // Try to find any selectable data that references this attachment point GUID
        var selectableData = SelectableAssetBundles.GetSelectableData()
            .FirstOrDefault(x => x?.MetaData?.AttachmentPointGuidMetaData != null &&
                                 x.MetaData.AttachmentPointGuidMetaData.Any(y => y.Guid == attachmentPoint.MetaData.Guid));

        if (selectableData != null)
        {
            var task = Database.GetMetaData(
                selectableData.AssetBundleName,
                selectableData.MetaData);

            await task;

            if (!Application.isPlaying)
                return;

            if (task.Result.ResultType == Database.MetaDataOpertaionResultType.Success)
            {
                var metaData = task.Result.MetaData;

                var apData = metaData
                    .AttachmentPointGuidMetaData
                    .FirstOrDefault(x => x.Guid == attachmentPoint.MetaData.Guid)
                    ?? selectableData.MetaData
                        .AttachmentPointGuidMetaData
                        .FirstOrDefault(x => x.Guid == attachmentPoint.MetaData.Guid);

                if (apData != null)
                {
                    apMeta = apData.MetaData;
                }
            }
            else
            {
                Debug.LogWarning($"Could not fetch metadata for filtering: {task.Result.ErrorMessage}");
            }
        }

        // Fallback to the attachment point's own metadata if we couldn't resolve from DB/asset bundles
        if (apMeta == null)
        {
            apMeta = attachmentPoint.MetaData ?? new AttachmentPointMetaData
            {
                Guid = attachmentPoint.GUID,
                AllowedSelectableCategories = new List<string>(),
                AllowedSelectableAssetBundleNames = new List<string>()
            };
        }

        // Apply filtering using resolved metadata
        ObjectMenuItems.ForEach(item =>
        {
            if (item.SelectableData == null)
            {
                item.GameObject.SetActive(false);
                return;
            }

            if (SearchIsActive && !item.ValidForSearch)
            {
                item.GameObject.SetActive(false);
                return;
            }

            var compareMetaData = item.SelectableMetaData;
            var categories = compareMetaData?.Categories ?? new List<string>();

            // Category-based allow list
            foreach (var category in categories)
            {
                if (apMeta.AllowedSelectableCategories != null &&
                    apMeta.AllowedSelectableCategories.Contains(category))
                {
                    item.GameObject.SetActive(true);
                    return;
                }
            }

            // Explicit asset bundle allow list
            if (apMeta.AllowedSelectableAssetBundleNames != null &&
                apMeta.AllowedSelectableAssetBundleNames.Contains(item.SelectableData.AssetBundleName))
            {
                item.GameObject.SetActive(true);
                return;
            }

            item.GameObject.SetActive(false);
        });
    }

    private void FilterMenuItems(List<string> assetBundleNames)
    {
        ObjectMenuItems.ForEach(item =>
        {
            if (item.SelectableData == null ||
            assetBundleNames.Contains(item.SelectableData.AssetBundleName) ||
            (SearchIsActive && !item.ValidForSearch) ||
            !IsCategoryValid(item))
            {
                item.GameObject.SetActive(false);
                return;
            }

            item.GameObject.SetActive(true);
        });
    }

    private bool IsCategoryValid(ObjectMenuItem item)
    {
        if (!Categories.activeSelf ||
        _currentCategoryFilters.Count == 0)
            return true;

        if (!string.IsNullOrEmpty(item.CustomFile))
        {
            return _currentCategoryFilters.Contains("Save Data");
        }

        if (item.SelectableMetaData.Categories.Count == 0)
            return false;

        foreach (var category in item.SelectableMetaData.Categories)
        {
            if (_currentCategoryFilters.Contains(category))
                return true;
        }

        return false;
    }

    private void ClearMenuFilter()
    {
        if (!SearchIsActive)
        {
            ObjectMenuItems = ObjectMenuItems
            .OrderBy(x => x.GameObject.GetComponentInChildren<TextMeshProUGUI>().text)
            .ToList();
        }

        for (int i = 0; i < ObjectMenuItems.Count; i++)
        {
            var item = ObjectMenuItems[i];

            if (!SearchIsActive)
                item.GameObject.transform.SetSiblingIndex(i);

            if (SearchIsActive && !item.ValidForSearch)
            {
                item.GameObject.SetActive(false);
                continue;
            }

            if (item.SelectableData == null)
            {
                item.GameObject.SetActive
                    (SceneManager.GetActiveScene().name != "ObjectEditor" &&
                    IsCategoryValid(item));

                continue;
            }

            if (SceneManager.GetActiveScene().name == "ObjectEditor")
            {
                item.GameObject.SetActive(IsCategoryValid(item));
                continue;
            }

            item.GameObject.SetActive
                (item.SelectableData.MetaData.IsStandalone &&
                IsCategoryValid(item));
        }
    }

    public static void OpenToSelectCompatibleObjects(List<string> assetBundleNames)
    {
        _activeAssetBundleNames = assetBundleNames;
        _selectCompatibleObjectsMode = true;
        Instance.ResetAllCategoryToggles();
        Instance.Categories.SetActive(true);
        Instance.ClearSearchValidity();
        Instance.FilterMenuItems(assetBundleNames);
        Instance.UpdateCategoryVisibility(false);
        Instance.gameObject.SetActive(true);
    }

    public static async void Open()
    {
        while (!_initialized)
            await Task.Yield();

        if (!Application.isPlaying) return;
        Instance.Categories.SetActive(true);
        Instance.ClearSearchValidity();
        Instance.ClearMenuFilter();
        Instance._attachmentPoint = null;

        Instance.UpdateCategoryVisibility
            (SceneManager.GetActiveScene().name != "ObjectEditor");

        Instance.gameObject.SetActive(true);
    }

    public static async void Open(AttachmentPoint attachmentPoint)
    {
        while (!_initialized)
            await Task.Yield();

        if (!Application.isPlaying) return;

        Instance.ResetAllCategoryToggles();
        Instance.Categories.SetActive(false);
        Instance.ClearSearchValidity();
        Instance._attachmentPoint = attachmentPoint;
        Instance.FilterMenuItems(attachmentPoint);
        Instance.gameObject.SetActive(true);
    }
}