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
    string filePath;
    
    string excelName = "PricingSheetForLight.xls";

    //public string testObjectName;

    public void SetExceFileName(string excelFileNameToUse)
    {
        string path1 = Path.Combine(Application.streamingAssetsPath, "Data");
        string path2 = Path.Combine(path1, "quotes");
        filePath = Path.Combine(path2, excelFileNameToUse);

        //testing calls
        //ReadExcel(filePath);
        //FindPrice(testObjectName);
    }


    public PriceExcelData FindPrice(string objectName)
    {
        if (!File.Exists(filePath))
        {
            Debug.Log(filePath);
            Debug.LogError("Excel file not found!");
            return null;
        }

        using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            //IWorkbook workbook = new XSSFWorkbook(stream);

            IWorkbook workbook = new HSSFWorkbook(stream);
            ISheet sheet = workbook.GetSheetAt(0); // First sheet

            for (int row = 0; row <= sheet.LastRowNum; row++)
            {
                IRow excelRow = sheet.GetRow(row);
                if (excelRow == null) continue;

                string rowData = "";
                for (int col = 0; col < excelRow.LastCellNum; col++)
                {
                    rowData += excelRow.GetCell(col)?.ToString() + "\t";
                }

                string excelObjectName = excelRow.GetCell(2)?.ToString();

                bool isMatched = String.Compare(excelObjectName, objectName, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols) == 0;

                if (isMatched)//Object found in the excel return the price and exit the loop
                {
                    Debug.Log("Price: " + excelRow.GetCell(3)?.ToString());
                    //return excelRow.GetCell(3)?.ToString();
                    string listPrice = excelRow.GetCell(3)?.ToString();
                    CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
                    double listPriceValue = double.Parse(listPrice, NumberStyles.Currency, culture);

                    PriceExcelData priceExcelData =  new PriceExcelData {
                        PartNumber = excelRow.GetCell(0)?.ToString(),
                        ObjectName = excelRow.GetCell(2)?.ToString(), 
                        ListPrice = listPriceValue,
                        SimFlexPrice = GetSimFlexPrice()
                    };
                    Debug.Log($"Price found for {objectName} for {priceExcelData.ListPrice} ");

                    return priceExcelData;
                }
                else
                {
                    Debug.Log($"Object not found row number {row} while value {excelRow.GetCell(2)?.ToString()}");
                }
            }
            Debug.LogError("Object not found in the excel! " + objectName);
            return null;
        }
    }

    public PriceExcelData[] GetColumnData(int priceColumnNumber, int minRowNumber, int maxRowNumber)
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
            ISheet sheet = workbook.GetSheetAt(0); // First sheet

            List<PriceExcelData> columnData = new List<PriceExcelData>();

            for (int row = minRowNumber; row <= maxRowNumber && row <= sheet.LastRowNum; row++)
            {
                IRow excelRow = sheet.GetRow(row);
                if (excelRow == null) continue;

                string cellValue = excelRow.GetCell(priceColumnNumber)?.ToString();///Price Sell value
                Debug.Log("priceColumnNumber: " + priceColumnNumber);
                Debug.Log("cellValue: " + cellValue);
                CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
                if (cellValue.Contains("$"))
                {
                    double listPriceValue = double.Parse(cellValue, NumberStyles.Currency, culture);
                    if (!string.IsNullOrEmpty(cellValue))
                    {
                        string listPrice = excelRow.GetCell(3)?.ToString();

                        columnData.Add(new PriceExcelData
                        {
                            PartNumber = excelRow.GetCell(0)?.ToString(),
                            ObjectName = excelRow.GetCell(1)?.ToString(),
                            ListPrice = listPriceValue//double.TryParse(cellValue, out double price) ? price : 0
                        });
                    }
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
    public string GetCellValue(int rowNumber, int columnNumber)
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
            ISheet sheet = workbook.GetSheetAt(0); // First sheet

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
            return cell?.ToString();
        }
    }

    public double GetSimFlexPrice()
    {
       // double.TryParse(GetCellValue(33, 3), out double result);//This is hard coded value from the excel, change it if the things get chagnd in the excel. 
        string cellValue = GetCellValue(33, 3);
        //string cellValue = excelRow.GetCell(priceColumnNumber)?.ToString();///Price Sell value
        
        Debug.Log("cellValue: " + cellValue);
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        double listPriceValue = double.Parse(cellValue, NumberStyles.Currency, culture);


        return listPriceValue;
        //114-001111	Sim.Flex Custom Arm Option	Assign when sim.flex option is selected	$5,358.73	$5,358.73

    }
}


