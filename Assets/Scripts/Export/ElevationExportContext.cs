using System.Collections.Generic;

/// <summary>
/// Live export session — ties elevation photos to the same catalog rows as the PDF tables.
/// </summary>
public static class ElevationExportContext
{
    public static IList<AssemblyData> AssemblyDatas { get; private set; }

    public static void Begin(IList<AssemblyData> assemblyDatas)
    {
        AssemblyDatas = assemblyDatas;
    }

    public static void End()
    {
        AssemblyDatas = null;
    }
}
