#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode checks for sales-proposal pricing. One button writes
/// TestData/pricing_reports/FOR_AGENT.txt for Cursor to read.
/// Menu: Tools → Operating Room → Generate Pricing Report for Cursor
/// </summary>
public sealed class PricingDiagnosticsWindow : EditorWindow
{
    const string DefaultV6Rel =
        "StreamingAssets/Data/quotes/2026_0501_-_Quote_Request_V6_(Blank).xlsx";
    const string ReportDirRel = "TestData/pricing_reports";
    const string AgentFileName = "FOR_AGENT.txt";

    Vector2 _scroll;
    string _log = "Press Generate report, then tell Cursor.";
    string _lastReportPath;

    [MenuItem("Tools/Operating Room/Generate Pricing Report for Cursor")]
    public static void RunForAgent()
    {
        string path = GenerateReport(DefaultV6Path());
        Debug.Log($"[Pricing Checks] Wrote {path}");
        if (Application.isBatchMode)
            EditorApplication.Exit(File.Exists(path) ? 0 : 1);
        else
            EditorUtility.RevealInFinder(path);
    }

    [MenuItem("Tools/Operating Room/Pricing Checks")]
    public static void Open()
    {
        var w = GetWindow<PricingDiagnosticsWindow>("Pricing Checks");
        w.minSize = new Vector2(640, 420);
        w.Show();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Pricing checks (no Play Mode)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Press Generate report. Then tell Cursor — it reads TestData/pricing_reports/FOR_AGENT.txt.",
            MessageType.Info);

        if (GUILayout.Button("Generate report", GUILayout.Height(44)))
        {
            RunMatch(DefaultV6Path());
            if (!string.IsNullOrEmpty(_lastReportPath) && File.Exists(_lastReportPath))
                EditorUtility.RevealInFinder(_lastReportPath);
        }

        if (GUILayout.Button("Use a different spreadsheet…"))
        {
            string picked = EditorUtility.OpenFilePanel(
                "Imagine price spreadsheet",
                Path.GetDirectoryName(DefaultV6Path()),
                "xlsx,xls");
            if (!string.IsNullOrEmpty(picked))
                RunMatch(picked);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = !string.IsNullOrEmpty(_lastReportPath) && File.Exists(_lastReportPath);
            if (GUILayout.Button("Open last report"))
                EditorUtility.RevealInFinder(_lastReportPath);
            GUI.enabled = true;
            if (GUILayout.Button("Open reports folder"))
            {
                string dir = ReportDir();
                Directory.CreateDirectory(dir);
                EditorUtility.RevealInFinder(dir);
            }
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.TextArea(_log, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    static string DefaultV6Path() =>
        Path.GetFullPath(Path.Combine(Application.dataPath, DefaultV6Rel));

    static string ReportDir() =>
        Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, ReportDirRel));

    void RunMatch(string supplierPath)
    {
        try
        {
            _lastReportPath = GenerateReport(supplierPath);
            _log = File.ReadAllText(_lastReportPath);
            Debug.Log($"[Pricing Checks] Wrote {_lastReportPath}");
        }
        catch (Exception ex)
        {
            _log = ex.ToString();
            Debug.LogException(ex);
        }
    }

    public static string GenerateReport(string supplierPath)
    {
        var report = PricingBridgeImporter.Analyze(supplierPath);
        string text = BuildLog(supplierPath, report);
        return WriteReport(text);
    }

