using UnityEngine;
using System.IO;
using NPOI.SS.UserModel;
using System.Globalization;
using System;
using System.Collections.Generic;
using NPOI.HSSF.UserModel;

/// <summary>
/// This is generic class to read excel specific column and row value. It is based NPOI library.
/// </summary>
public class ExcelReader : MonoBehaviour
{
    private string filePath;
    private IWorkbook _workbook;
    private readonly object _workbookLock = new object();
    private double? _cachedSimFlexPrice;
    private DateTime _workbookFileWriteTimeUtc;

    private void Start()
    {
        SetExcelFileName();
    }

    public void SetExcelFileName()
    {
        // Live prices come from Quote Request V6 via PricingBridgeStore — not an .xls in quotes/.
        filePath = null;
        InvalidateWorkbookCache();
    }

    private void OnDestroy()
    {
        InvalidateWorkbookCache();
    }

    public void InvalidateWorkbookCache()
    {
        lock (_workbookLock)
        {
            _workbook?.Close();
            _workbook = null;
            _cachedSimFlexPrice = null;
        }
    }

    private bool ValidateFilePath()
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return false;
        return true;
    }

    /// <summary>
    /// Load the pricing workbook once and reuse it. Opening HSSFWorkbook per lookup
    /// made room-load pricing rebuild take minutes.
    /// </summary>
    private IWorkbook GetWorkbook()
    {
        if (!ValidateFilePath())
            return null;

        DateTime writeUtc = File.GetLastWriteTimeUtc(filePath);
        if (_workbook != null && writeUtc != _workbookFileWriteTimeUtc)
            InvalidateWorkbookCache();

        if (_workbook != null)
            return _workbook;

        lock (_workbookLock)
        {
            if (_workbook != null)
                return _workbook;

            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // HSSFWorkbook copies into memory; safe to close the stream afterward.
                _workbook = OpenWorkbook(stream, filePath);
                if (_workbook == null)
                    return null;
            }

            _workbookFileWriteTimeUtc = writeUtc;
            PricingManager.Instance?.ClearCache();
            return _workbook;
        }
    }

    private ISheet GetSheet(string sheetName)
    {
        IWorkbook workbook = GetWorkbook();
        if (workbook == null)
            return null;

        ISheet sheet = workbook.GetSheet(sheetName);
        if (sheet == null)
            Debug.LogError($"Sheet not found in the excel file: {sheetName}");
        return sheet;
    }

    public PriceExcelData FetchPricingDataFromExcel(string sheetName, string objectNameToSearch, string objectSizeToSearch = null)
    {
        if (PricingBridgeStore.TryGetPriceExcelData(sheetName, objectNameToSearch, objectSizeToSearch, out var data)
            && data != null)
        {
            data.SimFlexPrice = GetSimFlexPrice();
            return data;
        }
        return null;
    }

    public PriceExcelData[] GetColumnData(int priceColumnNumber, int minRowNumber, int maxRowNumber, string sheetName)
    {
        bool boom = (sheetName ?? "").IndexOf("Boom", StringComparison.OrdinalIgnoreCase) >= 0;
        return PricingBridgeStore.ListFamily(boom ? "Boom" : "Light");
    }

    static IWorkbook OpenWorkbook(Stream stream, string path)
    {
        string ext = Path.GetExtension(path)?.ToLowerInvariant();
        if (ext == ".xlsx" || ext == ".xlsm")
        {
            Debug.LogError(
                $"Pricing workbook is {ext}, but this build's Excel reader only opens .xls (Excel 97-2003). " +
                $"Save/export the sheet as .xls, or replace StreamingAssets with an .xls copy. Path: {path}");
            return null;
        }

        return new HSSFWorkbook(stream);
    }
    
    /// <summary>
    /// This is generic function to read excel specific column and row value.
    /// </summary>
    /// <param name="rowNumber"></param>
    /// <param name="columnNumber"></param>
    /// <returns></returns>
    public string GetCellValue(int rowNumber, int columnNumber, string sheetName)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError("Excel file not found!");
            return null;
        }

        using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            IWorkbook workbook = OpenWorkbook(stream, filePath);
            if (workbook == null)
                return null;

            ISheet sheet = workbook.GetSheet(sheetName);
            if (sheet == null)
                return null;

            if (rowNumber < 0 || rowNumber > sheet.LastRowNum)
            {
                Debug.LogError("Invalid row number!");
                return null;
            }

            IRow excelRow = sheet.GetRow(rowNumber);
            if (excelRow == null)
            {
                Debug.LogError("Row not found!");
                return null;
            }

            if (columnNumber < 0 || columnNumber >= excelRow.LastCellNum)
            {
                Debug.LogError("Invalid column number!");
                return null;
            }

            ICell cell = excelRow.GetCell(columnNumber);
            return GetCellValue(cell);
        }
    }

    //private double GetSimFlexPrice()
    //{
    //    string sheetName = DataFilePaths.sheetNameLight;
    //    //This is hard coded value from the excel, change it if the things get chagnd in the excel. 
    //    string cellValue = GetCellValue(59, 3, sheetName);// GetCellValue(33, 3);
      
    //    Debug.Log("cellValue: " + cellValue);
    //    CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
    //    double listPriceValue = double.Parse(cellValue, NumberStyles.Currency, culture);


    //    return listPriceValue;
    //    //114-001111	Sim.Flex Custom Arm Option	Assign when sim.flex option is selected	$5,358.73	$5,358.73

    //}

    private double GetSimFlexPrice()
    {
        if (_cachedSimFlexPrice.HasValue)
            return _cachedSimFlexPrice.Value;

        var data = PricingBridgeStore.FindNamed("Light", "SimFlex")
                   ?? PricingBridgeStore.FindNamed("Light", "sim.flex")
                   ?? PricingBridgeStore.FindNamed("Light", "Horizontal Arm");
        _cachedSimFlexPrice = data?.ListPrice ?? 0.0;
        return _cachedSimFlexPrice.Value;
    }

    public PriceExcelData GetInstallationLightsCharges()
    {
        if (PricingBridgeStore.TryGetPriceExcelData(
                DataFilePaths.sheetNameLight, "Installation (Lights)", null, out var data))
            return data;
        return PricingBridgeStore.FindNamed("Install", "Install")
               ?? PricingBridgeStore.FindNamed("Install", "Valia");
    }
    public PriceExcelData GetShippingLightsCharges()
    {
        if (PricingBridgeStore.TryGetPriceExcelData(
                DataFilePaths.sheetNameLight, "Shipping (Lights)", null, out var data))
            return data;
        return PricingBridgeStore.FindNamed("Install", "Shipping");
    }

    static PriceExcelData ApplyBridgeOverride(PriceExcelData data, string sheetName)
    {
        if (data == null) return null;
        PricingBridgeStore.ApplyLivePrice(sheetName, data.ObjectName, data.ObjectSize, data);
        return data;
    }

    private double ParseCurrency(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0.0;

        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        return double.Parse(value, NumberStyles.Currency, culture);
    }

    private PriceExcelData GetRowData(int rowIndex, string sheetName)
    {
        IRow row = GetRow(rowIndex, sheetName);
        if (row == null) return null;

        int nameCol = 1;
        int priceCol = 3;
        int partCol = 0;
        int sizeCol = -1;
        if (DataFilePaths.SheetColumnMappings.TryGetValue(sheetName, out var cols))
        {
            nameCol = cols.ObjectName;
            priceCol = cols.ListPrice;
            partCol = cols.PartNumber;
            sizeCol = cols.ObjectSize;
        }

        string objectName = GetCellValue(row.GetCell(nameCol));
        // Light sheet install/ship rows put the label in Configuration (col 1), not Object Name.
        if (string.IsNullOrWhiteSpace(objectName) && nameCol != 1)
            objectName = GetCellValue(row.GetCell(1));

        var data = new PriceExcelData
        {
            PartNumber = GetCellValue(row.GetCell(partCol)),
            ObjectName = objectName,
            ObjectSize = sizeCol >= 0 ? GetCellValue(row.GetCell(sizeCol)) : null,
            ListPrice = ParseCurrency(GetCellValue(row.GetCell(priceCol))),
        };
        return ApplyBridgeOverride(data, sheetName);
    }

    private IRow GetRow(int rowIndex, string sheetName)
    {
        ISheet sheet = GetSheet(sheetName);
        if (sheet == null || rowIndex < 0 || rowIndex > sheet.LastRowNum)
            return null;

        lock (_workbookLock)
            return sheet.GetRow(rowIndex);
    }

    /// <summary>
    /// Retrieves the value of a cell, handling all possible cell types (numeric, string, formula, etc.).
    /// </summary>
    /// <param name="cell">The cell to retrieve the value from.</param>
    /// <returns>The value of the cell as a string, or null if the cell is empty or unsupported.</returns>
    private string GetCellValue(ICell cell)
    {
        if (cell == null)
            return null;

        switch (cell.CellType)
        {
            case CellType.Numeric:
                // Check if the numeric value is a date
                if (DateUtil.IsCellDateFormatted(cell))
                {
                    return cell.DateCellValue.ToString(CultureInfo.InvariantCulture);
                }
                // Restrict numeric value to two decimal places
                return cell.NumericCellValue.ToString("F2", CultureInfo.InvariantCulture);

            case CellType.String:
                return cell.StringCellValue;

            case CellType.Boolean:
                return cell.BooleanCellValue.ToString();

            case CellType.Formula:
                // Get the cached formula result
                if (cell.CachedFormulaResultType == CellType.Numeric)
                {
                    // Restrict numeric value to two decimal places
                    return cell.NumericCellValue.ToString("F2", CultureInfo.InvariantCulture);
                }
                return cell.StringCellValue;

            case CellType.Blank:
                return null;

            default:
                Debug.LogWarning($"Unsupported cell type: {cell.CellType}");
                return null;
        }
    }

    static bool NamesEqual(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        return string.Compare(
            a.Trim(),
            b.Trim(),
            CultureInfo.CurrentCulture,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols) == 0;
    }


    /*
    public PriceExcelData GetInstallationLightsCharges()
    {
        string sheetName = DataFilePaths.sheetNameLight;
        string price = GetCellValue(98, 3, sheetName);
        string title = GetCellValue(98, 1, sheetName);
        PriceExcelData priceExcelData = new PriceExcelData();
        priceExcelData.PartNumber = GetCellValue(98, 0, sheetName);
        priceExcelData.ObjectName = GetCellValue(98, 1, sheetName);

        // Debug.Log("Installation Lights Charges: " + price);
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        double listPriceValue = double.Parse(price, NumberStyles.Currency, culture);
        priceExcelData.ListPrice = listPriceValue;
        return priceExcelData;
    }
    public PriceExcelData GetShippingLightsCharges()
    {
        string sheetName = DataFilePaths.sheetNameLight;
        
        string price = GetCellValue(99, 3, sheetName);
        // Debug.Log("Shipping Light Charges: " + price);
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        
        double listPriceValue = double.Parse(price, NumberStyles.Currency, culture);

        string title = GetCellValue(99, 1, sheetName);
        PriceExcelData priceExcelData = new PriceExcelData();
        priceExcelData.PartNumber = GetCellValue(99, 0, sheetName);
        priceExcelData.ObjectName = GetCellValue(99, 1, sheetName);

        
        priceExcelData.ListPrice = listPriceValue;

        return priceExcelData;
    }
*/

    public List<Dictionary<string, string>> GetRawSheetData(string sheetName)
    {
        List<Dictionary<string, string>> rows = new();

        using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            IWorkbook workbook = OpenWorkbook(stream, filePath);
            if (workbook == null) return rows;

            ISheet sheet = workbook.GetSheet(sheetName);
            if (sheet == null) return rows;

            IRow headerRow = sheet.GetRow(0);
            List<string> headers = new();
            for (int i = 0; i < headerRow.LastCellNum; i++)
            {
                headers.Add(headerRow.GetCell(i)?.ToString().Trim());
            }

            for (int r = 1; r <= sheet.LastRowNum; r++)
            {
                IRow row = sheet.GetRow(r);
                if (row == null) continue;

                var dict = new Dictionary<string, string>();
                for (int c = 0; c < headers.Count; c++)
                {
                    dict[headers[c]] = row.GetCell(c)?.ToString();
                }

                rows.Add(dict);
            }
        }

        return rows;
    }
}
