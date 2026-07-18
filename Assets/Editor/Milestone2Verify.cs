#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor verifier for Milestone 2 fixes. Run from:
///   Tools → Operating Room → Milestone 2 Verify
/// Writes a text report under Tools/Reports/ that agents can re-read.
/// </summary>
public static class Milestone2Verify
{
    private const string ReportFolder = "Tools/Reports";
    private const string MenuRoot = "Tools/Operating Room/";

    private sealed class Check
    {
        public string Id;
        public string Name;
        public string Status; // PASS | FAIL | WARN | SKIP
        public string Detail;
    }

    [MenuItem(MenuRoot + "Milestone 2 Verify", false, 50)]
    private static void RunVerify()
    {
        var checks = new List<Check>();
        var sb = new StringBuilder();
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string reportDir = Path.Combine(Directory.GetCurrentDirectory(), ReportFolder);
        Directory.CreateDirectory(reportDir);
        string reportPath = Path.Combine(reportDir, $"Milestone2_Verify_{stamp}.txt");

        sb.AppendLine("ORS Milestone 2 Verification Report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Unity: {Application.unityVersion}");
        sb.AppendLine($"Project: {Directory.GetCurrentDirectory()}");
        sb.AppendLine($"Playing: {Application.isPlaying}");
        sb.AppendLine();

        // ── Automated static / filesystem checks ───────────────────────────
        sb.AppendLine("========== AUTOMATED CHECKS ==========");
        sb.AppendLine();

        RunSourceChecks(checks);
        RunPathLogicChecks(checks);
        RunExportArtifactScan(checks);
        RunPricingReferenceHints(checks);

        int pass = checks.Count(c => c.Status == "PASS");
        int fail = checks.Count(c => c.Status == "FAIL");
        int warn = checks.Count(c => c.Status == "WARN");
        int skip = checks.Count(c => c.Status == "SKIP");

        foreach (var c in checks)
        {
            sb.AppendLine($"[{c.Status}] {c.Id} — {c.Name}");
            if (!string.IsNullOrWhiteSpace(c.Detail))
                sb.AppendLine($"         {c.Detail}");
        }

        sb.AppendLine();
        sb.AppendLine($"SUMMARY: {pass} PASS, {fail} FAIL, {warn} WARN, {skip} SKIP (total {checks.Count})");
        sb.AppendLine();

        // ── Manual checklist (append every run) ────────────────────────────
        sb.AppendLine(GetManualChecklist());

        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);

        // Also write/update a stable "latest" path for agents.
        string latestPath = Path.Combine(reportDir, "Milestone2_Verify_LATEST.txt");
        File.WriteAllText(latestPath, sb.ToString(), Encoding.UTF8);