    static string BuildLog(string supplierPath, PricingBridgeImporter.MatchReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Pricing match (same logic as Sales proposal → Update prices → Load spreadsheet)");
        sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Spreadsheet: {supplierPath}");
        sb.AppendLine($"Price sheet: {PricingBridgeStore.BundledV6Path}");
        sb.AppendLine();

        if (!report.Ok)
        {
            sb.AppendLine("FAILED");
            sb.AppendLine(report.Message);
            foreach (var p in report.Problems ?? Enumerable.Empty<string>())
                sb.AppendLine("  " + p);
            return sb.ToString();
        }

        double matchPct = report.CatalogCount == 0 ? 0 : 100.0 * report.Matched / report.CatalogCount;
        sb.AppendLine(report.Message);
        sb.AppendLine($"Match rate: {matchPct:0.1}%  ({report.Matched}/{report.CatalogCount})");
        sb.AppendLine(
            $"Rows pulled from spreadsheet — lights {report.LightSupplierRows}, " +
            $"booms {report.BoomSupplierRows}, install {report.InstallSupplierRows} " +
            $"(total {report.SupplierRowCount})");
        sb.AppendLine();

        sb.AppendLine("=== Known Imagine List checks (V6 blank template) ===");
        sb.AppendLine("If these miss or land on the wrong row, in-game totals will be off.");
        foreach (var check in KnownChecks)
            sb.AppendLine(DescribeKnownCheck(report, check));
        sb.AppendLine();

        var moved = report.Items
            .Where(i => i != null && !i.missingFromSupplier && i.catalogPrice > 0)
            .Select(i =>
            {
                double pct = (i.effectivePrice - i.catalogPrice) / i.catalogPrice * 100.0;
                return (item: i, pct);
            })
            .Where(x => Math.Abs(x.pct) >= 2.0)
            .OrderByDescending(x => Math.Abs(x.pct))
            .Take(40)
            .ToList();

        sb.AppendLine("=== Matched rows that move ≥2% vs the app list ===");
        sb.AppendLine("(This is the kind of gap their V1.5.4 comparison called out.)");
        if (moved.Count == 0)
            sb.AppendLine("None. Matched prices stay within 2% of the current app list.");
        else
        {
            foreach (var x in moved)
            {
                sb.AppendLine(
                    $"  {x.pct,+6:0.1}%  catalog ${x.item.catalogPrice:N2} → " +
                    $"${x.item.effectivePrice:N2}  |  {ShortItem(x.item)}  ←  {x.item.supplierMatch}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== Highest-dollar items still missing a spreadsheet match ===");
        var missing = report.Items
            .Where(i => i != null && i.missingFromSupplier && i.catalogPrice > 0)
            .OrderByDescending(i => i.catalogPrice)
            .Take(40)
            .ToList();
        if (missing.Count == 0)
            sb.AppendLine("None.");
        else
        {
            foreach (var i in missing)
                sb.AppendLine($"  ${i.catalogPrice,10:N2}  {ShortItem(i)}");
        }

        return sb.ToString();
    }

    struct KnownCheck
    {
        public string Label;
        public double V6ImagineList;
        public string NameContains;
        public bool PreferBoomSheet;
        public bool PreferLightSheet;
    }

    static readonly KnownCheck[] KnownChecks =
    {
        new() { Label = "Powered XL boom", V6ImagineList = 33564.85, NameContains = "Powered XL", PreferBoomSheet = true },
        new() { Label = "Spring boom", V6ImagineList = 27458.40, NameContains = "Spring Boom, Top Arm", PreferBoomSheet = true },
        new() { Label = "LED / LED light", V6ImagineList = 13045.42, NameContains = "LED / LED", PreferLightSheet = true },
        new() { Label = "LED / FP light", V6ImagineList = 9790.38, NameContains = "LED / FP", PreferLightSheet = true },
        new() { Label = "Install MediBoom Standard", V6ImagineList = 2800.88, NameContains = "Install MediBoom Standard" },
    };

    static string DescribeKnownCheck(PricingBridgeImporter.MatchReport report, KnownCheck check)
    {
        var hits = report.Items.Where(i => i != null && NameLooksLike(i, check)).ToList();
        if (hits.Count == 0)
            return $"  MISS  {check.Label} — no app-list row containing “{check.NameContains}” (V6 Imagine List ${check.V6ImagineList:N2})";

        var best = hits
            .OrderBy(i => i.missingFromSupplier)
            .ThenBy(i => Math.Abs(i.effectivePrice - check.V6ImagineList))
            .ThenBy(i => Math.Abs(i.catalogPrice - check.V6ImagineList))
            .First();

        double vsV6 = check.V6ImagineList == 0
            ? 0
            : (best.effectivePrice - check.V6ImagineList) / check.V6ImagineList * 100.0;
        string match = best.missingFromSupplier ? "unmatched" : best.supplierMatch;
        string flag = Math.Abs(vsV6) > 2.0 ? "OFF" : "OK ";
        return
            $"  {flag}  {check.Label}: app ${best.catalogPrice:N2} → matched ${best.effectivePrice:N2} " +
            $"vs V6 ${check.V6ImagineList:N2} ({vsV6:+0.1;-0.1}%)  |  {ShortItem(best)}  |  {match}";
    }

    static bool NameLooksLike(PricingBridgeItem item, KnownCheck check)
    {
        string blob = $"{item.objectName} {item.configurationName}";
        if (blob.IndexOf(check.NameContains, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        bool boomSheet = (item.sheetName ?? "").IndexOf("Boom", StringComparison.OrdinalIgnoreCase) >= 0;
        bool lightSheet = (item.sheetName ?? "").IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0;
        if (check.PreferBoomSheet && !boomSheet) return false;
        if (check.PreferLightSheet && !lightSheet) return false;
        return true;
    }

    static string ShortItem(PricingBridgeItem i)
    {
        string size = string.IsNullOrWhiteSpace(i.size) ? "" : $" [{i.size}]";
        string sheet = i.sheetName ?? "";
        if (sheet.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0) sheet = "Light";
        else if (sheet.IndexOf("3D Boom", StringComparison.OrdinalIgnoreCase) >= 0) sheet = "Boom bundle";
        else if (sheet.IndexOf("Boom", StringComparison.OrdinalIgnoreCase) >= 0) sheet = "Boom";
        return $"{sheet} / {i.objectName}{size}";
    }

    static string WriteReport(string text)
    {
        string dir = ReportDir();
        Directory.CreateDirectory(dir);
        string agentPath = Path.Combine(dir, AgentFileName);
        File.WriteAllText(agentPath, text, Encoding.UTF8);
        string stamped = Path.Combine(dir, $"match_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(stamped, text, Encoding.UTF8);
        return agentPath;
    }
}
#endif
