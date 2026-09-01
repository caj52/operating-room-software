

using System.Collections.Generic;

public class DataFilePaths 
{
    //public static string ExcelFileBoomPricingSheet = "BoomPricingSheet_New_7Apr25.xls";// "BoomPricingSheet.xls";
    //public static string ExcelFilePricingSheetForLight = "PricingSheetForLight_New_7Apr25.xls";// "PricingSheetForLight.xls";

    public static string ExcelFileNameForLightAndBoomPricing = "2026_0501_-_Quote_Request_V6_(Blank).xlsx";

    public static string sheetNameLight = "2025_03_28 3D Light Pricing";
    public static string sheetNameBoomIndividual = "2025_03_28 Boom Pricing";
    public static string sheetNameBoomCombined = "2025_03_28 3D Boom Pricing"; // Bundled boom configuration list prices

    // Column mappings for each sheet
    public static readonly Dictionary<string, ExcelColumnMapping> SheetColumnMappings = new Dictionary<string, ExcelColumnMapping>
    {
        { sheetNameLight, new ExcelColumnMapping { PartNumber = 0, ObjectName = 2, ListPrice = 3, ObjectSize = -1 } },
        { sheetNameBoomIndividual, new ExcelColumnMapping { PartNumber = 0, ObjectName = 1, ListPrice = 4, ObjectSize = 5 } },
        { sheetNameBoomCombined, new ExcelColumnMapping { PartNumber = 0, ObjectName = 1, ListPrice = 3, ObjectSize = -1 } }
    };
}

// Class to represent column mappings
public class ExcelColumnMapping
{
    public int PartNumber { get; set; }
    public int ObjectName { get; set; }
    public int ListPrice { get; set; }
    public int ObjectSize { get; set; }
}
