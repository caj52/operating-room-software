using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Focused always-on dump for service-head mount scale
/// (<c>Logs/rail_scale.log</c>). Includes mesh-child scales so import
/// corrections (Rear Rail 0.01) are visible when tube isolation tries to wipe them.
/// </summary>
public static class RailScaleDiag
{
    private static readonly object Gate = new object();
    private static string _logPath;

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
            _logPath = Path.Combine(dir, "rail_scale.log");
            return _logPath;
        }
    }

    public static void Event(string phase, string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{phase}] {message}";
        lock (Gate)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine); }
            catch { /* ignore IO */ }
        }
        Debug.Log($"[RAIL_SCALE] [{phase}] {message}");
    }

    public static void Dump(string phase, Selectable sel, string extra = null)
    {
        if (sel == null) return;
        Transform t = sel.transform;
        var probe = ScaleAuditLog.CaptureLengthProbe(t);
        float size = sel.CurrentScaleLevel != null ? sel.CurrentScaleLevel.Size : 0f;
        float scaleZ = sel.CurrentScaleLevel != null ? sel.CurrentScaleLevel.ScaleZ : 0f;
        LengthScaleKind kind = sel.GetLengthScaleKind();

        var sb = new StringBuilder();
        sb.Append($"name={sel.name} kind={kind} path={GetPath(t)} local={t.localScale} lossy={t.lossyScale} ");
        sb.Append($"size={size:G6} scaleZ={scaleZ:G6} selfLen={probe.SelfLength:G6} ");
        sb.Append($"selfBounds={probe.SelfBoundsSize} childCount={t.childCount}");
        if (!string.IsNullOrEmpty(extra))
            sb.Append(' ').Append(extra);

        for (int i = 0; i < t.childCount && i < 8; i++)
        {
            Transform c = t.GetChild(i);
            if (c == null) continue;
            sb.Append($" | child[{i}]={c.name} local={c.localScale}");
        }

        Event(phase, sb.ToString());

        // ROOM_SPAN: either root lossy huge OR mesh child was wiped to 1 under a huge mesh.
        bool meshChildWiped = false;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform c = t.GetChild(i);
            if (c == null || c.GetComponent<Selectable>() != null) continue;
            if (c.GetComponent<AttachmentPoint>() != null) continue;
            if (c.GetComponent<MeshFilter>() == null) continue;
            if (Mathf.Abs(c.localScale.x - 1f) < 0.001f
                && Mathf.Abs(c.localScale.y - 1f) < 0.001f
                && Mathf.Abs(c.localScale.z - 1f) < 0.001f
                && probe.SelfLength > 2f)
            {
                meshChildWiped = true;
                break;
            }
        }

        if (probe.SelfLength > 5f || Mathf.Abs(t.lossyScale.x) > 5f || meshChildWiped)
            Event(phase + ".ALERT",
                $"ROOM_SPAN_RISK selfLen={probe.SelfLength:G6} lossy={t.lossyScale} meshChildWiped={meshChildWiped}");
    }

    static string GetPath(Transform t)
    {
        if (t == null) return "(null)";
        string path = t.name;
        Transform p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 24)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }
}
