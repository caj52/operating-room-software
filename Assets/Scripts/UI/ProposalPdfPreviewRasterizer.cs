using System;
using System.Collections.Generic;
using System.IO;
using PDFtoImage;
using SkiaSharp;
using UnityEngine;

/// <summary>
/// Rasterizes a generated sales-proposal PDF into Unity textures for the workspace viewer.
/// </summary>
public static class ProposalPdfPreviewRasterizer
{
    /// <summary>~110 DPI keeps A4 pages sharp enough on screen without multi‑MB textures.</summary>
    public const int DefaultDpi = 110;

    public static List<Texture2D> RasterizePages(string pdfPath, int dpi = DefaultDpi)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            throw new FileNotFoundException("Preview PDF not found.", pdfPath);

        byte[] bytes = File.ReadAllBytes(pdfPath);
        return RasterizePages(bytes, dpi);
    }

    public static List<Texture2D> RasterizePages(byte[] pdfBytes, int dpi = DefaultDpi)
    {
        if (pdfBytes == null || pdfBytes.Length == 0)
            throw new ArgumentException("PDF bytes are empty.", nameof(pdfBytes));

        var options = new RenderOptions(dpi);
        int pageCount = Conversion.GetPageCount(pdfBytes);
        var pages = new List<Texture2D>(Mathf.Max(1, pageCount));

        for (int i = 0; i < pageCount; i++)
        {
            using SKBitmap bitmap = Conversion.ToImage(pdfBytes, i, null, options);
            pages.Add(ToTexture(bitmap));
        }

        return pages;
    }

    static Texture2D ToTexture(SKBitmap bitmap)
    {
        // Encode via PNG so we inherit correct color order without manual BGRA swizzle.
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.name = "ProposalPdfPage";
        if (!tex.LoadImage(data.ToArray(), markNonReadable: true))
        {
            UnityEngine.Object.Destroy(tex);
            throw new InvalidOperationException("Failed to decode rasterized PDF page.");
        }
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }
}
