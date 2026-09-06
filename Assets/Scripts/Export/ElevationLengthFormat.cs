using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Shared catalog-length formatting for elevation photos, PDF tables, and proposal text.
/// Prefers committed <see cref="Selectable.CurrentScaleLevel"/>; falls back to a Selected ScaleLevel.
/// Fixed-length arms (PoweredXL) with empty ScaleLevels use primary PdfData "Nmm" (same as PDF table).
/// </summary>
public static class ElevationLengthFormat
{
    static readonly Regex PdfLengthMm = new(
        @"(\d+)\s*mm",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Catalog Size → meters. Arms store meters (0.8); some legacy SH metadata stores raw mm (>= 10).
    /// </summary>
    public static float SizeValueToMeters(float size)
    {
        if (size <= 0f)
            return 0f;
        return size >= 10f ? size * 0.001f : size;
    }

    /// <summary>Catalog Size (meters or legacy mm) → rounded millimeter integer.</summary>
    public static int SizeValueToMm(float size)
    {
        float meters = SizeValueToMeters(size);
        return meters <= 0f ? 0 : Mathf.RoundToInt(meters * 1000f);
    }

    /// <summary>
    /// Size meters, else primary PdfData fixed length (PoweredXL). Used by elevation dim curation.
    /// </summary>
    public static float ResolveCatalogLengthMeters(Selectable selectable)
    {
        float sizeM = ResolveSizeMeters(selectable);
        if (sizeM > 0f)
            return sizeM;
        return ResolvePdfDataLengthMeters(selectable);
    }

    /// <summary>
    /// Fixed product length from metadata PdfData (e.g. "1000mm") when ScaleLevels are empty.
    /// Only the related-group primary — same gate PdfExporter uses for PdfData table rows.
    /// </summary>
    public static float ResolvePdfDataLengthMeters(Selectable selectable)
    {
        int mm = ExtractPdfDataLengthMm(selectable);
        return mm > 0 ? mm * 0.001f : 0f;
    }

    /// <summary>
    /// Best-effort mm from metadata PdfData. RelatedSelectables share GetMetadata(), so only
    /// the group primary (RelatedSelectables[0] == self) may claim this as a length owner.
    /// </summary>
    public static int ExtractPdfDataLengthMm(Selectable selectable)
    {
        if (selectable == null)
            return 0;
        if (selectable.RelatedSelectables == null || selectable.RelatedSelectables.Count == 0
            || selectable.RelatedSelectables[0] != selectable)
            return 0;

        var meta = selectable.GetMetadata();
        if (meta?.PdfData == null)
            return 0;

        int best = 0;
        foreach (var pdf in meta.PdfData)
        {
            if (pdf == null)
                continue;
            foreach (string field in new[] { pdf.Value, pdf.Key })
            {
                if (string.IsNullOrEmpty(field))
                    continue;
                var m = PdfLengthMm.Match(field);
                if (!m.Success)
                    continue;
                if (int.TryParse(m.Groups[1].Value, out int mm) && mm > best)
                    best = mm;
            }
        }
        return best;
    }

    /// <summary>
    /// Returns e.g. "1300 mm" when the selectable has a positive catalog Size; otherwise null.
    /// </summary>
    public static string TryFormatMm(Selectable selectable)
    {
        float sizeM = ResolveSizeMeters(selectable);
        if (sizeM <= 0f)
            return null;

        int mm = SizeValueToMm(sizeM);
        if (mm <= 0)
            return null;

        return $"{mm} mm";
    }

    /// <summary>Parenthetical size suffix for proposal lists, e.g. " (1300 mm)", or empty.</summary>
    public static string TryFormatMmSuffix(Selectable selectable)
    {
        string mm = TryFormatMm(selectable);
        return string.IsNullOrEmpty(mm) ? string.Empty : $" ({mm})";
    }

    public static float ResolveSizeMeters(Selectable selectable)
    {
        float self = ResolveOwnSizeMeters(selectable);
        if (self > 0f || selectable?.RelatedSelectables == null)
            return self;

        // Dual-select prefabs: ScaleLevels often live on .001 while a related wrapper
        // owns the Measurable (or vice versa). Take the best positive Size in the group.
        float best = self;
        foreach (var rel in selectable.RelatedSelectables)
        {
            if (rel == null || rel == selectable)
                continue;
            float size = ResolveOwnSizeMeters(rel);
            if (size > best)
                best = size;
        }
        return best;
    }

    /// <summary>
    /// Compact Size-binding dump for ElevDim logs when catalog length is missing or wrong.
    /// Points at: orphan CurrentScaleLevel, empty ScaleLevels, Selected flag, liveZ mismatch.
    /// </summary>
    public static string DiagnoseSizeBinding(Selectable selectable)
    {
        if (selectable == null)
            return "sel=null";

        var sb = new StringBuilder(160);
        sb.Append("sel=").Append(selectable.name);
        sb.Append(" liveZ=").Append(selectable.transform.localScale.z.ToString("F3"));

        var cur = selectable.CurrentScaleLevel;
        if (cur == null)
            sb.Append(" current=null");
        else
        {
            bool inList = selectable.ScaleLevels != null && selectable.ScaleLevels.Contains(cur);
            sb.Append(" current={size=").Append(cur.Size.ToString("F3"))
              .Append(" z=").Append(cur.ScaleZ.ToString("F3"))
              .Append(" sel=").Append(cur.Selected)
              .Append(" inList=").Append(inList).Append('}');
        }

        int levelCount = selectable.ScaleLevels != null ? selectable.ScaleLevels.Count : 0;
        sb.Append(" levels=").Append(levelCount);
        if (levelCount > 0)
        {
            var selected = selectable.ScaleLevels.FirstOrDefault(s => s != null && s.Selected);
            var anySized = selectable.ScaleLevels.FirstOrDefault(s => s != null && s.Size > 0f);
            sb.Append(" selectedSize=")
              .Append(selected != null ? selected.Size.ToString("F3") : "none");
            sb.Append(" anySize=")
              .Append(anySized != null ? anySized.Size.ToString("F3") : "none");
        }

        float resolved = ResolveSizeMeters(selectable);
        sb.Append(" resolvedM=").Append(resolved.ToString("F3"))
          .Append(" resolvedMm=").Append(SizeValueToMm(resolved));

        if (selectable.RelatedSelectables != null)
        {
            float relatedBest = 0f;
            string relatedName = null;
            foreach (var rel in selectable.RelatedSelectables)
            {
                if (rel == null || rel == selectable)
                    continue;
                float s = ResolveOwnSizeMeters(rel);
                if (s > relatedBest)
                {
                    relatedBest = s;
                    relatedName = rel.name;
                }
            }
            if (relatedBest > 0f)
                sb.Append(" relatedBest=").Append(relatedName).Append('@').Append(relatedBest.ToString("F3"));
        }

        return sb.ToString();
    }

    /// <summary>Size on this selectable only — does not walk RelatedSelectables. Always meters.</summary>
    public static float ResolveOwnSizeMeters(Selectable selectable)
    {
        if (selectable == null)
            return 0f;

        if (selectable.CurrentScaleLevel != null && selectable.CurrentScaleLevel.Size > 0f)
            return SizeValueToMeters(selectable.CurrentScaleLevel.Size);

        if (selectable.ScaleLevels == null || selectable.ScaleLevels.Count == 0)
            return 0f;

        var selected = selectable.ScaleLevels.FirstOrDefault(s => s != null && s.Selected && s.Size > 0f);
        if (selected != null)
            return SizeValueToMeters(selected.Size);

        // Match live ScaleZ to a catalog level (load can leave CurrentScaleLevel unbound).
        float liveZ = selectable.transform.localScale.z;
        if (selectable.CurrentScaleLevel != null && selectable.CurrentScaleLevel.ScaleZ > 0.01f)
            liveZ = selectable.CurrentScaleLevel.ScaleZ;
        if (liveZ > 0.01f)
        {
            var byZ = selectable.ScaleLevels.FirstOrDefault(s =>
                s != null && s.Size > 0f && s.ScaleZ > 0.01f && Mathf.Abs(s.ScaleZ - liveZ) < 0.02f);
            if (byZ != null)
                return SizeValueToMeters(byZ.Size);
        }

        var any = selectable.ScaleLevels.FirstOrDefault(s => s != null && s.Size > 0f);
        return any != null ? SizeValueToMeters(any.Size) : 0f;
    }

    static float ResolveSizeMetersOn(Selectable selectable) => ResolveOwnSizeMeters(selectable);
}
