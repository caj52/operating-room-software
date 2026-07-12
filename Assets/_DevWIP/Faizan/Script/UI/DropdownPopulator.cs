using UnityEngine;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Fills a TMP_Dropdown with data from an Excel sheet.
/// Author: Faizan
/// </summary>
public class DropdownPopulator : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private ExcelReader excelReader;
    [SerializeField] private int priceColumnNumber = 3;
    [SerializeField] private int minRowNumber;
    [SerializeField] private int maxRowNumber;
    [SerializeField] public bool isBoomExcelFileDropDown;

    private Dictionary<int, PriceExcelData> optionDataMap = new Dictionary<int, PriceExcelData>();

    public static List<DropdownPopulator> Instances { get; private set; } = new List<DropdownPopulator>();

    void Awake()
    {
        if (dropdown == null)
            dropdown = GetComponent<TMP_Dropdown>();

        if (excelReader == null)
            excelReader = FindAnyObjectByType<ExcelReader>();

        PopulateDropdown();
        dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
        // Add this instance to the list
        Instances.Add(this);
    }

    void OnDestroy()
    {
        Instances.Remove(this);
    }

    private void PopulateDropdown()
    {
        if (dropdown == null || excelReader == null)
        {
            Debug.LogError("Dropdown or ExcelReader is not assigned.");
            return;
        }
        //Debug.LogError("Dropdown populated from excel.");
        string sheetName;

        if (isBoomExcelFileDropDown)
        {
         sheetName = DataFilePaths.sheetNameBoomIndividual;
        }
        else
        {
            sheetName = DataFilePaths.sheetNameLight;
        }
        excelReader.SetExcelFileName();
        PriceExcelData[] columnData = excelReader.GetColumnData(priceColumnNumber, minRowNumber, maxRowNumber, sheetName);

        if (columnData == null || columnData.Length == 0)
        {
            Debug.LogError("No data found in the specified column and row range.");
            return;
        }

        List<string> options = new List<string>();
        optionDataMap.Clear();
        for (int i = 0; i < columnData.Length; i++)
        {
            Debug.Log("column data" + columnData[i].ObjectName);
            options.Add(columnData[i].ObjectName);
            optionDataMap.Add(i, columnData[i]);
        }

        dropdown.ClearOptions();
        dropdown.AddOptions(options);
    }

    private void OnDropdownValueChanged(int index)
    {
        if (optionDataMap.TryGetValue(index, out PriceExcelData selectedData))
        {
            Debug.Log($"Selected Object: {selectedData.ObjectName}, Part Number: {selectedData.PartNumber}, Price: {selectedData.ListPrice}");
            // You can now use selectedData to access other values
        }
    }

    public static List<(DropdownPopulator, PriceExcelData)> GetAllCurrentStates()
    {
        List<(DropdownPopulator, PriceExcelData)> currentStates = new List<(DropdownPopulator, PriceExcelData)>();

        // Prune destroyed instances so stale refs don't break export / preview.
        Instances.RemoveAll(instance => instance == null);

        foreach (var instance in Instances)
        {
            if (instance.dropdown == null)
                continue;
            int currentIndex = instance.dropdown.value;
            if (instance.optionDataMap.TryGetValue(currentIndex, out PriceExcelData selectedData))
            {
                currentStates.Add((instance, selectedData));
            }
        }

        return currentStates;
    }
}
