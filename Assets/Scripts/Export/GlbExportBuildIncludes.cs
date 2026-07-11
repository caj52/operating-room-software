using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps glTFast export shaders referenced so player builds do not strip them.
/// </summary>
public static class GlbExportBuildIncludes
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void WarmExportShaders()
    {
        // Names from com.unity.cloud.gltfast Runtime/Shader/Export
        Shader.Find("Hidden/glTFExportColor");
        Shader.Find("Hidden/glTFExportMetalGloss");
        Shader.Find("Hidden/glTFExportNormal");
        Shader.Find("Hidden/glTFExportOcclusion");
        Shader.Find("Hidden/glTFExportSmoothness");
        Shader.Find("Hidden/glTFExportMaskMap");
    }
}
