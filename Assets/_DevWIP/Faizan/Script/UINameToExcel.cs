using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// This script maps UI names to Excel-compatible keys. You can get excel name by passing the UI button name. It currently used for Boom Objects
/// Author: Faizan
/// </summary>
public class UINameToExcelKey
{
    private static readonly Dictionary<string, string> ExcelKeyMappings;

    // Static constructor to initialize the dictionary
    static UINameToExcelKey()
    {
        ExcelKeyMappings = new Dictionary<string, string>();

        //FirName is UI name, Second Name is excel Name
        // Original mappings
        var originalMappings = new Dictionary<string, string>
        {
            { "Powered", "Powered" },
            { "Spring", "Spring" },
            { "Fixed", "Fixed" },
            { "Powered XL", "Powered XL" },
            { "Spring XL", "Spring XL" },
            { "Fixed XL", "Fixed XL" },
            { "Boom XL Ceiling Flange", "Ceiling Flange XL" },
            { "Boom  Ceiling Flange", "Ceiling Tube" },
            { "TopArm", "Top Arm" },
            { "XL Top Arm", "Top Arm" },//XL Top //query here about top arm xl
//            { "Boom Column Tube", "Ceiling Flange XL" },
            { "Boom Service Head", "Service Head" },
            { "Large Monitor Mount", "Large Monitor Boom" },
            { "Tandem Mount", "Tandem (1st Position)" },
            { "Single Mount - Boom", "Single Boom" },
            { "Gas Outlet (CO2)", "Medical Gas" },
            { "Gas Outlet (He-O2)", "Medical Gas" },
            { "Gas Outlet (Instrument Air)", "Medical Gas" },
            { "Gas Outlet (Medical Air)", "Medical Gas" },
            { "Gas Outlet (Nitrogen)", "Medical Gas" },
            { "Gas Outlet (Nitrous Oxide)", "Medical Gas" },
            { "Gas Outlet (O2-He)", "Medical Gas" },
            { "Gas Outlet (Oxygen)", "Medical Gas" },
            { "Gas Outlet (Vacuum Suction)", "Medical Gas" },
            { "Gas Outlet (WAGD Evac)", "Medical Gas" },
            { "Storz 4-1 Plate", "Accessories for Plate" },
            { "Red Duplex", "Electrical (4 Duplex)" },
            { "Service Head Rails (1000mm)", "Straight Rail" },
            { "Service Head Shelf (750mm with Controls)", "Shelf (750mm)" },
            { "Service Head Shelf (750mm)", "Shelf (750mm)" },
            { "Service Head Shelf (500mm with Controls)", "Shelf (500mm)" },
            { "Service Head Shelf (500mm)", "Shelf (500mm)" },
            { "Nitrogen Regulator", "Nitrogen Regulator" },
            { " Blank Plate", "Blank Preparation" },
        };

        // Add both original and processed keys to the dictionary
        foreach (var entry in originalMappings)
        {
            string originalKey = entry.Key;
            string processedKey = RemoveSpacesAndToLower(originalKey);

            // Add both original and processed keys
            ExcelKeyMappings[originalKey] = entry.Value;
            ExcelKeyMappings[processedKey] = entry.Value;
        }
    }

    /// <summary>
    /// Retrieves the Excel-compatible name for a given UI name.
    /// </summary>
    /// <param name="uiName">The UI name to look up.</param>
    /// <returns>The corresponding Excel-compatible name, or null if not found.</returns>
    public static string GetExcelName(string uiName)
    {
        if (string.IsNullOrWhiteSpace(uiName))
        {
            Debug.LogError("UI name cannot be null or empty.");
            return null;
        }

        uiName = uiName.Trim();
        string processedUiName = RemoveSpacesAndToLower(uiName);


        if (ExcelKeyMappings.TryGetValue(uiName, out string excelKey) ||
            ExcelKeyMappings.TryGetValue(processedUiName, out excelKey))
        {
            return excelKey;
        }

        Debug.LogWarning($"Excel key not found for UI name: {uiName}");
        return null;
    }

    /// <summary>
    /// Removes all spaces and converts the string to lowercase.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>The processed string.</returns>
    private static string RemoveSpacesAndToLower(string input)
    {
        return input.Replace(" ", "").ToLower();
    }
    /// <summary>
    /// Excel Name for Boom Base Model
    /// </summary>
    private static readonly string[] excelBoomBaseModelStrings = new[]
   {
        "Powered",
        "Powered XL",
        "Spring",
        "Spring XL",
        "Fixed",
        "Fixed XL",
        "Fixed XXL",
        "Large Monitor Boom",
        "Large Monitor Boom XL",
        "'Future' Boom"
    };

    public static bool IsBoomBaseModelFromExcel(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        // Normalize the input by removing spaces and converting to lowercase
        string normalizedInput = input.Replace(" ", "").ToLower();

        // Compare with the predefined strings
        return excelBoomBaseModelStrings.Any(s => s.Replace(" ", "").ToLower() == normalizedInput);
    }
}