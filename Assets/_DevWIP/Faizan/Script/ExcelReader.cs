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
        string fileName = DataFilePaths.ExcelFileNameForLightAndBoomPricing;
        string excelBasePath = Path.Combine(Application.streamingAssetsPath, "Data", "quotes");

        string nextPath = Path.Combine(excelBasePath, fileName);
        if (!string.Equals(filePath, nextPath, StringComparison.OrdinalIgnoreCase))
        {
            filePath = nextPath;
            InvalidateWorkbookCache();
        }

        if (!File.Exists(filePath))
            Debug.LogWarning($"The specified Excel file does not exist at path: {filePath}");
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
        if (string.IsNullOrEmpty(filePath))
            SetExcelFileName();

        if (!File.Exists(filePath))
        {
            Debug.LogError($"Excel file not found at path: {filePath}");
            return false;
        }
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
        if (!DataFilePaths.SheetColumnMappings.TryGetValue(sheetName, out var columnMapping))
        {
            Debug.LogError($"No column mapping found for sheet: {sheetName}");
            return null;
        }

        ISheet sheet = GetSheet(sheetName);
        if (sheet == null)
            return null;

        lock (_workbookLock)
        {
            for (int row = 0; row <= sheet.LastRowNum; row++)
            {
                IRow excelRow = sheet.GetRow(row);
                if (excelRow == null) continue;

                string excelObjectName = GetCellValue(excelRow.GetCell(columnMapping.ObjectName));
                // Light option rows (ceiling covers, tandem mounts) store the name in
                // column 1 (Configuration) with column 2 (3D combo) empty.
                string excelConfigName = columnMapping.ObjectName != 1
                    ? GetCellValue(excelRow.GetCell(1))
                    : null;
                string excelPartNumber = GetCellValue(excelRow.GetCell(columnMapping.PartNumber));
                string excelObjectSize = columnMapping.ObjectSize != -1
                    ? GetCellValue(excelRow.GetCell(columnMapping.ObjectSize))
                    : "";

                bool isMatchedName =
                    NamesEqual(excelObjectName, objectNameToSearch)
                    || NamesEqual(excelConfigName, objectNameToSearch)
                    || NamesEqual(excelPartNumber, objectNameToSearch);

                bool isMatchedSize = string.IsNullOrWhiteSpace(objectSizeToSearch) ||
                    String.Compare(
                        excelObjectSize,
                        objectSizeToSearch,
                        CultureInfo.CurrentCulture,
                        CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols) == 0;

                if (!isMatchedName || !isMatchedSize)
                    continue;

                if (string.IsNullOrWhiteSpace(excelObjectName))
                    excelObjectName = excelConfigName;

                string listPrice = GetCellValue(excelRow.GetCell(columnMapping.ListPrice));
                if (string.IsNullOrEmpty(listPrice)) continue;

                CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
                double listPriceValue = double.Parse(listPrice, NumberStyles.Currency, culture);

                return new PriceExcelData
                {
                    PartNumber = GetCellValue(excelRow.GetCell(columnMapping.PartNumber)),
                    ObjectName = excelObjectName,
                    ObjectSize = excelObjectSize,
                    ListPrice = listPriceValue,
                    SimFlexPrice = GetSimFlexPrice()
                };
            }
        }

        return null;
    }

    public PriceExcelData[] GetColumnData(int priceColumnNumber, int minRowNumber, int maxRowNumber, string sheetName)
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

            List<PriceExcelData> columnData = new List<PriceExcelData>();

            for (int row = minRowNumber; row <= maxRowNumber && row <= sheet.LastRowNum; row++)
            {
                IRow excelRow = sheet.GetRow(row);
                if (excelRow == null) continue;

                string cellValue = GetCellValue(excelRow.GetCell(priceColumnNumber));
                CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
                double listPriceValue = double.Parse(cellValue, NumberStyles.Currency, culture);
                if (!string.IsNullOrEmpty(cellValue))
                {
                    columnData.Add(new PriceExcelData
                    {
                        PartNumber = excelRow.GetCell(0)?.ToString(),
                        ObjectName = excelRow.GetCell(1)?.ToString(),
                        ListPrice = listPriceValue
                    });
                }
            }

            return columnData.ToArray();
        }
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

        PriceExcelData data = GetRowData(59, DataFilePaths.sheetNameLight);
        _cachedSimFlexPrice = data?.ListPrice ?? 0.0;
        return _cachedSimFlexPrice.Value;
    }

    public PriceExcelData GetInstallationLightsCharges()
    {
        return GetRowData(98, DataFilePaths.sheetNameLight);
    }
    public PriceExcelData GetShippingLightsCharges()
    {
        return GetRowData(99, DataFilePaths.sheetNameLight);
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

        return new PriceExcelData
        {
            PartNumber = GetCellValue(row.GetCell(0)),
            ObjectName = GetCellValue(row.GetCell(1)),
            ListPrice = ParseCurrency(GetCellValue(row.GetCell(3))),
            //SimFlexPrice = ParseCurrency(GetCellValue(row.GetCell(4))),
           // ObjectSize = GetCellValue(row.GetCell(5))
        };
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
