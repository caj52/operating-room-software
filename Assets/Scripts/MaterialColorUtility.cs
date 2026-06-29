using UnityEngine;

public static class MaterialColorUtility
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public static void SetColor(Renderer renderer, Color color, ref MaterialPropertyBlock block)
    {
        if (renderer == null) return;

        if (block == null)
            block = new MaterialPropertyBlock();

        renderer.GetPropertyBlock(block);
        var shared = renderer.sharedMaterial;
        if (shared != null && shared.HasProperty(BaseColorId))
            block.SetColor(BaseColorId, color);
        else
            block.SetColor(ColorId, color);
        renderer.SetPropertyBlock(block);
    }

    public static void SetLineRendererColor(LineRenderer lineRenderer, Color color, ref MaterialPropertyBlock block)
    {
        if (lineRenderer == null) return;

        lineRenderer.startColor = color;
        lineRenderer.endColor = color;

        if (block == null)
            block = new MaterialPropertyBlock();

        lineRenderer.GetPropertyBlock(block);
        var shared = lineRenderer.sharedMaterial;
        if (shared != null && shared.HasProperty(BaseColorId))
            block.SetColor(BaseColorId, color);
        else
            block.SetColor(ColorId, color);
        lineRenderer.SetPropertyBlock(block);
    }
}
