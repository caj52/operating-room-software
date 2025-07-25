using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PopulateUIWithPricingItems : MonoBehaviour
{
    public GameObject parentObject;
    public PricingRowDataFill rowPrefab;
    public List<SelectablePrice> boomObjects = new();
    public List<SelectablePrice> lightObjects = new();
    public List<SelectablePrice> otherObjects = new();

    private readonly HashSet<int> processedLightIds = new();
    private readonly HashSet<int> processedBoomIds = new();

    private void OnEnable()
    {
        if (PricingManager.Instance == null) return;

        // Subscribe to PricingManager events
        var manager = PricingManager.Instance;
        manager.OnPriceAdded.AddListener(OnPriceAddedHandler);
        manager.OnPriceRemoved.AddListener(OnPriceRemovedHandler);
        manager.OnPriceUpdated.AddListener(OnPriceUpdatedHandler);

        RebuildListsFromActivePrices();
    }

    private void OnDisable()
    {
        if (PricingManager.Instance == null) return;

        var manager = PricingManager.Instance;
        manager.OnPriceAdded.RemoveListener(OnPriceAddedHandler);
        manager.OnPriceRemoved.RemoveListener(OnPriceRemovedHandler);
        manager.OnPriceUpdated.RemoveListener(OnPriceUpdatedHandler);
    }

    private void RebuildListsFromActivePrices()
    {
        ClearLists();

        foreach (var sp in PricingManager.Instance.GetAllPrices())
        {
            if (sp?.sheetName == null) continue;

            AddToAppropriateList(sp);
        }

        ResolveAndLogLightPricing();
    }

    private void ClearLists()
    {
        lightObjects.Clear();
        boomObjects.Clear();
        otherObjects.Clear();
        processedLightIds.Clear();
        processedBoomIds.Clear();
    }

    private void AddToAppropriateList(SelectablePrice sp)
    {
        int id = sp.GetInstanceID();

        switch (sp.sheetName)
        {
            case var sheet when sheet == DataFilePaths.sheetNameLight:
                if (processedLightIds.Add(id))
                    lightObjects.Add(sp);
                break;
            case var sheet when sheet == DataFilePaths.sheetNameBoomIndividual:
                if (processedBoomIds.Add(id))
                    boomObjects.Add(sp);
                break;
            default:
                if (!otherObjects.Contains(sp))
                    otherObjects.Add(sp);
                break;
        }
    }

    // Event handlers
    private void OnPriceAddedHandler(SelectablePrice sp) => SetDataIntoList(sp);

    private void OnPriceRemovedHandler(SelectablePrice sp)
    {
        sp?.UIRefPricingRowDataFill?.DestroyRow();
        CleanSelectableObject(sp);
    }

    private void OnPriceUpdatedHandler(SelectablePrice sp) => ResolveAndLogLightPricing();

    public void SetDataIntoList(SelectablePrice selectablePrice = null, GameObject destroyingObject = null)
    {
        CleanNullEntries();

        if (selectablePrice != null)
        {
            AddToAppropriateList(selectablePrice);
            Debug.Log($"Added pricing item to list: {selectablePrice.UIObjectName}");
        }

        if (destroyingObject != null)
            Destroy(destroyingObject);

        ResolveAndLogLightPricing();
    }

    private void CleanNullEntries()
    {
        if (boomObjects.RemoveAll(x => x == null) > 0) { }
        if (lightObjects.RemoveAll(x => x == null) > 0) { }
        if (otherObjects.RemoveAll(x => x == null) > 0) { }
    }

    public void ResolveAndLogLightPricing()
    {
        ClearContent();

        ProcessLightGroups();
        ProcessBoomObjects();
    }

    private void ProcessLightGroups()
    {
        var lightGroups = lightObjects
            .Where(obj => obj?.gameObject != null)
            .GroupBy(obj => GetRootParent(obj.transform))
            .ToList();

        foreach (var group in lightGroups)
        {
            ProcessLightGroup(group.ToList(), DataFilePaths.sheetNameLight);
        }
    }

    private void ProcessLightGroup(List<SelectablePrice> lightGroupList, string sheetName)
    {
        int i = 0;
        while (i < lightGroupList.Count)
        {
            var validLights = GetValidLightsAtIndex(lightGroupList, i, 3);
            if (validLights.Count == 0)
            {
                i++;
                continue;
            }

            int processed = TryProcessLightCombo(validLights, lightGroupList, i, sheetName);
            i += processed > 0 ? processed : 1;
        }
    }

    private int TryProcessLightCombo(List<string> validLights, List<SelectablePrice> lightGroupList, int startIndex, string sheetName)
    {
        // Try 3-light combo
        if (validLights.Count == 3)
        {
            string combo3Key = string.Join(", ", validLights);
            var data3 = PricingManager.Instance.GetCachedPricingData(sheetName, combo3Key);
            if (data3 != null)
            {
                AddComboRowToUI(data3, 3, lightGroupList.Skip(startIndex).Take(3).ToList());
                return 3;
            }
        }

        // Try 2-light combo
        if (validLights.Count >= 2)
        {
            string combo2Key = string.Join(", ", validLights.Take(2));
            var data2 = PricingManager.Instance.GetCachedPricingData(sheetName, combo2Key);
            if (data2 != null)
            {
                AddComboRowToUI(data2, 2, lightGroupList.Skip(startIndex).Take(2).ToList());
                return 2;
            }
        }

        // Single light
        var data1 = PricingManager.Instance.GetCachedPricingData(sheetName, validLights[0]);
        if (data1 != null)
        {
            AddComboRowToUI(data1, 1, new List<SelectablePrice> { lightGroupList[startIndex] });
        }

        return 1;
    }

    private void ProcessBoomObjects()
    {
        foreach (var boomObj in boomObjects.Where(obj => obj != null))
        {
            string key = boomObj.UIObjectName?.Trim();
            if (string.IsNullOrEmpty(key)) continue;

            var data = PricingManager.Instance.GetCachedPricingData(DataFilePaths.sheetNameBoomIndividual, key, boomObj.Size);
            if (data != null)
            {
                AddComboRowToUI(data, 1, new List<SelectablePrice> { boomObj });
            }
        }
    }

    private List<string> GetValidLightsAtIndex(List<SelectablePrice> lights, int startIndex, int maxCount)
    {
        var result = new List<string>();
        int endIndex = Mathf.Min(startIndex + maxCount, lights.Count);

        for (int j = startIndex; j < endIndex; j++)
        {
            string uiName = lights[j].pricingObjectName?.Trim();
            if (string.IsNullOrEmpty(uiName)) break;
            result.Add(uiName);
        }

        return result;
    }

    private void AddComboRowToUI(PriceExcelData data, int quantity, List<SelectablePrice> linkedObjects)
    {
        var row = Instantiate(rowPrefab, transform);
        row.Initialize(data, quantity, linkedObjects.FirstOrDefault());
    }

    private void ClearContent()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name != "Heading")
                Destroy(child.gameObject);
        }
    }

    public void CleanSelectableObject(SelectablePrice sp)
    {
        if (sp == null) return;

        int instanceId = sp.GetInstanceID();

        lightObjects.Remove(sp);
        boomObjects.Remove(sp);
        otherObjects.Remove(sp);
        processedLightIds.Remove(instanceId);
        processedBoomIds.Remove(instanceId);

        SetDataIntoList(null);
    }

    private Transform GetRootParent(Transform t)
    {
        while (t.parent != null)
            t = t.parent;
        return t;
    }
}