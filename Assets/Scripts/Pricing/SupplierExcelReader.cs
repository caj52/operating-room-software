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

    public struct QuoteRates
    {
        public double Margin;
        public double Tariff;
        public double ShippingPerConfig;
        public double InstallationPerConfig;
    }

    /// <summary>
    /// Margin, tariff, and per-configuration shipping come from the workbook
    /// (the margin cell and the summary formulas), not from constants here.
    /// </summary>
    public static QuoteRates ReadQuoteRates(string path)
    {
        var rates = new QuoteRates();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return rates;
        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetExtension(path), ".xlsm", StringComparison.OrdinalIgnoreCase))
            return rates;

        try
        {
            using var zip = ZipFile.OpenRead(path);
            var shared = ReadSharedStrings(zip);
            var targets = ReadSheetTargets(zip);
            foreach (var (name, target) in targets)
            {
                var cells = ReadCellDetails(zip, target, shared);
                if (cells.Count == 0)
                    continue;

                if (name.IndexOf("Ondal", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Simeon", StringComparison.OrdinalIgnoreCase) >= 0)
                    rates.Margin = FirstMargin(cells, rates.Margin);

                if (name.IndexOf("Boom", StringComparison.OrdinalIgnoreCase) >= 0
                    && name.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0)
                    ReadSummaryRates(cells, ref rates);
            }
        }
        catch
        {
            // A sheet we cannot parse contributes no rates. Callers must not invent them.
        }

        return rates;
    }

    sealed class CellDetail
    {
        public string Text;
        public string Formula;
        public int Row;
        public int Col;
    }

    static Dictionary<string, CellDetail> ReadCellDetails(ZipArchive zip, string target, List<string> shared)
    {
        var map = new Dictionary<string, CellDetail>(StringComparer.OrdinalIgnoreCase);
        string entryPath = NormalizeXlPath(target);
        var entry = zip.GetEntry(entryPath);
        if (entry == null)
            return map;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        foreach (var c in doc.Descendants(MainNs + "c"))
        {
            string refAttr = (string)c.Attribute("r");
            if (string.IsNullOrEmpty(refAttr))
                continue;
            string text = ReadXlsxCell(c, shared);
            string formula = c.Element(MainNs + "f")?.Value;
            map[refAttr] = new CellDetail
            {
                Text = text,
                Formula = formula,
                Row = RowIndexFromRef(refAttr),
                Col = ColumnIndexFromRef(refAttr)
            };
        }
        return map;
    }

    static double FirstMargin(Dictionary<string, CellDetail> cells, double already)
    {
        if (already > 0 && already < 1)
            return already;

        foreach (var cell in cells.Values)
        {
            if (cell.Text == null || cell.Text.IndexOf("Imagine Margin", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            foreach (var other in cells.Values)
            {
                if (other.Col != cell.Col || other.Row <= cell.Row)
                    continue;
                if (TryParsePrice(other.Text, out double margin) && margin > 0 && margin < 1)
                    return margin;
            }
        }
        return already;
    }

    static void ReadSummaryRates(Dictionary<string, CellDetail> cells, ref QuoteRates rates)
    {
        foreach (var cell in cells.Values)
        {
            string formula = cell.Formula ?? "";
            if (formula.Length == 0)
                continue;

            var marginMatch = System.Text.RegularExpressions.Regex.Match(
                formula, @"/\s*\(\s*1\s*-\s*(\d*\.?\d+)\s*\)");
            if (marginMatch.Success
                && TryParsePrice(marginMatch.Groups[1].Value, out double margin)
                && margin > 0 && margin < 1
                && rates.Margin <= 0)
                rates.Margin = margin;

            var tariffMatch = System.Text.RegularExpressions.Regex.Match(
                formula, @"^[A-Za-z]+\d+\s*\*\s*(\d*\.?\d+)\s*$");
            string beside = LabelBeside(cells, cell);
            bool tariffLabel = beside.IndexOf("tariff", StringComparison.OrdinalIgnoreCase) >= 0
                               || beside.IndexOf("tarriff", StringComparison.OrdinalIgnoreCase) >= 0;
            if (tariffMatch.Success
                && tariffLabel
                && TryParsePrice(tariffMatch.Groups[1].Value, out double tariff)
                && tariff > 0 && tariff < 1)
                rates.Tariff = tariff;

            var perMatch = System.Text.RegularExpressions.Regex.Match(
                formula, @"\*\s*(\d*\.?\d+)\s*$");
            if (!perMatch.Success || !TryParsePrice(perMatch.Groups[1].Value, out double per) || per < 1)
                continue;

            string above = LabelAbove(cells, cell);
            if (above.IndexOf("shipping", StringComparison.OrdinalIgnoreCase) >= 0)
                rates.ShippingPerConfig = per;
            else if (above.IndexOf("install", StringComparison.OrdinalIgnoreCase) >= 0)
                rates.InstallationPerConfig = per;
        }
    }

    static string LabelBeside(Dictionary<string, CellDetail> cells, CellDetail cell)
    {
        foreach (var other in cells.Values)
        {
            if (other.Row == cell.Row && other.Col == cell.Col - 1)
                return other.Text ?? "";
        }
        return "";
    }

    static string LabelAbove(Dictionary<string, CellDetail> cells, CellDetail cell)
    {
        foreach (var other in cells.Values)
        {
            if (other.Col == cell.Col && other.Row == cell.Row - 1)
                return other.Text ?? "";
        }
        return "";
    }

    static int RowIndexFromRef(string cellRef)
    {
        if (string.IsNullOrEmpty(cellRef))
            return 0;
        int i = 0;
        while (i < cellRef.Length && char.IsLetter(cellRef[i]))
            i++;
        return int.TryParse(cellRef.Substring(i), out int row) ? row : 0;
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
