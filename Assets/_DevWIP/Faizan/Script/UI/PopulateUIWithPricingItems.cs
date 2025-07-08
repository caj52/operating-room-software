using System;
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

    public Action<SelectablePrice, GameObject> OnSetPriceData;
    public Action<SelectablePrice> OnClearSp; //Clearing from SelectablePrice.cs

    private Dictionary<string, PriceExcelData> pricingCache = new();

    private void OnEnable()
    {
        OnSetPriceData += SetDataIntoList;
        OnClearSp += CleanSelectableObject;
        ResolveAndLogLightPricing();
    }

    private void OnDisable()
    {
       // ClearContent();
    }
    private void Start()
    {
        parentObject.SetActive(false);
    }

    //public PricingRowDataFill GenerateUIRow(SelectablePrice selectablePrice)
    //{
    //    SetDataIntoList(selectablePrice);

    //    //PricingRowDataFill pricingRowDataFill = Instantiate(rowPrefab, transform);
    //    //pricingRowDataFill.FillData(selectablePrice);
    //    Debug.Log("Anas Adding Excel Data In Quotation Panel 1");


    //    //selectablePrice.OnDestroyed += pricingRowDataFill.DestroyRow;

    //    // Store the event subscription
    //    Action onDestroyedAction = pricingRowDataFill.DestroyRow;
    //    selectablePrice.OnDestroyed += onDestroyedAction;

    //    // Unsubscribe from the event when the UI row is destroyed
    //    pricingRowDataFill.OnDestroyEvent += () => selectablePrice.OnDestroyed -= onDestroyedAction;
    //    ResolveAndLogLightPricing();
    //    return pricingRowDataFill;
    //}

    public void SetDataIntoList(SelectablePrice selectablePrice = null,GameObject destroyingObject = null)
    {

        boomObjects =  CleanListWithNullVaules(boomObjects);
        lightObjects = CleanListWithNullVaules(lightObjects);
        otherObjects = CleanListWithNullVaules(otherObjects);

        Debug.Log(" LightObjects count after cleanup: " + lightObjects.Count);        

        if (selectablePrice != null)
        {
            switch (selectablePrice.sheetName)
            {
                case "2025_03_28 3D Light Pricing":
                    bool isLightObjectDuplicated = lightObjects.Any(x => x.GetInstanceID() == selectablePrice.GetInstanceID());
                    if (isLightObjectDuplicated)
                        return;

                    lightObjects.Add(selectablePrice);

                    break;

                case "2025_03_28 Boom Pricing":
                    bool isBoomObjectDuplicated = boomObjects.Any(x => x.GetInstanceID() == selectablePrice.GetInstanceID());
                    if (isBoomObjectDuplicated)
                        return;
                    Debug.Log("Checking Boom Added From");

                    boomObjects.Add(selectablePrice);
                    break;

                default:
                    break;
            }

            Debug.Log("Anas => Added to list");
        }
        if (destroyingObject != null)
        {
            Destroy(destroyingObject);
        }
        
        ResolveAndLogLightPricing();
    }


    public void ResolveAndLogLightPricing()
    {
        ClearContent();
        //ExcelReader reader = FindAnyObjectByType<ExcelReader>();
        //if (reader == null)
        //{
        //    Debug.LogError("ExcelReader not found in scene!");
        //    return;
        //}

        string lightsSheetName = "2025_03_28 3D Light Pricing";
        string boomSheetName = "2025_03_28 Boom Pricing";
        
        var lightGroups = lightObjects
        .GroupBy(obj => GetRootParent(obj.transform))
        .ToList();

        Debug.Log("Anas lightGroups => " + lightGroups.Count);



        foreach (var group in lightGroups)
        {
            var lightGroupList = group.ToList();
            int i = 0;

            while (i < lightGroupList.Count)
            {               
                string ui1 = lightGroupList[i].UIObjectName?.Trim();
                string ui2 = (i + 1 < lightGroupList.Count) ? lightGroupList[i + 1].UIObjectName?.Trim() : null;
                string ui3 = (i + 2 < lightGroupList.Count) ? lightGroupList[i + 2].UIObjectName?.Trim() : null;

                Debug.Log("Anas => ui1 " + ui1);
                Debug.Log("Anas => ui2 " + ui2);
                Debug.Log("Anas => ui3 " + ui3);

                bool has2 = (i + 1 < lightGroupList.Count);
                bool has3 = (i + 2 < lightGroupList.Count);                

                if (has3 && !string.IsNullOrEmpty(ui1) && !string.IsNullOrEmpty(ui2) && !string.IsNullOrEmpty(ui3))
                {
                    string combo3 = string.Join(", ", ui1, ui2, ui3);
                    //var data3 = reader.FetchPricingDataFromExcel(lightsSheetName, combo3);
                    var data3 = GetCachedPricingData(lightsSheetName, combo3);

                    if (data3 != null)
                    {
                        AddComboRowToUI(data3, 3, new List<SelectablePrice> { lightGroupList[i], lightGroupList[i + 1], lightGroupList[i + 2] });
                        i += 3;
                        Debug.Log("ANas = > Combo of 3 " + data3);
                        continue;
                    }

                }

                if (has2 && !string.IsNullOrEmpty(ui1) && !string.IsNullOrEmpty(ui2))
                {
                    string combo2 = string.Join(", ", ui1, ui2);
                    // var data2 = reader.FetchPricingDataFromExcel(lightsSheetName, combo2);
                    var data2 = GetCachedPricingData(lightsSheetName, combo2);
                    if (data2 != null)
                    {
                        AddComboRowToUI(data2, 2, new List<SelectablePrice> { lightGroupList[i], lightGroupList[i + 1] });
                        i += 2;
                        Debug.Log("ANas = > Combo of 2 " + data2);

                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(ui1))
                {   
                    var data1 = GetCachedPricingData(lightsSheetName, ui1);
                    if (data1 != null)
                    {
                        AddComboRowToUI(data1, 1, new List<SelectablePrice> { lightGroupList[i] });
                        Debug.Log("ANas = > Combo of 1 " + data1);
                    }
                }

                i++;
            }
        }

        int boomIndex = 0;
       
        while (boomIndex < boomObjects.Count)
        {

            string ui1 = boomObjects[boomIndex].UIObjectName?.Trim();
            if (!string.IsNullOrEmpty(ui1))
            {
                //var data1 = reader.FetchPricingDataFromExcel(boomSheetName, ui1, boomObjects[boomIndex].size);
                var data1 = GetCachedPricingData(boomSheetName, ui1, boomObjects[boomIndex].size);


                Debug.Log("Anas => " + ui1);
                if (data1 != null)
                {
                    AddComboRowToUI(data1, 1, new List<SelectablePrice> { boomObjects[boomIndex] });
                }
            }

            boomIndex++;
        }

    }

    private void AddComboRowToUI(PriceExcelData data, int quantity,List<SelectablePrice> linkedObjects = null)
    {
        PricingRowDataFill row = Instantiate(rowPrefab, transform);

        row.FillData(data, quantity, linkedObjects.FirstOrDefault());



        //foreach (var sp in linkedObjects)
        //{
        //    if (sp == null) continue;

        //    sp.OnDestroyed += row.DestroyRow;
        //    Debug.Log("Anas => sp Destroy Event", sp.gameObject);

        //    row.OnDestroyEvent += () => sp.OnDestroyed -= row.DestroyRow;
        //}
    }

    private void ClearContent() 
    {
        foreach (Transform child in transform)
        {
            if (child.name == "Heading")
            {
                continue;
            }
            Debug.Log("Anas Clearig Row " + child.name);
            Destroy(child.gameObject);
        }
    }
    private List<SelectablePrice> CleanListWithNullVaules(List<SelectablePrice> list)
    {
        return list
            .Where(x => x != null && !ReferenceEquals(x, null) && x.gameObject != null)
            .ToList();
    }

    public void CleanSelectableObject(SelectablePrice sp) 
    {
        lightObjects.Remove(sp);
        boomObjects.Remove(sp);
        SetDataIntoList(null);

    }

    private Transform GetRootParent(Transform t)
    {
        while (t.parent != null)
        {
            t = t.parent;
        }
        Debug.Log("Anas => " + t , t.gameObject);
        return t;
    }

    private PriceExcelData GetCachedPricingData(string sheetName, string key, string size = "")
    {
        string cacheKey = $"{sheetName}::{key}::{size}";

        if (pricingCache.TryGetValue(cacheKey, out var cachedData))
        {   
            Debug.Log("Anas => Using cached data for: " + cacheKey);
            return cachedData;
        }

        ExcelReader reader = FindAnyObjectByType<ExcelReader>();
        if (reader == null)
        {
            Debug.LogError("ExcelReader not found in scene!");
            return null;
        }

        PriceExcelData data;

        if (!string.IsNullOrEmpty(size))
            data = reader.FetchPricingDataFromExcel(sheetName, key, size);
        else
            data = reader.FetchPricingDataFromExcel(sheetName, key);

        pricingCache[cacheKey] = data;

        return data;
    }


}
