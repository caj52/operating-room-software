using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;

/// <summary>
/// Minimal Excel reader for supplier price workbooks (.xls via NPOI, .xlsx via Open XML zip).
/// </summary>
public static class SupplierExcelReader
{
    public class SheetTable
    {
        public string Name;
        public List<string[]> Rows = new();
    }

    public static bool TryOpen(string path, out List<SheetTable> sheets, out string error)
    {
        sheets = null;
        error = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error = "File not found.";
            return false;
        }

        try
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            sheets = ext is ".xlsx" or ".xlsm"
                ? ReadXlsx(path)
                : ReadXls(path);
            if (sheets == null || sheets.Count == 0)
            {
                error = "Opened the file but found no sheets.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not open spreadsheet: {ex.Message}";
            sheets = null;
            return false;
        }
    }

    static List<SheetTable> ReadXls(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        IWorkbook wb = new HSSFWorkbook(stream);
        var list = new List<SheetTable>();
        for (int i = 0; i < wb.NumberOfSheets; i++)
        {
            ISheet sheet = wb.GetSheetAt(i);
            var table = new SheetTable { Name = sheet.SheetName };
            for (int r = 0; r <= sheet.LastRowNum; r++)
            {
                IRow row = sheet.GetRow(r);
                if (row == null) continue;
                int last = Math.Max((int)row.LastCellNum, 1);
                var cells = new string[last];
                bool any = false;
                for (int c = 0; c < last; c++)
                {
                    string v = CellToString(row.GetCell(c));
                    cells[c] = v;
                    if (!string.IsNullOrWhiteSpace(v)) any = true;
                }
                if (any) table.Rows.Add(cells);
            }
            list.Add(table);
        }
        return list;
    }

    static string CellToString(ICell cell)
    {
        if (cell == null) return null;
        switch (cell.CellType)
        {
            case CellType.Numeric:
                return cell.NumericCellValue.ToString("G", CultureInfo.InvariantCulture);
            case CellType.String:
                return cell.StringCellValue;
            case CellType.Boolean:
                return cell.BooleanCellValue.ToString();
            case CellType.Formula:
                try
                {
                    if (cell.CachedFormulaResultType == CellType.Numeric)
                        return cell.NumericCellValue.ToString("G", CultureInfo.InvariantCulture);
                    return cell.StringCellValue;
                }
                catch { return cell.ToString(); }
            default:
                return cell.ToString();
        }
    }

    static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    static readonly XNamespace OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    static List<SheetTable> ReadXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var shared = ReadSharedStrings(zip);
        var sheetTargets = ReadSheetTargets(zip);
        var list = new List<SheetTable>();
        foreach (var (name, target) in sheetTargets)
        {
            string entryPath = NormalizeXlPath(target);
            ZipArchiveEntry entry = zip.GetEntry(entryPath)
                ?? zip.Entries.FirstOrDefault(e =>
                {
                    string full = e.FullName.Replace('\\', '/');
                    string t = target.Replace('\\', '/').TrimStart('/');
                    return full.Equals("xl/" + t, StringComparison.OrdinalIgnoreCase)
                           || full.EndsWith("/" + t, StringComparison.OrdinalIgnoreCase)
                           || full.Equals(t, StringComparison.OrdinalIgnoreCase);
                });
            if (entry == null) continue;

            var table = new SheetTable { Name = name };
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            foreach (var row in doc.Root.Descendants(MainNs + "row"))
            {
                var cells = new Dictionary<int, string>();
                int max = -1;
                foreach (var c in row.Elements(MainNs + "c"))
                {
                    string refAttr = (string)c.Attribute("r");
                    int col = ColumnIndexFromRef(refAttr);
                    string val = ReadXlsxCell(c, shared);
                    cells[col] = val;
                    if (col > max) max = col;
                }
                if (max < 0) continue;
                var arr = new string[max + 1];
                bool any = false;
                for (int i = 0; i <= max; i++)
                {
                    if (cells.TryGetValue(i, out var v))
                    {
                        arr[i] = v;
                        if (!string.IsNullOrWhiteSpace(v)) any = true;
                    }
                }
                if (any) table.Rows.Add(arr);
            }
            list.Add(table);
        }
        return list;
    }

    static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return list;
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        foreach (var si in doc.Root.Elements(MainNs + "si"))
        {
            var texts = si.Descendants(MainNs + "t").Select(t => t.Value);
            list.Add(string.Concat(texts));
        }
        return list;
    }

    static List<(string name, string target)> ReadSheetTargets(ZipArchive zip)
    {
        var result = new List<(string, string)>();
        var wbEntry = zip.GetEntry("xl/workbook.xml");
        var relEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (wbEntry == null || relEntry == null) return result;

        Dictionary<string, string> rels;
        using (var relStream = relEntry.Open())
        {
            var relDoc = XDocument.Load(relStream);
            rels = relDoc.Root.Elements(RelNs + "Relationship")
                .ToDictionary(
                    e => (string)e.Attribute("Id"),
                    e => (string)e.Attribute("Target"),
                    StringComparer.OrdinalIgnoreCase);
        }

        using var wbStream = wbEntry.Open();
        var wbDoc = XDocument.Load(wbStream);
        foreach (var sheet in wbDoc.Root.Element(MainNs + "sheets")?.Elements(MainNs + "sheet") ?? Enumerable.Empty<XElement>())
        {
            string name = (string)sheet.Attribute("name");
            string rid = (string)sheet.Attribute(OfficeRelNs + "id");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(rid) || !rels.TryGetValue(rid, out var target))
                continue;
            result.Add((name, target));
        }
        return result;
    }

    static string NormalizeXlPath(string target)
    {
        string t = (target ?? "").Replace('\\', '/').TrimStart('/');
        if (t.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
            return t;
        return "xl/" + t;
    }

    static string ReadXlsxCell(XElement c, List<string> shared)
    {
        string type = (string)c.Attribute("t");
        var v = c.Element(MainNs + "v");
        if (type == "s" && v != null && int.TryParse(v.Value, out int idx) && idx >= 0 && idx < shared.Count)
            return shared[idx];
        if (type == "inlineStr")
            return string.Concat(c.Descendants(MainNs + "t").Select(t => t.Value));
        return v?.Value;
    }

    static int ColumnIndexFromRef(string cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return 0;
        int col = 0;
        foreach (char ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return Math.Max(0, col - 1);
    }

    public static bool TryParsePrice(string raw, out double price)
    {
        price = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string cleaned = raw.Trim().Replace("$", "").Replace(",", "");
        return double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out price)
               || double.TryParse(cleaned, NumberStyles.Any, CultureInfo.GetCultureInfo("en-US"), out price);
    }
}
