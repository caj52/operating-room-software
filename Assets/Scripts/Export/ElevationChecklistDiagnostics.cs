using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Post-capture checklist: honest DRAW/MISSING per curated dim + SUMMARY.
/// </summary>
public static class ElevationChecklistDiagnostics
{
    public struct FrameMetrics
    {
        public bool Valid;
        public float MPerPxX;
        public float MPerPxY;
        public float RoomH;
        public float WorldW;
        public float Aniso;
        public int CropW;
        public int CropH;
        public int RtW;
        public int RtH;
        public float FloorY;
        public float CeilingY;
        public float FillX;
        public float FillY;
    }

    public static FrameMetrics LastFrame;

    static Dictionary<Selectable, (Measurable measurable, float sizeM)> _stashedLengths;
    static Dictionary<Selectable, Measurable> _stashedFloors;

    const float ScaleErrFailPct = 0.05f;
    const float TickOutsideFailM = 0.025f;
    const float FloorSnapFailM = 0.03f;
    const float SpanMatchTolM = 0.02f;
    const float FillFailBelow = 0.55f;

    public static void StashForScore(
        Dictionary<Selectable, (Measurable measurable, float sizeM)> lengthByOwner,
        Dictionary<Selectable, Measurable> floorBySource)
    {
        _stashedLengths = lengthByOwner;
        _stashedFloors = floorBySource;
    }

    public static void ScoreStashed(Camera camera)
    {
        _stashedLengths = null;
        _stashedFloors = null;
    }

    public static void RecordFrame(
        float mPerPxX, float mPerPxY, float roomH, float worldW, float aniso,
        int cropW, int cropH, int rtW, int rtH, float floorY, float ceilingY,
        float orthoSize = 0f, float cameraAspect = 1f)
    {
        // Sheet = cropped elevation image (floor–ceiling Y, assembly X). Square ortho
        // view has empty margin that ReadPixels discards — do not score ortho packing.
        float cropWorldW = mPerPxX * Mathf.Max(1, cropW);
        float cropWorldH = mPerPxY * Mathf.Max(1, cropH);
        float fillX = cropWorldW > 1e-4f ? Mathf.Clamp01(worldW / cropWorldW) : 0f;
        float fillY = cropWorldH > 1e-4f ? Mathf.Clamp01(roomH / cropWorldH) : 0f;
        LastFrame = new FrameMetrics
        {
            Valid = true,
            MPerPxX = mPerPxX,
            MPerPxY = mPerPxY,
            RoomH = roomH,
            WorldW = worldW,
            Aniso = aniso,
            CropW = cropW,
            CropH = cropH,
            RtW = rtW,
            RtH = rtH,
            FloorY = floorY,
            CeilingY = ceilingY,
            FillX = fillX,
            FillY = fillY,
        };
    }

