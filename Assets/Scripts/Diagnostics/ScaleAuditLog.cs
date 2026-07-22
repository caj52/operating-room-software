using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Extremely detailed scale diagnostics. Writes to Unity console AND
/// <c>Logs/scale_audit.log</c> at the project root so agents can read the dump
/// after a play session without scraping Editor.log.
/// </summary>
public static class ScaleAuditLog
{
    private static readonly object Gate = new object();
    private static string _logPath;
    private static bool _sessionStarted;

    public static string LogPath
    {
        get
        {
            if (_logPath != null) return _logPath;
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? Application.dataPath;
            string dir = Path.Combine(projectRoot, "Logs");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "scale_audit.log");
            return _logPath;
        }
    }

    public static void Event(string phase, string message)
    {
        EnsureSession();
        string line = $"[{Now()}] [{phase}] {message}";
        Write(line);
        Debug.Log($"[SCALE_AUDIT] [{phase}] {message}");
    }

    public static void Warn(string phase, string message)
    {
        EnsureSession();
        string line = $"[{Now()}] [{phase}] WARN {message}";
        Write(line);
        Debug.LogWarning($"[SCALE_AUDIT] [{phase}] {message}");
    }

    /// <summary>Dump one transform's local/lossy scale, parent chain, and flags.</summary>
    public static void Node(string phase, Transform t, string note = null)
    {
        if (t == null)
        {
            Event(phase, "node=null");
            return;
        }

        EnsureSession();
        var sb = new StringBuilder(512);
        sb.Append(DescribeNode(t));
        if (!string.IsNullOrEmpty(note))
            sb.Append(" | ").Append(note);
        Event(phase, sb.ToString());
    }

    /// <summary>Full hierarchy dump for a root (relevant nodes + every non-uniform scale).</summary>
    public static void Hierarchy(string phase, Transform root, string label = null)
    {
        if (root == null)
        {
            Event(phase, "hierarchy root=null");
            return;
        }

        EnsureSession();
        var sb = new StringBuilder(4096);
        sb.Append("HIERARCHY");
        if (!string.IsNullOrEmpty(label))
            sb.Append(" label=").Append(label);
        sb.Append(" root=").Append(PathOf(root));
        sb.AppendLine();

        int dumped = 0;
        int flagged = 0;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            bool relevant = IsScaleRelevant(t);
            bool nonUniform = IsNonUniform(t.localScale) || IsNonUniform(t.lossyScale);
            bool skewSuspect = IsSkewSuspect(t);
            if (!relevant && !nonUniform && !skewSuspect)
                continue;

            dumped++;
            if (skewSuspect) flagged++;
            sb.Append("  ");
            if (skewSuspect) sb.Append("!!SKEW!! ");
            sb.Append(DescribeNode(t));
            sb.AppendLine();
        }

        sb.Append("  summary dumped=").Append(dumped)
            .Append(" skewSuspects=").Append(flagged);
        Write($"[{Now()}] [{phase}] {sb}");
        Debug.Log($"[SCALE_AUDIT] [{phase}] hierarchy label={label} root={root.name} dumped={dumped} skewSuspects={flagged} (see {LogPath})");
    }

    /// <summary>Side-by-side compare of matching paths under two roots (e.g. original vs duplicate).</summary>
    public static void CompareHierarchies(string phase, Transform a, Transform b, string labelA = "A", string labelB = "B")
    {
        if (a == null || b == null)
        {
            Event(phase, $"compare aborted a={(a != null)} b={(b != null)}");
            return;
        }

        EnsureSession();
        var mapA = BuildPathMap(a);
        var mapB = BuildPathMap(b);
        var paths = new HashSet<string>(mapA.Keys);
        paths.UnionWith(mapB.Keys);

        var sb = new StringBuilder(8192);
        sb.Append("COMPARE ").Append(labelA).Append(" vs ").Append(labelB)
            .Append(" aRoot=").Append(PathOf(a))
            .Append(" bRoot=").Append(PathOf(b))
            .AppendLine();

        int mismatches = 0;
        int checkedPairs = 0;
        foreach (string path in paths)
        {
            mapA.TryGetValue(path, out Transform ta);
            mapB.TryGetValue(path, out Transform tb);
            if (ta == null || tb == null)
            {
                mismatches++;
                sb.Append("  MISSING path=").Append(path)
                    .Append(" inA=").Append(ta != null)
                    .Append(" inB=").Append(tb != null)
                    .AppendLine();
                continue;
            }

            bool relevant = IsScaleRelevant(ta) || IsScaleRelevant(tb)
                || IsNonUniform(ta.localScale) || IsNonUniform(tb.localScale)
                || IsNonUniform(ta.lossyScale) || IsNonUniform(tb.lossyScale);
            if (!relevant)
                continue;

            checkedPairs++;
            Vector3 la = ta.localScale, lb = tb.localScale;
            Vector3 wa = ta.lossyScale, wb = tb.lossyScale;
            bool localDiff = !Approx(la, lb);
            bool lossyDiff = !Approx(wa, wb);
            if (!localDiff && !lossyDiff)
                continue;

            mismatches++;
            sb.Append("  MISMATCH path=").Append(path).AppendLine();
            sb.Append("    ").Append(labelA).Append(" local=").Append(V(la))
                .Append(" lossy=").Append(V(wa))
                .Append(" parent=").Append(ParentName(ta)).AppendLine();
            sb.Append("    ").Append(labelB).Append(" local=").Append(V(lb))
                .Append(" lossy=").Append(V(wb))
                .Append(" parent=").Append(ParentName(tb)).AppendLine();
            sb.Append("    deltaLocal=").Append(V(lb - la))
                .Append(" deltaLossy=").Append(V(wb - wa)).AppendLine();
        }

        sb.Append("  summary checkedRelevant=").Append(checkedPairs)
            .Append(" mismatches=").Append(mismatches);
        Write($"[{Now()}] [{phase}] {sb}");
        Debug.Log($"[SCALE_AUDIT] [{phase}] compare {labelA} vs {labelB}: mismatches={mismatches} (see {LogPath})");
    }

    public static void ReparentBegin(string phase, Transform self, Transform newParent, Vector3 capturedWorldScale)
    {
        Node(phase + ".before", self,
            $"newParent={(newParent != null ? PathOf(newParent) : "null")} " +
            $"capturedWorldScale={V(capturedWorldScale)} " +
            $"oldParent={ParentName(self)}");
        if (newParent != null)
            Node(phase + ".newParent", newParent, "destination parent scales");
    }

    public static void ReparentEnd(string phase, Transform self, Vector3 targetWorldScale)
    {
        Vector3 got = self.lossyScale;
        bool ok = Approx(got, targetWorldScale, 0.01f);
        string note = $"targetWorld={V(targetWorldScale)} gotLossy={V(got)} match={ok} " +
                      $"localAfter={V(self.localScale)} parent={ParentName(self)}";
        if (!ok)
            Warn(phase + ".after", DescribeNode(self) + " | " + note);
        else
            Node(phase + ".after", self, note);
    }

    private static void EnsureSession()
    {
        if (_sessionStarted) return;
        lock (Gate)
        {
            if (_sessionStarted) return;
            _sessionStarted = true;
            string banner =
                $"========== SCALE AUDIT SESSION {DateTime.Now:yyyy-MM-dd HH:mm:ss} " +
                $"unity={Application.unityVersion} product={Application.productName} " +
                $"path={LogPath} ==========";
            Write(banner);
            Debug.Log($"[SCALE_AUDIT] Logging to {LogPath}");
        }
    }

    private static void Write(string line)
    {
        lock (Gate)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SCALE_AUDIT] Failed writing log: {ex.Message}");
            }
        }
    }

    private static string Now() => DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static string DescribeNode(Transform t)
    {
        var sb = new StringBuilder(256);
        sb.Append("path=").Append(PathOf(t));
        sb.Append(" active=").Append(t.gameObject.activeInHierarchy);
        sb.Append(" local=").Append(V(t.localScale));
        sb.Append(" lossy=").Append(V(t.lossyScale));
        sb.Append(" parent=").Append(ParentName(t));
        if (t.parent != null)
            sb.Append(" parentLocal=").Append(V(t.parent.localScale))
              .Append(" parentLossy=").Append(V(t.parent.lossyScale));

        if (t.TryGetComponent(out AttachmentPoint ap))
        {
            sb.Append(" AP(moveUp=").Append(ap.MoveUpOnAttach)
              .Append(" origParent=").Append(ap._originalParent != null ? ap._originalParent.name : "null")
              .Append(')');
        }

        if (t.TryGetComponent(out Selectable sel))
        {
            sb.Append(" Sel(dup=").Append(sel.isDuplicated);
            if (sel.CurrentScaleLevel != null)
                sb.Append(" scaleZ=").Append(F(sel.CurrentScaleLevel.ScaleZ))
                  .Append(" size=").Append(sel.CurrentScaleLevel.Size);
            sb.Append(')');
        }

        if (t.TryGetComponent(out TrackedObject tracked) && tracked.HasStoredValues)
            sb.Append(" savedLocal=").Append(V(tracked.data.localScale));

        if (IsSkewSuspect(t))
            sb.Append(" SKEW_RATIO=").Append(F(SkewRatio(t.lossyScale)));

        return sb.ToString();
    }

    private static bool IsScaleRelevant(Transform t)
    {
        string n = t.name;
        if (n.IndexOf("ArmDropTube", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("BoomDropTube", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("DropTube", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("AttachmentPoint", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.Equals("AttachPoint", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.IndexOf("ArmSegment", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Cardanic", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("Simeon_Light", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("LightHead", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (n.IndexOf("BoomHead", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (t.GetComponent<AttachmentPoint>() != null) return true;
        if (t.GetComponent<Selectable>() != null && t.GetComponent<Selectable>().ScaleLevels != null
            && t.GetComponent<Selectable>().ScaleLevels.Count > 0)
            return true;
        return false;
    }

    private static bool IsNonUniform(Vector3 s) =>
        Mathf.Abs(s.x - s.y) > 0.02f || Mathf.Abs(s.y - s.z) > 0.02f || Mathf.Abs(s.x - s.z) > 0.02f;

    private static bool IsSkewSuspect(Transform t)
    {
        Vector3 w = t.lossyScale;
        if (w.sqrMagnitude < 1e-8f) return true;
        return SkewRatio(w) > 1.25f;
    }

    private static float SkewRatio(Vector3 s)
    {
        float ax = Mathf.Abs(s.x), ay = Mathf.Abs(s.y), az = Mathf.Abs(s.z);
        float min = Mathf.Min(ax, Mathf.Min(ay, az));
        float max = Mathf.Max(ax, Mathf.Max(ay, az));
        if (min < 1e-6f) return 999f;
        return max / min;
    }

    private static Dictionary<string, Transform> BuildPathMap(Transform root)
    {
        var map = new Dictionary<string, Transform>(256);
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null) continue;
            string rel = RelativePath(root, t);
            if (!map.ContainsKey(rel))
                map[rel] = t;
        }
        return map;
    }

    private static string RelativePath(Transform root, Transform t)
    {
        if (t == root) return ".";
        var parts = new List<string>();
        Transform cur = t;
        while (cur != null && cur != root)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string PathOf(Transform t)
    {
        if (t == null) return "null";
        var parts = new List<string>();
        Transform cur = t;
        int guard = 0;
        while (cur != null && guard++ < 64)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string ParentName(Transform t) =>
        t != null && t.parent != null ? t.parent.name : "(none)";

    private static bool Approx(Vector3 a, Vector3 b, float eps = 0.001f) =>
        Mathf.Abs(a.x - b.x) <= eps && Mathf.Abs(a.y - b.y) <= eps && Mathf.Abs(a.z - b.z) <= eps;

    private static string V(Vector3 v) =>
        $"({F(v.x)},{F(v.y)},{F(v.z)})";

    private static string F(float f) =>
        f.ToString("G6", CultureInfo.InvariantCulture);
}
