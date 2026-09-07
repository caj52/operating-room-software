using System.Collections.Generic;

/// <summary>
/// Live export session — catalog rows for PDF tables/dims. The photo is the live room.
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