        Debug.Log($"[Milestone2Verify] Report written:\n{reportPath}\n(also {latestPath})");
        EditorUtility.RevealInFinder(reportPath);
        EditorUtility.DisplayDialog(
            "Milestone 2 Verify",
            $"Done.\n\n{pass} PASS / {fail} FAIL / {warn} WARN / {skip} SKIP\n\nReport:\n{reportPath}",
            "OK");
    }

    [MenuItem(MenuRoot + "Open Latest Milestone 2 Report", false, 51)]
    private static void OpenLatest()
    {
        string latest = Path.Combine(Directory.GetCurrentDirectory(), ReportFolder, "Milestone2_Verify_LATEST.txt");
        if (!File.Exists(latest))
        {
            EditorUtility.DisplayDialog("Milestone 2 Verify", "No report yet. Run Milestone 2 Verify first.", "OK");
            return;
        }

        EditorUtility.RevealInFinder(latest);
        Application.OpenURL("file:///" + latest.Replace("\\", "/"));
    }

    // ── Check implementations ──────────────────────────────────────────────

    private static void RunSourceChecks(List<Check> checks)
    {
        ExpectSource(checks, "SRC-OBJ-EXPORT",
            "ObjExporter writes OBJ/MTL (not GLB-only)",
            "Assets/Scripts/ObjExporter.cs",
            mustContain: new[] { "mtllib", ".obj", ".mtl", "ExportPaths.ObjSceneDir" },
            mustNotContain: new[] { "GlbExporter.DoExport(makeSubmeshes" });

        ExpectSource(checks, "SRC-EXPORT-PATHS",
            "ExportPaths default parent is AppData persistentDataPath",
            "Assets/Scripts/IO/ExportPaths.cs",
            mustContain: new[]
            {
                "Application.persistentDataPath",
                "\"OBJ\", \"Room\"",
                "LegacyDocumentsFolderName"
            },
            mustNotContain: null);

        ExpectSource(checks, "SRC-OBJ-DIR",
            "ObjSceneDir is {Room}/OBJ/Room",
            "Assets/Scripts/IO/ExportPaths.cs",
            mustContain: new[] { "\"OBJ\", \"Room\"" },
            mustNotContain: null);

        ExpectSource(checks, "SRC-SAVE-FORCE-PATH",
            "Save forces AppData Saved / Saved/Configs",
            "Assets/Scripts/IO/Save.cs",
            mustContain: new[]
            {
                "GetSavedConfigsFolder()",
                "GetSavedRoomsFolder()",
                "name-only"
            },
            mustNotContain: null);

        ExpectSource(checks, "SRC-EXPORT-ALL",
            "Export hub has Export All one-click",
            "Assets/Scripts/UI/UI_ExportOptions.cs",
            mustContain: new[] { "Export All", "RunExportAll" },
            mustNotContain: null);

        ExpectSource(checks, "SRC-EXPORT-DEFAULTS",
            "Room export defaults include elevations + proposal + snapshots",
            "Assets/Scripts/Export/ExportRequest.cs",
            mustContain: new[]
            {
                "IncludeElevations = true",
                "IncludeProposal = true",
                "IncludeSnapshots = true"
            },
            mustNotContain: null);

        ExpectSource(checks, "SRC-PHONE",
            "Proposal phone is 214.987.0404",
            "Assets/_DevWIP/Faizan/PDF/Scripts/ProposalPDFGenerator.cs",
            mustContain: new[] { "214.987.0404" },
            mustNotContain: new[] { "877 789 8106" });

        ExpectSource(checks, "SRC-PHONE-PREVIEW",
            "Proposal preview phone is 214.987.0404",
            "Assets/Scripts/UI/ProposalPreviewModel.cs",
            mustContain: new[] { "214.987.0404" },
            mustNotContain: new[] { "877 789 8106" });

        ExpectSource(checks, "SRC-SALES-REP",
            "Proposal header prints sales rep name/phone/email",
            "Assets/_DevWIP/Faizan/PDF/Scripts/ProposalPDFGenerator.cs",
            mustContain: new[] { "salesRepName", "salesRepPhone", "salesRepEmail" },
            mustNotContain: new[] { "Sales Rep:" });

        ExpectSource(checks, "SRC-BOOM-KEY",
            "Boom pricing key uses live scales + bottom-arm/BCM family (not Fixed Top Arm / XXL mesh XL)",
            "Assets/Scripts/Export/ProposalPricingResolver.cs",
            mustContain: new[]
            {
                "InferSizesFromSelectables",
                "InferBoomFamily",
                "ApplyBoomConfigurationManager",
                "never overwrite live scale"
            },
            mustNotContain: null);

        ExpectSource(checks, "SRC-BOOM-EXTRAS",
            "Boom extras (covers/duplex/gas/shelves) priced on top of bundled boom package",
            "Assets/Scripts/Export/ProposalPricingResolver.cs",
            mustContain: new[]
            {
                "ResolveBoomExtraLines",
                "IsBoomExtraPart",
                "SumConfigListPrice"
            },
            mustNotContain: null);

        ExpectSource(checks, "SRC-METRIC-DIMS",
            "Elevation overlays use metric (mm) in photo mode",
            "Assets/Scripts/Measurer.cs",
            mustContain: new[] { "IsInElevationPhotoMode", " mm", "GetFloorTopY" },
            mustNotContain: null);

        ExpectSource(checks, "SRC-SNAPSHOT-CEILING",
            "Snapshot hides/restores ceiling unconditionally",
            "Assets/Scripts/ScreenshotCapture.cs",
            mustContain: new[] { "SetCeilingRenderersEnabled(false)", "SetCeilingRenderersEnabled(true)" },
            mustNotContain: null);

        ExpectSource(checks, "SRC-CLIENT-DATA",
            "Client Data suppresses auto-popup / duplicate DDOL",
            "Assets/Scripts/UI/UI_ClientMetaData.cs",
            mustContain: new[] { "_open", "Destroy(gameObject)", "sceneLoaded" },
            mustNotContain: null);

        ExpectSource(checks, "SRC-DOOR-FLUSH",
            "Door placement flush embed logic present",
            "Assets/Scripts/Selectable.cs",
            mustContain: new[] { "GetDoorWallOutwardNormal", "halfThickness" },
            mustNotContain: null);

        ExpectSource(checks, "SRC-UONE-LIGHT",
            "LightFactory SetLight + emission sync",
            "Assets/Scripts/LightFactory.cs",
            mustContain: new[] { "SetLight", "ToggleEmissive" },
            mustNotContain: null);

        ExpectSource(checks, "SRC-ZOOM",
            "Room ortho zoom ignores empty UI raycast pads",
            "Assets/Scripts/RoomBoundary.cs",
            mustContain: new[] { "IsPointerOverBlockingUi", "OrthographicSize" },
            mustNotContain: null);

        ExpectSource(checks, "SRC-CHANGE-VIEW",
            "Change View resets cam priorities + returns home",
            "Assets/Scripts/CameraManager.cs",
            mustContain: new[] { "_homeCamIndex", "Priority = 0" },
            mustNotContain: null);
    }

    private static void RunPathLogicChecks(List<Check> checks)
    {
        try
        {
            string persisted = Application.persistentDataPath;
            Add(checks, "PATH-PERSISTENT", "Unity persistentDataPath resolves",
                string.IsNullOrWhiteSpace(persisted) ? "FAIL" : "PASS",
                persisted);

            string saved = Path.Combine(persisted, "Saved");
            string configs = Path.Combine(saved, "Configs");
            Add(checks, "PATH-SAVED-EXISTS", "Saved folder exists (created if missing)",
                "PASS",
                EnsureDir(saved));
            Add(checks, "PATH-CONFIGS-EXISTS", "Saved/Configs folder exists (created if missing)",
                "PASS",
                EnsureDir(configs));

            // Expected export layout helper
            string sampleRoom = "Verify_Sample_Room";
            string expectedObj = Path.Combine(persisted, sampleRoom, "OBJ", "Room");
            Add(checks, "PATH-OBJ-LAYOUT", "Expected OBJ export layout under AppData",
                "PASS",
                expectedObj + "  (created on export)");

            // Warn if legacy Documents export folder still has recent GLBs only
            string docsExports = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Operating Room Exports");
            if (Directory.Exists(docsExports))
            {
                var glbs = Directory.GetFiles(docsExports, "*.glb", SearchOption.AllDirectories);
                var objs = Directory.GetFiles(docsExports, "*.obj", SearchOption.AllDirectories);
                Add(checks, "PATH-LEGACY-DOCS",
                    "Legacy Documents/Operating Room Exports still present",
                    glbs.Length > 0 && objs.Length == 0 ? "WARN" : "PASS",
                    $"GLB={glbs.Length}, OBJ={objs.Length} under {docsExports}");
            }
            else
            {
                Add(checks, "PATH-LEGACY-DOCS",
                    "Legacy Documents/Operating Room Exports absent (good)",
                    "PASS",
                    "Not found");
            }
        }
        catch (Exception e)
        {
            Add(checks, "PATH-LOGIC", "Path logic checks threw", "FAIL", e.Message);
        }
    }

    private static void RunExportArtifactScan(List<Check> checks)
    {
        try
        {
            string persisted = Application.persistentDataPath;
            if (!Directory.Exists(persisted))
            {
                Add(checks, "ART-EXPORT-SCAN", "Scan AppData for latest room exports",
                    "SKIP", "persistentDataPath missing");
                return;
            }

            // Find newest .obj under persistentDataPath
            var objs = Directory.GetFiles(persisted, "*.obj", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(5)
                .ToList();
            var mtls = Directory.GetFiles(persisted, "*.mtl", SearchOption.AllDirectories);
            var glbs = Directory.GetFiles(persisted, "*.glb", SearchOption.AllDirectories);
            var pdfs = Directory.GetFiles(persisted, "*.pdf", SearchOption.AllDirectories);
            var pngs = Directory.GetFiles(persisted, "*.png", SearchOption.AllDirectories)
                .Where(p => p.IndexOf("snapshot", StringComparison.OrdinalIgnoreCase) >= 0
                            || p.IndexOf("HD_", StringComparison.OrdinalIgnoreCase) >= 0
                            || p.IndexOf("ExportedArm", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (objs.Count == 0)
            {
                Add(checks, "ART-OBJ", "OBJ artifacts under AppData",
                    "SKIP",
                    "No .obj found yet — run Export All in Play Mode, then re-verify.");
            }
            else
            {
                string newest = objs[0];
                bool underObjRoom = newest.Replace('\\', '/')
                    .IndexOf("/OBJ/Room/", StringComparison.OrdinalIgnoreCase) >= 0;
                string mtlBeside = Path.ChangeExtension(newest, ".mtl");
                bool hasMtl = File.Exists(mtlBeside);
                Add(checks, "ART-OBJ", "Newest OBJ under AppData",
                    underObjRoom && hasMtl ? "PASS" : "WARN",
                    $"{newest}\n         MTL beside OBJ: {hasMtl}; under OBJ/Room: {underObjRoom}");

                // Quick MTL content probe
                if (hasMtl)
                {
                    string mtlText = File.ReadAllText(mtlBeside);
                    bool hasMap = mtlText.IndexOf("map_Kd", StringComparison.OrdinalIgnoreCase) >= 0
                                  || mtlText.IndexOf("map_Ka", StringComparison.OrdinalIgnoreCase) >= 0;
                    Add(checks, "ART-MTL-MAP", "MTL references texture maps",
                        hasMap ? "PASS" : "WARN",
                        hasMap ? "Found map_Kd/map_Ka" : "No map_* entries — materials may be color-only");
                }
            }

            Add(checks, "ART-GLB-APPDATA", "GLB count under AppData (should trend down)",
                glbs.Length == 0 ? "PASS" : "WARN",
                $"GLB files: {glbs.Length}");

            Add(checks, "ART-PDF", "PDF artifacts under AppData",
                pdfs.Length > 0 ? "PASS" : "SKIP",
                pdfs.Length > 0
                    ? $"Found {pdfs.Length} PDF(s). Newest: {pdfs.OrderByDescending(File.GetLastWriteTimeUtc).First()}"
                    : "None yet — export elevations/proposal in Play Mode.");

            Add(checks, "ART-SNAPSHOTS", "Snapshot/PNG artifacts under AppData",
                pngs.Count > 0 ? "PASS" : "SKIP",
                pngs.Count > 0
                    ? $"Matched {pngs.Count} snapshot-like PNG(s)"
                    : "None yet — run snapshots / Export All.");

            // JSON saves
            string saved = Path.Combine(persisted, "Saved");
            int roomJson = Directory.Exists(saved)
                ? Directory.GetFiles(saved, "*.json", SearchOption.TopDirectoryOnly).Length
                : 0;
            int configJson = Directory.Exists(Path.Combine(saved, "Configs"))
                ? Directory.GetFiles(Path.Combine(saved, "Configs"), "*.json").Length
                : 0;
            Add(checks, "ART-JSON-SAVES", "JSON room/config saves present",
                roomJson + configJson > 0 ? "PASS" : "SKIP",
                $"Rooms={roomJson}, Configs={configJson} under {saved}");
        }
        catch (Exception e)
        {
            Add(checks, "ART-EXPORT-SCAN", "Export artifact scan threw", "FAIL", e.Message);
        }
    }

    private static void RunPricingReferenceHints(List<Check> checks)
    {
        // Optional: if Carlyn's comparison workbook is on Desktop, note expected totals.
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "2026 0715 - Sales Proposal Comparison.xlsx"),
            @"c:\Users\conno\Desktop\2026 0715 - Sales Proposal Comparison.xlsx"
        };

        string found = candidates.FirstOrDefault(File.Exists);
        if (found == null)
        {
            Add(checks, "PRICE-REF", "Estimating Form comparison workbook on Desktop",
                "SKIP",
                "Place '2026 0715 - Sales Proposal Comparison.xlsx' on Desktop for reference notes.");
            return;
        }

        Add(checks, "PRICE-REF", "Estimating Form comparison workbook found",
            "PASS",
            found);

        // Hardcoded expected list totals from the workbook (Config #1–3) for the manual section.
        Add(checks, "PRICE-TARGETS", "Known Estimating Form list targets (manual compare)",
            "PASS",
            "Config1=$106151.40 | Config2=$38190.13 | Config3=$74115.64 | Phone=214.987.0404");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void ExpectSource(
        List<Check> checks,
        string id,
        string name,
        string relativePath,
        string[] mustContain,
        string[] mustNotContain)
    {
        string full = Path.Combine(Directory.GetCurrentDirectory(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            Add(checks, id, name, "FAIL", $"Missing file: {relativePath}");
            return;
        }

        string text = File.ReadAllText(full);
        var missing = (mustContain ?? Array.Empty<string>())
            .Where(s => text.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0)
            .ToList();
        var forbidden = (mustNotContain ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrEmpty(s) && text.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        if (missing.Count == 0 && forbidden.Count == 0)
        {
            Add(checks, id, name, "PASS", relativePath);
            return;
        }

        var detail = new StringBuilder(relativePath);
        if (missing.Count > 0)
            detail.Append(" | missing: ").Append(string.Join(", ", missing.Select(m => $"'{Trim(m, 40)}'")));
        if (forbidden.Count > 0)
            detail.Append(" | forbidden still present: ").Append(string.Join(", ", forbidden.Select(m => $"'{Trim(m, 40)}'")));
        Add(checks, id, name, "FAIL", detail.ToString());
    }

    private static string Trim(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Add(List<Check> checks, string id, string name, string status, string detail)
    {
        checks.Add(new Check { Id = id, Name = name, Status = status, Detail = detail });
    }

    private static string GetManualChecklist()
    {
        return @"========== MANUAL REVIEW CHECKLIST ==========
Mark each item [x] when verified in Play Mode / on client machine.
Paste updates into chat or re-run verify after exports so ART-* flips PASS.

--- Save / Load ---
[ ] Save room → file lands in AppData\...\Saved\*.json (not Documents)
[ ] Quit app → reopen from Load UI → full room fidelity
[ ] Save config → lands in Saved\Configs\*.json
[ ] Quit app → add saved config from object menu (cold start)
[ ] Duplicate room/config works after reopen
[ ] GLB is not treated as a reopenable room save

--- Export All / CAD ---
[ ] Room Exports hub shows Export All first
[ ] Export All asks once for folder, then writes all deliverables
[ ] 3D export is .obj + .mtl (+ textures) under {Room}\OBJ\Room
[ ] No required .glb deliverable for Revit/Enscape path
[ ] OBJ opens in Blender with materials
[ ] Blender→Revit/Enscape materials/logos usable (not black/white/transparent)
[ ] Snapshots included; ceiling objects visible in top-down (ceiling mesh hidden)
[ ] Elevations PDF generated when booms present
[ ] Proposal PDF generated
[ ] Export does not hard-freeze (can cancel / completes)

--- PDF Elevations ---
[ ] Dimensions show metric (mm) on boom/light elevations
[ ] Floor line aligns with floor dimension reference (no floating/double line)
[ ] No overlapping / floating dimensions
[ ] Surgical light cutsheet dims originate at the light (not lower pivots)
[ ] Radial / horizontal dims look to-scale (no obvious ÷2)

--- Sales Proposal ---
[ ] Header phone = 214.987.0404
[ ] Sales rep name/phone/email present in header when set
[ ] Sales rep editable from proposal preview (header hotspot / side rail) — not a second Project-line label
[ ] Config1 list ≈ $106,151.40 (± small % vs Estimating Form)
[ ] Config2 list ≈ $38,190.13 (Spring boom — not Powered 1000mm)
[ ] Config3 list ≈ $74,115.64
[ ] After 50% discount, proposal totals still track Estimating Form
[ ] Compare against Desktop workbook: 2026 0715 - Sales Proposal Comparison.xlsx

--- Placement / UX ---
[ ] Doors flush with wall (vertical + orientation)
[ ] Base room shows surgical bed + 4 ceiling lights (new/empty)
[ ] U|ONE LEDs turn on with Light toggle (parity with U|002)
[ ] Client Data does NOT auto-pop on launch (Main Menu → Edit Account Data only)
[ ] Change View cycles and returns to FreeLook / original feel
[ ] Zoom in/out works in room ortho views
[ ] Boom arm materials cover full standard (non-XL) top arm length

--- After you finish ---
1) Run Tools → Operating Room → Milestone 2 Verify again
2) Send Tools/Reports/Milestone2_Verify_LATEST.txt in chat for agent review
";
    }
}
#endif