    public static void LogAfterCutsheetPass(
        Camera camera,
        Dictionary<Selectable, (Measurable measurable, float sizeM)> lengthByOwner,
        Dictionary<Selectable, Measurable> floorBySource)
    {
        var sb = new StringBuilder(2048);
        sb.Append("[ElevDim] CHECKLIST");

        var expected = new HashSet<string>();
        foreach (var sel in ElevationDimCurator.CollectCatalogTableOwners())
        {
            if (sel == null) continue;
            if (!ElevationDimPolicy.TryResolveCatalogLengthClaim(sel, out var claim))
                continue;
            expected.Add($"{StripClone(claim.Owner.name)}@{Mathf.RoundToInt(claim.LengthM * 1000f)}");
        }

        // Pass 1: collect drawn features (STOLEN must only compare drawn peers).
        var drewKeys = new HashSet<string>();
        var drawn = new List<(Selectable owner, float catalog, Vector3 a, Vector3 b, bool vertical)>();
        int missingCount = 0;

        if (lengthByOwner != null)
        {
            foreach (var kv in lengthByOwner.OrderByDescending(k => k.Value.sizeM))
            {
                var owner = kv.Key;
                var measurable = kv.Value.measurable;
                float catalog = kv.Value.sizeM;
                if (owner == null || measurable == null || catalog <= 0f)
                    continue;

                if (!TryGetLengthFeatures(measurable, out Vector3 a, out Vector3 b, out bool vertical))
                {
                    missingCount++;
                    sb.Append("\n  LEN ").Append(owner.name)
                      .Append(" mm=").Append(Mathf.RoundToInt(catalog * 1000f))
                      .Append(" decision=MISSING reason=noActiveFeatures");
                    continue;
                }

                drewKeys.Add($"{StripClone(owner.name)}@{Mathf.RoundToInt(catalog * 1000f)}");
                drawn.Add((owner, catalog, a, b, vertical));
            }
        }

        int hScaleFail = 0, hScalePass = 0;
        int matchFail = missingCount, matchPass = 0;
        int tickFail = missingCount, tickPass = 0;
        int ceilTickFail = 0, ceilTickPass = 0;

        // Pass 2: score drawn dims against each other.
        foreach (var row in drawn)
        {
            var owner = row.owner;
            float catalog = row.catalog;
            Vector3 a = row.a;
            Vector3 b = row.b;
            bool vertical = row.vertical;

            float worldSpan = Vector3.Distance(a, b);
            float spanErrMm = Mathf.Abs(worldSpan - catalog) * 1000f;
            bool spanOk = spanErrMm <= SpanMatchTolM * 1000f;
            if (spanOk) matchPass++; else matchFail++;

            float distA = DistToOwnerMesh(a, owner, catalog);
            float distB = DistToOwnerMesh(b, owner, catalog);
            bool ticksOk = distA <= TickOutsideFailM && distB <= TickOutsideFailM;

            Selectable nearerOther = null;
            float nearerOtherDist = float.MaxValue;
            foreach (var other in drawn)
            {
                if (other.owner == null || other.owner == owner) continue;
                float d = Mathf.Min(
                    DistToOwnerMesh(a, other.owner, other.catalog),
                    DistToOwnerMesh(b, other.owner, other.catalog));
                if (d < nearerOtherDist)
                {
                    nearerOtherDist = d;
                    nearerOther = other.owner;
                }
            }

            bool stolen = nearerOther != null
                && nearerOtherDist + 0.01f < Mathf.Min(distA, distB);
            if (ticksOk && !stolen) tickPass++; else tickFail++;

            float topY = Mathf.Max(a.y, b.y);
            float ceilY = LastFrame.Valid ? LastFrame.CeilingY : ElevationRoomFrame.Current.CeilingY;
            float gapMm = (ceilY - topY) * 1000f;
            bool isCeilingTube = Measurable.IsDropTubeName(owner.name)
                || Measurable.IsVerticalHangLengthName(owner.name);
            string ceilBit = "";
            if (isCeilingTube)
            {
                // Top tick on room ceiling OR on the tandem/cover plate top.
                bool ceilOk = Mathf.Abs(topY - ceilY) <= TickOutsideFailM;
                if (!ceilOk
                    && ElevationLengthGeometry.TryGetCeilingPlateBand(
                        a.x, a.z, ceilY, out float plateTop, out _)
                    && Mathf.Abs(topY - plateTop) <= TickOutsideFailM)
                    ceilOk = true;
                if (ceilOk) ceilTickPass++; else ceilTickFail++;
                ceilBit = $" topY={topY:F3} ceilY={ceilY:F3} gapMm={gapMm:F0}→" +
                          (ceilOk ? "ceilOK" : "ceilFAIL");
            }

            string scaleBit = "";
            if (LastFrame.Valid && camera != null)
            {
                Vector3 sa = camera.WorldToScreenPoint(a);
                Vector3 sbPt = camera.WorldToScreenPoint(b);
                float actualPx = Vector2.Distance(
                    new Vector2(sa.x, sa.y), new Vector2(sbPt.x, sbPt.y));
                float mPerPx = vertical ? LastFrame.MPerPxY : LastFrame.MPerPxX;
                // Scale check: drawn world span vs pixels (not catalog — foreshortening /
                // keepMesh(short) are scored under numMatch / pageRatio instead).
                float expectPx = mPerPx > 1e-9f ? worldSpan / mPerPx : 0f;
                float errPct = expectPx > 1f ? Mathf.Abs(actualPx - expectPx) / expectPx : 999f;
                bool scaleOk = errPct <= ScaleErrFailPct;
                if (!vertical)
                {
                    if (scaleOk) hScalePass++; else hScaleFail++;
                }
                float pageRatio = catalog > 1e-6f
                    ? Vector3.ProjectOnPlane(b - a, camera.transform.forward).magnitude / catalog
                    : 0f;
                scaleBit =
                    $" screenPx={actualPx:F0} expect={expectPx:F0} err%={errPct * 100f:F1}→" +
                    (scaleOk ? "PASS" : "FAIL") +
                    $" pageRatio={pageRatio:F2}";
            }

            sb.Append("\n  LEN ").Append(owner.name)
              .Append(" mm=").Append(Mathf.RoundToInt(catalog * 1000f))
              .Append(vertical ? " V" : " H")
              .Append(" decision=DRAW")
              .Append(" spanMm=").Append((worldSpan * 1000f).ToString("F0"))
              .Append(" errMm=").Append(spanErrMm.ToString("F0"))
              .Append(spanOk ? "→spanOK" : "→spanBAD")
              .Append(" tickA=").Append((distA * 1000f).ToString("F0"))
              .Append("mm tickB=").Append((distB * 1000f).ToString("F0"))
              .Append("mm")
              .Append(ticksOk ? "→ticksOK" : "→ticksBAD")
              .Append(stolen ? " STOLEN→" + nearerOther.name : "")
              .Append(ceilBit)
              .Append(scaleBit);
        }

        int floorFail = 0, floorPass = 0;
        if (floorBySource != null)
        {
            foreach (var kv in floorBySource)
            {
                var m = kv.Value;
                if (m?.Measurements == null) continue;
                foreach (var item in m.Measurements)
                {
                    if (item == null || item.MeasurementType != MeasurementType.Floor)
                        continue;
                    if (item.Measurer == null || !item.Measurer.gameObject.activeSelf)
                        continue;
                    float floorY = item.HitPoint.y;
                    float underside = item.Origin.y;
                    float roomFloor = LastFrame.Valid ? LastFrame.FloorY : ElevationRoomFrame.Current.FloorY;
                    float err = Mathf.Abs(floorY - roomFloor);
                    bool ok = err <= FloorSnapFailM;
                    if (ok) floorPass++; else floorFail++;
                    sb.Append("\n  FLR ").Append(kv.Key != null ? kv.Key.name : "?")
                      .Append(" undersideMm=").Append((underside * 1000f).ToString("F0"))
                      .Append(" hitY=").Append(floorY.ToString("F3"))
                      .Append(ok ? "→PASS" : "→FAIL");
                }
            }
        }

        var missing = expected.Where(e => !drewKeys.Any(d => NamesMatch(e, d))).ToList();
        var extra = drewKeys.Where(d => !expected.Any(e => NamesMatch(e, d))).ToList();

        sb.Append("\n  SUMMARY")
          .Append(" fill=").Append(LastFrame.Valid
              ? (Mathf.Min(LastFrame.FillX, LastFrame.FillY) >= FillFailBelow ? "PASS" : "FAIL")
              : "?")
          .Append(" hScale=").Append(FormatPass(hScalePass, hScaleFail))
          .Append(" ceilScale=").Append(LastFrame.Valid
              && Mathf.Abs(LastFrame.CropH * LastFrame.MPerPxY - LastFrame.RoomH) <= 0.01f
              && Mathf.Abs(LastFrame.Aniso - 1f) <= 0.03f
                  ? "PASS" : "FAIL")
          .Append(" numMatch=").Append(FormatPass(matchPass, matchFail))
          .Append(" ticks=").Append(FormatPass(tickPass, tickFail))
          .Append(" ceilTicks=").Append(FormatPass(ceilTickPass, ceilTickFail))
          .Append(" floor=").Append(FormatPass(floorPass, floorFail))
          .Append(" missing=[").Append(string.Join(",", missing)).Append(']')
          .Append(" extra=[").Append(string.Join(",", extra)).Append(']');
    }

