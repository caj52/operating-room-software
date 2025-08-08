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
    
    private void Start()
    {
        
        SetExcelFileName();
    }

    public void SetExcelFileName()
    {
        string fileName = DataFilePaths.ExcelFileNameForLightAndBoomPricing;
        string excelBasePath = Path.Combine(Application.streamingAssetsPath, "Data", "quotes");
        
        filePath = Path.Combine(excelBasePath, fileName);
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"The specified Excel file does not exist at path: {filePath}");
        }
    }

    private bool ValidateFilePath()
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"Excel file not found at path: {filePath}");
            return false;
        }
        return true;
    }

    public PriceExcelData FetchPricingDataFromExcel(string sheetName, string objectNameToSearch, string objectSizeToSearch =null)
    {
        if (!ValidateFilePath()) return null;

        if (!DataFilePaths.SheetColumnMappings.TryGetValue(sheetName, out var columnMapping))
        {
            Debug.LogError($"No column mapping found for sheet: {sheetName}");
            return null;
        }

        Debug.Log($"Finding Start for Sheet Name {sheetName} and File path is : {filePath} ");
        using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            IWorkbook workbook = new HSSFWorkbook(stream);
            ISheet sheet = workbook.GetSheet(sheetName);
            if (sheet == null)
            {
                Debug.LogError("Sheet not found in the excel file!");
                return null;
            }

            Debug.Log($"Searching for Object: {objectNameToSearch} and the size {objectSizeToSearch}");

            for (int row = 0; row <= sheet.LastRowNum; row++)
            {
                IRow excelRow = sheet.GetRow(row);
                if (excelRow == null) continue;

                string excelObjectName = GetCellValue(excelRow.GetCell(columnMapping.ObjectName));
                string excelObjectSize = "";
                if (columnMapping.ObjectSize != -1)
                {
                    excelObjectSize = GetCellValue(excelRow.GetCell(columnMapping.ObjectSize));
                }
                else
                {
                    Debug.Log("Size is not availble for this object!");
                }
                

                Debug.Log($"Excel Object Name: {excelObjectName} Size: {excelObjectSize}");

                // Match name (mandatory) and size (optional if provided)
                bool isMatchedName = String.Compare(
                    excelObjectName,
                    objectNameToSearch,
                    CultureInfo.CurrentCulture,
                    CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols) == 0;

                bool isMatchedSize = string.IsNullOrWhiteSpace(objectSizeToSearch) ||
                    String.Compare(
                        excelObjectSize,
                        objectSizeToSearch,
                        CultureInfo.CurrentCulture,
                        CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols) == 0;

                if (isMatchedName && isMatchedSize)
                {
                    string listPrice = GetCellValue(excelRow.GetCell(columnMapping.ListPrice));
                    if (string.IsNullOrEmpty(listPrice)) continue;

                    CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
                    double listPriceValue = double.Parse(listPrice, NumberStyles.Currency, culture);

                    PriceExcelData priceExcelData = new PriceExcelData
                    {
                        PartNumber = GetCellValue(excelRow.GetCell(columnMapping.PartNumber)),
                        ObjectName = excelObjectName,
                        ObjectSize = excelObjectSize,
                        ListPrice = listPriceValue,
                        SimFlexPrice = GetSimFlexPrice()
                    };

                    Debug.Log($"Price found for {objectNameToSearch} size: {objectSizeToSearch} Price: {priceExcelData.ListPrice}");
                    return priceExcelData;
                }
            }

            Debug.LogError($"Object not found in the excel! {objectNameToSearch} while size is {objectSizeToSearch}");

            return null;
        }
    }

    public PriceExcelData[] GetColumnData(int priceColumnNumber, int minRowNumber, int maxRowNumber, string sheetName)
    {
        if (!File.Exists(filePath))
        {
            Debug.Log(filePath);
            Debug.LogError("Excel file not found!");
            return null;
        }

        Debug.Log(filePath);
       
        using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            //IWorkbook workbook = new XSSFWorkbook(stream);
            IWorkbook workbook = new HSSFWorkbook(stream);
            //ISheet sheet = workbook.GetSheetAt(0); // First sheet
            ISheet sheet = workbook.GetSheet(sheetName); // First sheet
         
                 Debug.Log("sheetName: " + sheet.SheetName);
            List<PriceExcelData> columnData = new List<PriceExcelData>();

            for (int row = minRowNumber; row <= maxRowNumber && row <= sheet.LastRowNum; row++)
            {
                IRow excelRow = sheet.GetRow(row);
                if (excelRow == null) continue;

                //string cellValue = excelRow.GetCell(priceColumnNumber)?.ToString();///Price Sell value
                string cellValue = GetCellValue(excelRow.GetCell(priceColumnNumber));///Price Sell value
                Debug.Log("priceColumnNumber: " + priceColumnNumber);
                Debug.Log("cellValue: " + cellValue);
                CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
                double listPriceValue = double.Parse(cellValue, NumberStyles.Currency, culture);
                if (!string.IsNullOrEmpty(cellValue))
                {
                    string listPrice = GetCellValue(excelRow.GetCell(3));
              
                    columnData.Add(new PriceExcelData
                    {
                        PartNumber = excelRow.GetCell(0)?.ToString(),
                        ObjectName = excelRow.GetCell(1)?.ToString(),
                        ListPrice = listPriceValue//double.TryParse(cellValue, out double price) ? price : 0
                    });
                }
            }

            return columnData.ToArray();
        }
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
            Debug.Log(filePath);
            Debug.LogError("Excel file not found!");
            return null;
        }

        using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            IWorkbook workbook = new HSSFWorkbook(stream);
            ISheet sheet = workbook.GetSheet(sheetName); // First sheet

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
        PriceExcelData data = GetRowData(59, DataFilePaths.sheetNameLight);
        return data?.ListPrice ?? 0.0;
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
        if (!ValidateFilePath()) return null;

        using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            IWorkbook workbook = new HSSFWorkbook(stream);
            ISheet sheet = workbook.GetSheet(sheetName);
            if (sheet == null || rowIndex < 0 || rowIndex > sheet.LastRowNum) return null;

            return sheet.GetRow(rowIndex);
        }
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


    /*
    public PriceExcelData GetInstallationLightsCharges()
    {
        string sheetName = DataFilePaths.sheetNameLight;
        string price = GetCellValue(98, 3, sheetName);
        string title = GetCellValue(98, 1, sheetName);
        PriceExcelData priceExcelData = new PriceExcelData();
        priceExcelData.PartNumber = GetCellValue(98, 0, sheetName);
        priceExcelData.ObjectName = GetCellValue(98, 1, sheetName);

        Debug.Log("Installation Lights Charges: " + price);
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        double listPriceValue = double.Parse(price, NumberStyles.Currency, culture);
        priceExcelData.ListPrice = listPriceValue;
        return priceExcelData;
    }
    public PriceExcelData GetShippingLightsCharges()
    {
        string sheetName = DataFilePaths.sheetNameLight;
        
        string price = GetCellValue(99, 3, sheetName);
        Debug.Log("Shipping Light Charges: " + price);
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
            IWorkbook workbook = new HSSFWorkbook(stream);
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
