/// <summary>
/// This is template class to read excel specific column and row value and save in this class object.
/// </summary>
public class PriceExcelData
{
    public string PartNumber { get; set; }
    public string ObjectName { get; set; }
    public double ListPrice { get; set; }
    public double SimFlexPrice { get; set; }
    public bool isSimFlexArmAvailable { get; set; }
}