    static string FormatPass(int ok, int bad)
    {
        if (ok == 0 && bad == 0) return "?";
        return (bad == 0 ? "PASS" : "FAIL") + $"({ok}ok/{bad}bad)";
    }

    static bool TryGetLengthFeatures(
        Measurable measurable, out Vector3 a, out Vector3 b, out bool vertical)
    {
        a = b = default;
        vertical = measurable.CutsheetLengthIsVertical;
        if (measurable.Measurements == null)
            return false;
        foreach (var item in measurable.Measurements)
        {
            if (item == null || item.MeasurementType != MeasurementType.ToArmAssemblyOrigin)
                continue;
            var mr = item.Measurer;
            if (mr == null || !mr.gameObject.activeSelf)
                continue;
            if (mr.ElevationLeadersValid)
            {
                a = mr.ElevationLeaderFeatureA;
                b = mr.ElevationLeaderFeatureB;
                return true;
            }
            a = item.Origin;
            b = item.HitPoint;
            return true;
        }
        return false;
    }

    static float DistToOwnerMesh(Vector3 p, Selectable owner, float catalog)
    {
        if (owner == null)
            return float.MaxValue;

        float best = float.MaxValue;

        void ConsiderBounds(Bounds b)
        {
            if (b.size.sqrMagnitude < 1e-6f)
                return;
            float d = b.Contains(p) ? 0f : Vector3.Distance(p, b.ClosestPoint(p));
            if (d < best)
                best = d;
        }

        void ConsiderPoint(Vector3 q)
        {
            float d = Vector3.Distance(p, q);
            if (d < best)
                best = d;
        }

        if (ElevationLengthGeometry.TryGetLengthMeshBounds(owner, catalog, out Bounds mesh, out _))
            ConsiderBounds(mesh);
        else if (Measurable.TryGetOwnRendererBounds(owner, out Bounds own))
            ConsiderBounds(own);

        // Claim volume: owner + non-catalog descendant shells (PoweredXL elbow _2_3, etc.)
        if (ElevationLengthGeometry.TryGetClaimPartBounds(owner, out Bounds claimVol))
            ConsiderBounds(claimVol);

        // Drawn claim endpoints (solver output).
        if (ElevationLengthGeometry.TryGetRememberedClaimEndpoints(owner, out Vector3 ca, out Vector3 cb))
        {
            ConsiderPoint(ca);
            ConsiderPoint(cb);
        }

        // Joint APs are the length-claim endpoints — shared joints must score as on-part.
        if (ElevationLengthGeometry.TryResolveProximalAttachment(owner, out var prox) && prox != null)
        {
            ConsiderPoint(prox.transform.position);
            var distal = ElevationLengthGeometry.FindDistalAttachmentPoint(owner, prox);
            if (distal != null)
                ConsiderPoint(distal.transform.position);
        }

        // Ceiling-mount dims: top tick seats on the ceiling plane (architect review).
        if (Measurable.IsDropTubeName(owner.name))
        {
            float ceilY = LastFrame.Valid ? LastFrame.CeilingY : ElevationRoomFrame.Current.CeilingY;
            float mx = owner.transform.position.x;
            float mz = owner.transform.position.z;
            if (owner.ParentAttachmentPoint != null)
            {
                mx = owner.ParentAttachmentPoint.transform.position.x;
                mz = owner.ParentAttachmentPoint.transform.position.z;
            }
            ConsiderPoint(new Vector3(mx, ceilY, mz));
            ConsiderPoint(new Vector3(mx, ceilY - catalog, mz));
            if (ElevationLengthGeometry.TryGetCeilingPlateBand(
                    mx, mz, ceilY, out float plateTop, out float plateUnder))
            {
                ConsiderPoint(new Vector3(mx, plateTop, mz));
                ConsiderPoint(new Vector3(mx, plateUnder, mz));
            }
        }

        if (best < float.MaxValue)
            return best;
        return Vector3.Distance(p, owner.transform.position);
    }

    static string StripClone(string n)
    {
        if (string.IsNullOrEmpty(n)) return "?";
        return n.Replace("(Clone)", "").Trim();
    }

    static bool NamesMatch(string expected, string drew)
    {
        if (expected == drew) return true;
        var ep = expected.Split('@');
        var dp = drew.Split('@');
        if (ep.Length != 2 || dp.Length != 2) return false;
        if (ep[1] != dp[1]) return false;
        string e = Measurable.DualSelectStem(ep[0]);
        string d = Measurable.DualSelectStem(dp[0]);
        return e == d || ep[0].IndexOf(d, System.StringComparison.OrdinalIgnoreCase) >= 0
               || dp[0].IndexOf(e, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
