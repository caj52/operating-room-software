using UnityEngine;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Fills a TMP_Dropdown with data from an Excel sheet.
/// Selections persist per room (and stay in sync across the quote panel / proposal options UI).
/// Author: Faizan
/// </summary>
public class DropdownPopulator : MonoBehaviour
{
    const string PrefsPrefix = "SalesProposal.DropdownOption.";

    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private ExcelReader excelReader;
    [SerializeField] private int priceColumnNumber = 3;
    [SerializeField] private int minRowNumber;
    [SerializeField] private int maxRowNumber;
    [SerializeField] public bool isBoomExcelFileDropDown;

    private Dictionary<int, PriceExcelData> optionDataMap = new Dictionary<int, PriceExcelData>();

    public static List<DropdownPopulator> Instances { get; private set; } = new List<DropdownPopulator>();

    static bool _subscribedToRoomLoad;

    void Awake()
    {
        if (dropdown == null)
            dropdown = GetComponent<TMP_Dropdown>();

        if (excelReader == null)
            excelReader = FindAnyObjectByType<ExcelReader>();

        PopulateDropdown();
        dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
        Instances.Add(this);
        EnsureRoomLoadSubscription();
    }

    void OnDestroy()
    {
        Instances.Remove(this);
    }

    static void EnsureRoomLoadSubscription()
    {
        if (_subscribedToRoomLoad)
            return;
        _subscribedToRoomLoad = true;
        ConfigurationManager.OnRoomLoadComplete.AddListener(RestoreAllPersistedSelections);
    }

    private void PopulateDropdown()
    {
        if (dropdown == null || excelReader == null)
        {
            Debug.LogError("Dropdown or ExcelReader is not assigned.");
            return;
        }

        string sheetName = isBoomExcelFileDropDown
            ? DataFilePaths.sheetNameBoomIndividual
            : DataFilePaths.sheetNameLight;

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
            options.Add(columnData[i].ObjectName);
            optionDataMap.Add(i, columnData[i]);
        }

        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        RestorePersistedSelection();
    }

    private void OnDropdownValueChanged(int index)
    {
        PersistSelection(index);

        if (optionDataMap.TryGetValue(index, out PriceExcelData selectedData))
        {
            Debug.Log($"Selected Object: {selectedData.ObjectName}, Part Number: {selectedData.PartNumber}, Price: {selectedData.ListPrice}");
        }
    }

    /// <summary>Save the current dropdown value so it survives menu close / reopen / room reload.</summary>
    public void PersistCurrentSelection()
    {
        if (dropdown == null)
            return;
        PersistSelection(dropdown.value);
    }

    void PersistSelection(int index)
    {
        if (!optionDataMap.TryGetValue(index, out PriceExcelData selectedData) || selectedData == null)
            return;

        string key = PrefsKey();
        PlayerPrefs.SetString(key, selectedData.ObjectName ?? "");
        PlayerPrefs.SetString(key + ".Part", selectedData.PartNumber ?? "");
        PlayerPrefs.Save();
    }

    /// <summary>Re-apply the last saved choice for this dropdown (no notify / no price side-effects).</summary>
    public void RestorePersistedSelection()
    {
        if (dropdown == null || optionDataMap.Count == 0)
            return;

        string key = PrefsKey();
        string savedName = PlayerPrefs.GetString(key, "");
        string savedPart = PlayerPrefs.GetString(key + ".Part", "");
        if (string.IsNullOrEmpty(savedName) && string.IsNullOrEmpty(savedPart))
            return;

        int found = -1;
        foreach (var kvp in optionDataMap)
        {
            var data = kvp.Value;
            if (data == null)
                continue;
            if (!string.IsNullOrEmpty(savedPart)
                && !string.IsNullOrEmpty(data.PartNumber)
                && string.Equals(data.PartNumber, savedPart, System.StringComparison.OrdinalIgnoreCase))
            {
                found = kvp.Key;
                break;
            }
        }

        if (found < 0 && !string.IsNullOrEmpty(savedName))
        {
            foreach (var kvp in optionDataMap)
            {
                var data = kvp.Value;
                if (data == null)
                    continue;
                if (string.Equals(data.ObjectName, savedName, System.StringComparison.OrdinalIgnoreCase))
                {
                    found = kvp.Key;
                    break;
                }
            }
        }

        if (found < 0 || found >= dropdown.options.Count)
            return;

        dropdown.SetValueWithoutNotify(found);
        dropdown.RefreshShownValue();
    }

    public static void RestoreAllPersistedSelections()
    {
        Instances.RemoveAll(instance => instance == null);
        foreach (var instance in Instances)
            instance.RestorePersistedSelection();
    }

    public static void PersistAllCurrentSelections()
    {
        Instances.RemoveAll(instance => instance == null);
        foreach (var instance in Instances)
            instance.PersistCurrentSelection();
    }

    string PrefsKey()
    {
        string room = "Default";
        try
        {
            if (ExportPaths.HasSavedRoomName())
                room = ExportPaths.GetRoomExportName();
        }
        catch
        {
            // ExportPaths / ConfigurationManager may be unavailable during early Awake.
        }

        string side = isBoomExcelFileDropDown ? "Boom" : "Light";
        string id = gameObject != null ? gameObject.name : "Unknown";
        // Include row range so two dropdowns with similar names stay distinct.
        return $"{PrefsPrefix}{room}.{side}.{id}.{minRowNumber}-{maxRowNumber}";
    }

    public TMP_Dropdown Dropdown => dropdown;

    /// <summary>Label for the currently selected option, or empty if none.</summary>
    public string GetCurrentSelectionText()
    {
        if (dropdown == null || dropdown.options == null || dropdown.options.Count == 0)
            return "";

        int index = Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1);
        if (optionDataMap.TryGetValue(index, out PriceExcelData data)
            && data != null
            && !string.IsNullOrWhiteSpace(data.ObjectName))
            return data.ObjectName;

        var opt = dropdown.options[index];
        return opt != null ? (opt.text ?? "") : "";
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
