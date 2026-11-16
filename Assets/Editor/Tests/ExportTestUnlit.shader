Shader "Hidden/OrsExportTestLit"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        [MainTexture] _BaseMap("Albedo", 2D) = "" {}
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("Albedo", 2D) = "" {}
        _OcclusionMap("Occlusion", 2D) = "" {}
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass { }
    }
}
