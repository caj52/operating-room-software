using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using PDFtoImage;
using SkiaSharp;
using UnityEngine;

/// <summary>
/// Rasterizes a generated sales-proposal PDF into Unity textures for the workspace viewer.
/// </summary>
public static class ProposalPdfPreviewRasterizer
{
    /// <summary>
    /// ~200 DPI keeps body text sharp at fit and ~1.7× click-zoom (A4 ≈ 1654×2339).
    /// </summary>
    public const int DefaultDpi = 200;

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
        if (bitmap == null)
            throw new ArgumentNullException(nameof(bitmap));

        int w = bitmap.Width;
        int h = bitmap.Height;
        if (w < 1 || h < 1)
            throw new InvalidOperationException("Rasterized page has zero size.");

        // Prefer a raw pixel upload — Encode(PNG/JPEG)+LoadImage is far slower.
        using (var copy = bitmap.Copy(SKColorType.Rgba8888))
        {
            if (copy == null)
                return ToTextureViaEncode(bitmap);

            IntPtr ptr = copy.GetPixels();
            if (ptr == IntPtr.Zero)
                return ToTextureViaEncode(bitmap);

            int rowBytes = copy.RowBytes;
            int rgbaStride = w * 4;
            var pixels = new byte[w * h * 4];

            // Skia is top-down; Unity textures are bottom-up — flip while copying.
            for (int y = 0; y < h; y++)
            {
                IntPtr srcRow = IntPtr.Add(ptr, y * rowBytes);
                int dstRow = (h - 1 - y) * rgbaStride;
                Marshal.Copy(srcRow, pixels, dstRow, rgbaStride);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = "ProposalPdfPage";
            tex.LoadRawTextureData(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
            return tex;
        }
    }

    static Texture2D ToTextureViaEncode(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
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
