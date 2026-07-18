using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using UnityEngine;

/// <summary>
/// Locates editable proposal fields by reading real text positions from the
/// generated PDF — not by guessing normalized rectangles.
/// </summary>
public static class ProposalPdfPreviewHotspotFinder
{
    /// <summary>Ignore footer / page-event text stamped near the bottom.</summary>
    const float FooterExclusionPoints = 48f;

    /// <summary>Extra hit padding around matched glyphs (PDF points).</summary>
    const float PadX = 6f;
    const float PadY = 4f;

    struct Glyph
    {
        public string Text;
        public float X0, Y0, X1, Y1;
        public float MidY => (Y0 + Y1) * 0.5f;
    }

    public static List<ProposalPreviewHotspot> Find(string pdfPath, ProposalPreviewModel model)
    {
        var results = new List<ProposalPreviewHotspot>();
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            return results;

        string salesName = model?.SalesRepName?.Trim() ?? "";
        string salesPhone = model?.SalesRepPhone?.Trim() ?? "";
        string salesEmail = model?.SalesRepEmail?.Trim() ?? "";
        string configName = model?.ConfigName?.Trim() ?? "";

        try
        {
            using var reader = new PdfReader(pdfPath);
            for (int page = 1; page <= reader.NumberOfPages; page++)
            {
                var pageSize = reader.GetPageSize(page);
                float pageW = pageSize.Width;
                float pageH = pageSize.Height;
                if (pageW < 1f || pageH < 1f)
                    continue;

                var glyphs = CollectGlyphs(reader, page);
                if (glyphs.Count == 0)
                    continue;

                int pageIndex = page - 1;
                // Page 2+ repeats the company header (sales rep / submitted to / project / config).
                // Keep those clickable on every page — not just page 1.
                AddLabelRegion(results, glyphs, pageIndex, pageW, pageH,
                    ProposalPreviewEditKind.ClientData, "Submitted to / client",
                    g => StartsWithIgnoreCase(g.Text, "Submitted To"), preferTop: true,
                    expandRightToContent: true, minHeightPts: 22f);
                AddProject(results, glyphs, pageIndex, pageW, pageH);
                AddSalesRep(results, glyphs, pageIndex, pageW, pageH, salesName, salesPhone, salesEmail);
                AddConfigTitle(results, glyphs, pageIndex, pageW, pageH, configName);
                AddPageEventProjectInfo(results, glyphs, pageIndex, pageW, pageH);
                AddOptions(results, glyphs, pageIndex, pageW, pageH);
                AddDiscount(results, glyphs, pageIndex, pageW, pageH);
                AddNotes(results, glyphs, pageIndex, pageW, pageH);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Proposal hotspot find failed: {e.Message}");
        }

        return results;
    }

    static List<Glyph> CollectGlyphs(PdfReader reader, int page)
    {
        var listener = new GlyphListener(FooterExclusionPoints);
        var parser = new PdfReaderContentParser(reader);
        parser.ProcessContent(page, listener);
        return listener.Glyphs;
    }

    /// <summary>
    /// Click target is the original header block under "Proposal" (name/phone/email).
    /// Present on every proposal page that repeats the company header (page 2+ included).
    /// </summary>
    static void AddSalesRep(
        List<ProposalPreviewHotspot> results,
        List<Glyph> glyphs,
        int pageIndex,
        float pageW,
        float pageH,
        string salesName,
        string salesPhone,
        string salesEmail)
    {
        var lines = BuildLines(glyphs);

        TextLine proposalLine = default;
        bool hasProposal = false;
        foreach (var line in lines)
        {
            if (!string.Equals(line.Text.Trim(), "Proposal", StringComparison.OrdinalIgnoreCase))
                continue;
            proposalLine = line;
            hasProposal = true;
            break;
        }

        if (!hasProposal)
            return;

        var headerHits = new List<Glyph>();
        foreach (var line in lines)
        {
            string text = line.Text;
            bool nameHit = !string.IsNullOrEmpty(salesName)
                           && text.IndexOf(salesName, StringComparison.OrdinalIgnoreCase) >= 0;
            bool phoneHit = !string.IsNullOrEmpty(salesPhone)
                            && text.IndexOf(salesPhone, StringComparison.OrdinalIgnoreCase) >= 0;
            bool emailHit = !string.IsNullOrEmpty(salesEmail)
                            && text.IndexOf(salesEmail, StringComparison.OrdinalIgnoreCase) >= 0;
            if (nameHit || phoneHit || emailHit)
                headerHits.AddRange(line.Glyphs);
        }

        if (headerHits.Count == 0)
        {
            headerHits.AddRange(lines
                .Where(l =>
                    l.MidY < proposalLine.Y0 - 2f
                    && l.MidY > proposalLine.Y0 - 88f
                    && l.X0 < pageW * 0.70f
                    && !StartsWithIgnoreCase(l.Text, "Imagine")
                    && !StartsWithIgnoreCase(l.Text, "Submitted")
                    && !StartsWithIgnoreCase(l.Text, "Project")
                    && !StartsWithIgnoreCase(l.Text, "9155")
                    && !StartsWithIgnoreCase(l.Text, "Irving")
                    && !StartsWithIgnoreCase(l.Text, "Tel:"))
                .SelectMany(l => l.Glyphs));
        }

        float colX0 = 12f;
        float colX1 = pageW * 0.68f;
        float yTop;
        float yBot;
        if (headerHits.Count > 0)
        {
            yBot = headerHits.Min(h => h.Y0) - 12f;
            yTop = Mathf.Max(headerHits.Max(h => h.Y1) + 12f, proposalLine.Y0 - 2f);
        }
        else
        {
            // Empty placeholder under "Proposal" so the field stays editable.
            yTop = proposalLine.Y0 - 2f;
            yBot = proposalLine.Y0 - 78f;
        }

        EmitSalesRepSpot(results, pageIndex, pageW, pageH, colX0, yBot, colX1, yTop);
    }

    static void EmitSalesRepSpot(
        List<ProposalPreviewHotspot> results,
        int pageIndex,
        float pageW,
        float pageH,
        float x0,
        float y0,
        float x1,
        float y1)
    {
        y0 = Mathf.Clamp(y0, FooterExclusionPoints, pageH);
        y1 = Mathf.Clamp(y1, FooterExclusionPoints, pageH);
        x0 = Mathf.Clamp(x0, 0f, pageW);
        x1 = Mathf.Clamp(x1, 0f, pageW);
        if (y1 - y0 < 32f)
        {
            float mid = (y0 + y1) * 0.5f;
            y0 = mid - 16f;
            y1 = mid + 16f;
        }
        if (x1 - x0 < 80f || y1 - y0 < 12f)
            return;

        results.Add(new ProposalPreviewHotspot(
            ProposalPreviewEditKind.SalesRep,
            pageIndex,
            x0 / pageW,
            y0 / pageH,
            x1 / pageW,
            y1 / pageH,
            "Sales rep — click to edit"));
    }

    static void AddProject(
        List<ProposalPreviewHotspot> results,
        List<Glyph> glyphs,
        int pageIndex,
        float pageW,
        float pageH)
    {
        var lines = BuildLines(glyphs);
        var hit = lines
            .Where(l => StartsWithIgnoreCase(l.Text, "Project:"))
            .OrderByDescending(l => l.MidY)
            .FirstOrDefault();
        if (hit.Glyphs == null || hit.Glyphs.Count == 0)
            return;

        var list = new List<Glyph>(hit.Glyphs);
        list.AddRange(glyphs.Where(g =>
            Mathf.Abs(g.MidY - hit.MidY) < 6f
            && g.X0 >= hit.X0 - 1f));

        EmitUnion(results, list, pageIndex, pageW, pageH,
            ProposalPreviewEditKind.Project, "Project",
            fullContentWidth: true, minHeightPts: 22f);
    }

    /// <summary>
    /// Page 2+ page-event stamp: "Configuration 1: {name}" (and any extra Project /
    /// Submitted lines that survived footer filtering). Maps to the same editors as the header.
    /// </summary>
    static void AddPageEventProjectInfo(
        List<ProposalPreviewHotspot> results,
        List<Glyph> glyphs,
        int pageIndex,
        float pageW,
        float pageH)
    {
        if (pageIndex <= 0)
            return;

        var lines = BuildLines(glyphs);
        foreach (var line in lines)
        {
            string t = line.Text.Trim();
            if (StartsWithIgnoreCase(t, "Configuration 1:"))
            {
                EmitUnion(results, line.Glyphs, pageIndex, pageW, pageH,
                    ProposalPreviewEditKind.ConfigTitle, "Configuration title",
                    fullContentWidth: true, minHeightPts: 20f);
            }
            else if (StartsWithIgnoreCase(t, "Project:") && line.MidY < pageH * 0.35f)
            {
                // Lower-page stamp only (header Project is already covered).
                EmitUnion(results, line.Glyphs, pageIndex, pageW, pageH,
                    ProposalPreviewEditKind.Project, "Project",
                    fullContentWidth: true, minHeightPts: 20f);
            }
            else if (StartsWithIgnoreCase(t, "Submitted To") && line.MidY < pageH * 0.35f)
            {
                EmitUnion(results, line.Glyphs, pageIndex, pageW, pageH,
                    ProposalPreviewEditKind.ClientData, "Submitted to / client",
                    fullContentWidth: true, minHeightPts: 20f);
            }
        }
    }

    static void AddConfigTitle(
        List<ProposalPreviewHotspot> results,
        List<Glyph> glyphs,
        int pageIndex,
        float pageW,
        float pageH,
        string configName)
    {
        if (string.IsNullOrWhiteSpace(configName))
            return;

        var lines = BuildLines(glyphs);
        var candidates = lines
            .Where(l =>
            {
                string t = l.Text.Trim().TrimStart('\t');
                if (StartsWithIgnoreCase(t, "Configuration 1:"))
                    return true;
                return string.Equals(t, configName, StringComparison.OrdinalIgnoreCase)
                       || t.EndsWith(configName, StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(l => l.MidY)
            .ToList();

        if (candidates.Count == 0)
            return;

        float lastY = float.MaxValue;
        foreach (var c in candidates)
        {
            if (Mathf.Abs(c.MidY - lastY) < 10f)
                continue;
            lastY = c.MidY;
            EmitUnion(results, c.Glyphs, pageIndex, pageW, pageH,
                ProposalPreviewEditKind.ConfigTitle, "Configuration title",
                fullContentWidth: true, minHeightPts: 18f);
        }
    }

    struct TextLine
    {
        public string Text;
        public float X0, Y0, X1, Y1;
        public float MidY => (Y0 + Y1) * 0.5f;
        public List<Glyph> Glyphs;
    }

    static List<TextLine> BuildLines(List<Glyph> glyphs)
    {
        var ordered = glyphs.OrderByDescending(g => g.MidY).ThenBy(g => g.X0).ToList();
        var lines = new List<TextLine>();
        TextLine current = default;
        bool has = false;

        foreach (var g in ordered)
        {
            if (!has || Mathf.Abs(g.MidY - current.MidY) > 3.5f)
            {
                if (has)
                    lines.Add(current);
                current = new TextLine
                {
                    Text = g.Text ?? "",
                    X0 = g.X0,
                    Y0 = g.Y0,
                    X1 = g.X1,
                    Y1 = g.Y1,
                    Glyphs = new List<Glyph> { g }
                };
                has = true;
            }
            else
            {
                current.Text += g.Text;
                current.X0 = Mathf.Min(current.X0, g.X0);
                current.Y0 = Mathf.Min(current.Y0, g.Y0);
                current.X1 = Mathf.Max(current.X1, g.X1);
                current.Y1 = Mathf.Max(current.Y1, g.Y1);
                current.Glyphs.Add(g);
            }
        }

        if (has)
            lines.Add(current);
        return lines;
    }

    static void AddOptions(
        List<ProposalPreviewHotspot> results,
        List<Glyph> glyphs,
        int pageIndex,
        float pageW,
        float pageH)
    {
        var headers = glyphs
            .Where(g => g.Text.IndexOf("OPTION/ACCESSORY", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(g => g.MidY)
            .ToList();
        if (headers.Count == 0)
            return;

        var bands = new List<Glyph>();
        foreach (var header in headers)
        {
            float top = header.Y1 + PadY;
            float bottom = header.Y0 - 8f;

            foreach (var g in glyphs.Where(g => g.MidY < header.MidY - 1f).OrderByDescending(g => g.MidY))
            {
                bool isStop = g.Text.IndexOf("EQUIPMENT TOTAL", StringComparison.OrdinalIgnoreCase) >= 0
                              || (string.Equals(g.Text.Trim(), "MODEL DESCRIPTION", StringComparison.OrdinalIgnoreCase)
                                  && g.MidY < header.MidY - 20f);

                if (isStop)
                {
                    bottom = Mathf.Min(bottom, g.Y1 + 2f);
                    break;
                }

                if (header.MidY - g.MidY > 160f)
                    break;

                bottom = Mathf.Min(bottom, g.Y0 - PadY);
            }

            bands.Add(new Glyph
            {
                Text = header.Text,
                X0 = 18f,
                Y0 = bottom,
                X1 = pageW - 18f,
                Y1 = top
            });
        }

        EmitUnion(results, bands, pageIndex, pageW, pageH,
            ProposalPreviewEditKind.Options, "Light & boom options",
            fullContentWidth: true, minHeightPts: 28f);
    }

    static void AddDiscount(
        List<ProposalPreviewHotspot> results,
        List<Glyph> glyphs,
        int pageIndex,
        float pageW,
        float pageH)
    {
        AddLabelRegion(results, glyphs, pageIndex, pageW, pageH,
            ProposalPreviewEditKind.Discount, "Discount",
            g => g.Text.IndexOf("DISCOUNT", StringComparison.OrdinalIgnoreCase) >= 0, preferTop: false,
            expandRightToContent: true, minHeightPts: 20f);
    }

    static void AddNotes(
        List<ProposalPreviewHotspot> results,
        List<Glyph> glyphs,
        int pageIndex,
        float pageW,
        float pageH)
    {
        var notesHeader = glyphs
            .Where(g => string.Equals(g.Text.Trim(), "Notes:", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(g.Text.Trim(), "Notes", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.MidY)
            .FirstOrDefault();

        if (notesHeader.Text == null)
            return;

        float top = notesHeader.Y1 + PadY;
        float bottom = FooterExclusionPoints + 8f;

        // Include acceptance / signature block under notes.
        var signature = glyphs
            .Where(g => g.Text.IndexOf("Signature", StringComparison.OrdinalIgnoreCase) >= 0
                        && g.MidY < notesHeader.MidY)
            .OrderBy(g => g.MidY)
            .FirstOrDefault();
        if (signature.Text != null)
            bottom = Mathf.Min(bottom, signature.Y0 - PadY);

        var band = new Glyph
        {
            Text = "Notes",
            X0 = 18f,
            Y0 = bottom,
            X1 = pageW - 18f,
            Y1 = top
        };
        EmitUnion(results, new List<Glyph> { band }, pageIndex, pageW, pageH,
            ProposalPreviewEditKind.Notes, "Notes & acceptance",
            fullContentWidth: true, minHeightPts: 40f);
    }

    static void AddLabelRegion(
        List<ProposalPreviewHotspot> results,
        List<Glyph> glyphs,
        int pageIndex,
        float pageW,
        float pageH,
        ProposalPreviewEditKind kind,
        string tooltip,
        Func<Glyph, bool> match,
        bool preferTop,
        bool expandRightToContent = false,
        float minHeightPts = 18f)
    {
        // Prefer whole-line matches so fragmented PDF text still hits.
        var lines = BuildLines(glyphs);
        Func<TextLine, bool> lineMatch = l => l.Glyphs.Any(match) || match(new Glyph { Text = l.Text, X0 = l.X0, Y0 = l.Y0, X1 = l.X1, Y1 = l.Y1 });
        IEnumerable<TextLine> q = lines.Where(lineMatch);
        q = preferTop ? q.OrderByDescending(l => l.MidY) : q.OrderBy(l => l.MidY);
        var hit = q.FirstOrDefault();
        if (hit.Glyphs == null || hit.Glyphs.Count == 0)
            return;

        var list = new List<Glyph>(hit.Glyphs);
        if (expandRightToContent)
        {
            list.AddRange(glyphs.Where(g =>
                Mathf.Abs(g.MidY - hit.MidY) < 6f
                && g.X0 >= hit.X0 - 1f));
        }

        EmitUnion(results, list, pageIndex, pageW, pageH, kind, tooltip,
            fullContentWidth: expandRightToContent, minHeightPts: minHeightPts);
    }

    static void EmitUnion(
        List<ProposalPreviewHotspot> results,
        List<Glyph> hits,
        int pageIndex,
        float pageW,
        float pageH,
        ProposalPreviewEditKind kind,
        string tooltip,
        bool fullContentWidth,
        float minHeightPts)
    {
        if (hits == null || hits.Count == 0)
            return;

        float x0 = hits.Min(h => h.X0) - PadX;
        float y0 = hits.Min(h => h.Y0) - PadY;
        float x1 = hits.Max(h => h.X1) + PadX;
        float y1 = hits.Max(h => h.Y1) + PadY;

        if (fullContentWidth)
        {
            x0 = Mathf.Min(x0, 14f);
            x1 = Mathf.Max(x1, pageW - 14f);
        }

        if (y1 - y0 < minHeightPts)
        {
            float mid = (y0 + y1) * 0.5f;
            y0 = mid - minHeightPts * 0.5f;
            y1 = mid + minHeightPts * 0.5f;
        }

        x0 = Mathf.Clamp(x0, 0f, pageW);
        x1 = Mathf.Clamp(x1, 0f, pageW);
        y0 = Mathf.Clamp(y0, 0f, pageH);
        y1 = Mathf.Clamp(y1, 0f, pageH);
        if (x1 - x0 < 8f || y1 - y0 < 8f)
            return;

        results.Add(new ProposalPreviewHotspot(
            kind,
            pageIndex,
            x0 / pageW,
            y0 / pageH,
            x1 / pageW,
            y1 / pageH,
            tooltip));
    }

    static bool StartsWithIgnoreCase(string text, string prefix)
    {
        return !string.IsNullOrEmpty(text)
               && text.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    sealed class GlyphListener : IRenderListener
    {
        readonly float _minY;
        public readonly List<Glyph> Glyphs = new();

        public GlyphListener(float minY) => _minY = minY;

        public void BeginTextBlock() { }
        public void EndTextBlock() { }
        public void RenderImage(ImageRenderInfo renderInfo) { }

        public void RenderText(TextRenderInfo renderInfo)
        {
            if (renderInfo == null)
                return;

            string text = renderInfo.GetText();
            if (string.IsNullOrWhiteSpace(text))
                return;

            // Use ascent/descent baselines for a stable glyph box.
            Vector bl = renderInfo.GetDescentLine().GetStartPoint();
            Vector br = renderInfo.GetDescentLine().GetEndPoint();
            Vector tl = renderInfo.GetAscentLine().GetStartPoint();
            Vector tr = renderInfo.GetAscentLine().GetEndPoint();

            float x0 = Min4(bl[Vector.I1], br[Vector.I1], tl[Vector.I1], tr[Vector.I1]);
            float x1 = Max4(bl[Vector.I1], br[Vector.I1], tl[Vector.I1], tr[Vector.I1]);
            float y0 = Min4(bl[Vector.I2], br[Vector.I2], tl[Vector.I2], tr[Vector.I2]);
            float y1 = Max4(bl[Vector.I2], br[Vector.I2], tl[Vector.I2], tr[Vector.I2]);

            if (y1 < _minY && !IsEditableLabel(text))
                return;

            Glyphs.Add(new Glyph
            {
                Text = text,
                X0 = x0,
                Y0 = y0,
                X1 = x1,
                Y1 = y1
            });
        }

        static bool IsEditableLabel(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string t = text.TrimStart();
            return t.StartsWith("Project:", StringComparison.OrdinalIgnoreCase)
                   || t.StartsWith("Submitted To", StringComparison.OrdinalIgnoreCase)
                   || t.StartsWith("Configuration 1:", StringComparison.OrdinalIgnoreCase);
        }

        static float Min4(float a, float b, float c, float d) => Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d));
        static float Max4(float a, float b, float c, float d) => Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d));
    }
}
