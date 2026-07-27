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

    /// <summary>
    /// When true (default in Editor / Development builds), SetScaleLevel also dumps the
    /// full hierarchy. Length-isolation probes always log regardless of this flag.
    /// </summary>
    public static bool VerboseHierarchy
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        = true;
#else
        = false;
#endif

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

    /// <summary>
    /// Snapshot used to detect "length stretch" vs true length isolation:
    /// tube XY stable, tube Z / tip distance grow, downstream lossy stays ~1, diameter stable.
    /// </summary>
    public struct LengthProbe
    {
        public Vector3 TubeLocal;
        public Vector3 TubeLossy;
        public Vector3 SelfBoundsSize;
        public Vector3 SubtreeBoundsSize;
        public float SelfDiameter;
        public float SelfLength;
        public float TipDistance;
        public string FirstChildName;
        public Vector3 FirstChildLocal;
        public Vector3 FirstChildLossy;
        public string DownstreamName;
        public Vector3 DownstreamLossy;
        public int ChildCount;
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

    public static LengthProbe CaptureLengthProbe(Transform tube)
    {
        var probe = new LengthProbe();
        if (tube == null) return probe;

        probe.TubeLocal = tube.localScale;
        probe.TubeLossy = tube.lossyScale;
        probe.ChildCount = tube.childCount;
        // Mesh often lives on an untracked wrapper (.002), not the Selectable — use that
        // segment's renderers, stopping before the next Selectable (next arm / head).
        probe.SelfBoundsSize = ArmMeshBoundsSize(tube);
        probe.SubtreeBoundsSize = BoundsSizeOn(tube, includeChildren: true);
        SplitLengthDiameter(probe.SelfBoundsSize, out probe.SelfLength, out probe.SelfDiameter);
        probe.TipDistance = TipWorldDistance(tube);

        if (tube.childCount > 0)
        {
            Transform child = tube.GetChild(0);
            probe.FirstChildName = child.name;
            probe.FirstChildLocal = child.localScale;
            probe.FirstChildLossy = child.lossyScale;
        }

        Selectable down = FindFirstDownstreamSelectable(tube);
        if (down != null)
        {
            probe.DownstreamName = down.name;
            probe.DownstreamLossy = down.transform.lossyScale;
        }

        return probe;
    }

    /// <summary>
    /// Compare before/after SetScaleLevel and emit a one-line verdict that is easy to grep:
    /// LENGTH_OK | TUBE_XY_STRETCH | DIAMETER_STRETCH | DOWNSTREAM_STRETCH | MIXED_FAIL
    /// </summary>
    public static void LogLengthIsolation(
        string phase,
        Transform tube,
        LengthProbe before,
        float requestedScaleZ,
        float sizeMeters,
        bool setSelected,
        bool usedStoredChildScales,
        bool usedCalculateInverse)
    {
        if (tube == null) return;

        LengthProbe after = CaptureLengthProbe(tube);
        float eps = 0.04f;

        bool tubeXyStable =
            Mathf.Abs(after.TubeLocal.x - before.TubeLocal.x) <= eps
            && Mathf.Abs(after.TubeLocal.y - before.TubeLocal.y) <= eps;
        bool tubeZMatched = Mathf.Abs(after.TubeLocal.z - requestedScaleZ) <= 0.02f;

        bool diameterStable = before.SelfDiameter < 1e-5f
            || Mathf.Abs(after.SelfDiameter / Mathf.Max(before.SelfDiameter, 1e-6f) - 1f) <= 0.08f;

        float lengthRatio = before.SelfLength > 1e-5f
            ? after.SelfLength / before.SelfLength
            : 0f;
        float tipRatio = before.TipDistance > 1e-5f
            ? after.TipDistance / before.TipDistance
            : 0f;
        float zRatio = Mathf.Abs(before.TubeLocal.z) > 1e-5f
            ? after.TubeLocal.z / before.TubeLocal.z
            : requestedScaleZ;

        bool downstreamOk = true;
        if (!string.IsNullOrEmpty(after.DownstreamName))
        {
            // Downstream should stay near world-uniform 1 (isolation), not track tube Z.
            Vector3 d = after.DownstreamLossy;
            float dMax = Mathf.Max(Mathf.Abs(d.x), Mathf.Max(Mathf.Abs(d.y), Mathf.Abs(d.z)));
            float dMin = Mathf.Min(Mathf.Abs(d.x), Mathf.Min(Mathf.Abs(d.y), Mathf.Abs(d.z)));
            bool nearOne = Mathf.Abs(dMax - 1f) <= 0.12f && Mathf.Abs(dMin - 1f) <= 0.12f;
            bool grewWithTube = zRatio > 1.05f && dMax > Mathf.Max(AbsMax(before.DownstreamLossy) * 1.08f, 1.12f);
            downstreamOk = nearOne && !grewWithTube;
        }

        bool childInverseOk = true;
        if (!string.IsNullOrEmpty(after.FirstChildName) && Mathf.Abs(requestedScaleZ - 1f) > 0.05f)
        {
            // Direct mesh/AP child should compensate so its lossy stays ~1 when tube Z != 1.
            Vector3 cl = after.FirstChildLossy;
            float cMax = Mathf.Max(Mathf.Abs(cl.x), Mathf.Max(Mathf.Abs(cl.y), Mathf.Abs(cl.z)));
            childInverseOk = Mathf.Abs(cMax - 1f) <= 0.15f;
        }

        string verdict = "LENGTH_OK";
        if (!tubeXyStable) verdict = "TUBE_XY_STRETCH";
        else if (!diameterStable) verdict = "DIAMETER_STRETCH";
        else if (!downstreamOk) verdict = "DOWNSTREAM_STRETCH";
        else if (!childInverseOk) verdict = "CHILD_INVERSE_FAIL";
        else if (!tubeZMatched) verdict = "Z_MISMATCH";

        var sb = new StringBuilder(768);
        sb.Append("verdict=").Append(verdict);
        sb.Append(" name=").Append(tube.name);
        sb.Append(" sizeM=").Append(F(sizeMeters));
        sb.Append(" reqZ=").Append(F(requestedScaleZ));
        sb.Append(" setSelected=").Append(setSelected);
        sb.Append(" applyPath=");
        if (usedStoredChildScales && usedCalculateInverse) sb.Append("stored+worldPreserve");
        else if (usedStoredChildScales) sb.Append("storedChildScales");
        else if (usedCalculateInverse) sb.Append("worldPreserve");
        else sb.Append("none");

        sb.Append(" | tubeLocal ").Append(V(before.TubeLocal)).Append("->").Append(V(after.TubeLocal));
        sb.Append(" tubeLossy ").Append(V(before.TubeLossy)).Append("->").Append(V(after.TubeLossy));
        sb.Append(" tubeXyStable=").Append(tubeXyStable);
        sb.Append(" zMatched=").Append(tubeZMatched);

        sb.Append(" | selfBounds ").Append(V(before.SelfBoundsSize)).Append("->").Append(V(after.SelfBoundsSize));
        sb.Append(" selfLen ").Append(F(before.SelfLength)).Append("->").Append(F(after.SelfLength))
            .Append(" (x").Append(F(lengthRatio)).Append(')');
        sb.Append(" selfDiam ").Append(F(before.SelfDiameter)).Append("->").Append(F(after.SelfDiameter))
            .Append(" diamStable=").Append(diameterStable);

        sb.Append(" | tipDist ").Append(F(before.TipDistance)).Append("->").Append(F(after.TipDistance))
            .Append(" (x").Append(F(tipRatio)).Append(") zRatio=").Append(F(zRatio));

        if (!string.IsNullOrEmpty(after.FirstChildName))
        {
            sb.Append(" | child0=").Append(after.FirstChildName);
            sb.Append(" local ").Append(V(before.FirstChildLocal)).Append("->").Append(V(after.FirstChildLocal));
            sb.Append(" lossy ").Append(V(before.FirstChildLossy)).Append("->").Append(V(after.FirstChildLossy));
            sb.Append(" inverseOk=").Append(childInverseOk);
        }

        if (!string.IsNullOrEmpty(after.DownstreamName))
        {
            sb.Append(" | down=").Append(after.DownstreamName);
            sb.Append(" lossy ").Append(V(before.DownstreamLossy)).Append("->").Append(V(after.DownstreamLossy));
            sb.Append(" ok=").Append(downstreamOk);
        }

        if (tube.TryGetComponent(out Selectable sel) && sel.CurrentScaleLevel != null)
        {
            sb.Append(" | metaZ=").Append(F(sel.CurrentScaleLevel.ScaleZ))
              .Append(" metaSize=").Append(F(sel.CurrentScaleLevel.Size))
              .Append(" liveZ=").Append(F(after.TubeLocal.z))
              .Append(" metaDesync=")
              .Append(Mathf.Abs(sel.CurrentScaleLevel.ScaleZ - after.TubeLocal.z) > 0.05f);
        }

        if (verdict == "LENGTH_OK")
            Event(phase, sb.ToString());
        else
            Warn(phase, sb.ToString());
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

    private static Vector3 ArmMeshBoundsSize(Transform tube)
    {
        Bounds? merged = null;
        AccumulateRendererBounds(tube, ref merged, stopAtSelectableChildren: true);
        return merged.HasValue ? merged.Value.size : Vector3.zero;
    }

    private static Vector3 BoundsSizeOn(Transform root, bool includeChildren)
    {
        if (!includeChildren)
        {
            Bounds? self = null;
            AccumulateRendererBounds(root, ref self, stopAtSelectableChildren: false, selfOnly: true);
            return self.HasValue ? self.Value.size : Vector3.zero;
        }

        Bounds? merged = null;
        AccumulateRendererBounds(root, ref merged, stopAtSelectableChildren: false);
        return merged.HasValue ? merged.Value.size : Vector3.zero;
    }

    private static void AccumulateRendererBounds(
        Transform root,
        ref Bounds? merged,
        bool stopAtSelectableChildren,
        bool selfOnly = false)
    {
        if (root == null) return;

        Renderer[] selfRenderers = root.GetComponents<Renderer>();
        for (int i = 0; i < selfRenderers.Length; i++)
            EncapsulateIfUseful(selfRenderers[i], ref merged);

        if (selfOnly) return;

        for (int c = 0; c < root.childCount; c++)
        {
            Transform child = root.GetChild(c);
            if (child == null) continue;
            if (stopAtSelectableChildren && child.GetComponent<Selectable>() != null)
                continue;
            AccumulateRendererBounds(child, ref merged, stopAtSelectableChildren, selfOnly: false);
        }
    }

    private static void EncapsulateIfUseful(Renderer r, ref Bounds? merged)
    {
        if (r == null || !r.enabled) return;
        // Skip tiny logo/measurement quads — they skew diameter metrics.
        if (r.name.IndexOf("Logo", StringComparison.OrdinalIgnoreCase) >= 0) return;
        if (r.name.IndexOf("Measurement", StringComparison.OrdinalIgnoreCase) >= 0) return;
        if (r.name.IndexOf("Quad", StringComparison.OrdinalIgnoreCase) >= 0
            && r.bounds.size.sqrMagnitude < 0.05f)
            return;

        if (merged == null) merged = r.bounds;
        else
        {
            Bounds b = merged.Value;
            b.Encapsulate(r.bounds);
            merged = b;
        }
    }

    private static void SplitLengthDiameter(Vector3 size, out float length, out float diameter)
    {
        float ax = Mathf.Abs(size.x), ay = Mathf.Abs(size.y), az = Mathf.Abs(size.z);
        length = Mathf.Max(ax, Mathf.Max(ay, az));
        // Diameter = mean of the two axes that are NOT the length axis.
        // (Previously used "two non-shortest", which wrongly folded length into diameter
        // and false-alarmed DIAMETER_STRETCH whenever the arm lengthened.)
        if (ax >= ay && ax >= az) diameter = 0.5f * (ay + az);
        else if (ay >= ax && ay >= az) diameter = 0.5f * (ax + az);
        else diameter = 0.5f * (ax + ay);
        if (length < 1e-6f) diameter = 0f;
    }

    private static float TipWorldDistance(Transform tube)
    {
        if (tube == null) return 0f;
        Vector3 origin = tube.position;
        float best = 0f;

        // Prefer attachment / light / next-arm tips under this tube.
        Transform[] all = tube.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t == tube) continue;
            string n = t.name;
            bool tipLike =
                n.IndexOf("AttachPoint", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("LightHead", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ArmSegment_2", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("BoomHead", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!tipLike) continue;
            float d = Vector3.Distance(origin, t.position);
            if (d > best) best = d;
        }

        if (best > 1e-5f) return best;

        // Fallback: farthest renderer corner from tube origin.
        Renderer[] renderers = tube.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            Vector3 c = r.bounds.center;
            float d = Vector3.Distance(origin, c) + r.bounds.extents.magnitude;
            if (d > best) best = d;
        }
        return best;
    }

    private static Selectable FindFirstDownstreamSelectable(Transform tube)
    {
        if (tube == null) return null;
        Selectable[] sels = tube.GetComponentsInChildren<Selectable>(true);
        for (int i = 0; i < sels.Length; i++)
        {
            Selectable s = sels[i];
            if (s == null || s.transform == tube) continue;
            return s;
        }
        return null;
    }

    private static float AbsMax(Vector3 v) =>
        Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));

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
                $"verboseHierarchy={VerboseHierarchy} path={LogPath} ==========";
            Write(banner);
            Debug.Log($"[SCALE_AUDIT] Logging to {LogPath} (length probes always on; hierarchy verbose={VerboseHierarchy})");
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
