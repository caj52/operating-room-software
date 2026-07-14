using System;

public enum ElevationExportMode
{
    CombinedRoom,
    PerAssembly
}

public enum ExportScope
{
    Room,
    SelectedObject
}

[Serializable]
public class ExportRequest
{
    public ExportScope Scope = ExportScope.Room;

    public bool IncludeObj = true;
    public bool IncludeElevations = true;
    public bool IncludeProposal = false;
    public bool IncludeSnapshots = true;

    public ElevationExportMode ElevationMode = ElevationExportMode.CombinedRoom;
    public ObjExportOptions ObjOptions = ObjExportOptions.CreateDefaults();

    public static bool HasSelection()
        => Selectable.SelectedSelectables != null && Selectable.SelectedSelectables.Count > 0;

    public static bool SelectionIsArmAssembly()
    {
        if (!HasSelection())
            return false;
        return Selectable.SelectedSelectables[0].IsArmAssembly;
    }

    /// <summary>
    /// True when the selection can be saved as an object configuration
    /// (boom / arm assembly, scalable segment, etc.).
    /// </summary>
    public static bool SelectionIsConfigurable()
    {
        if (!HasSelection())
            return false;

        var s = Selectable.SelectedSelectables[0];
        if (s == null)
            return false;
        if (s.GetComponent<RoomBoundary>() != null || s.GetComponentInParent<RoomBoundary>() != null)
            return false;
        if (s.IsArmAssembly)
            return true;
        if (s.GetComponentInChildren<BoomConfigurationManager>(true) != null)
            return true;
        if (s.GetComponentInChildren<BoomHeadScaleHandler>(true) != null)
            return true;
        if (s.ScaleLevels != null && s.ScaleLevels.Count > 0)
            return true;
        return false;
    }

    public static ExportScope CurrentScope()
        => HasSelection() ? ExportScope.SelectedObject : ExportScope.Room;

    public static ExportRequest CreateDefaults()
        => CurrentScope() == ExportScope.SelectedObject
            ? CreateDefaultsForSelection()
            : CreateDefaultsForRoom();

    public static ExportRequest CreateDefaultsForRoom()
    {
        return new ExportRequest
        {
            Scope = ExportScope.Room,
            IncludeObj = true,
            IncludeElevations = false,
            IncludeProposal = false,
            IncludeSnapshots = true,
            ElevationMode = ElevationExportMode.CombinedRoom,
            ObjOptions = ObjExportOptions.CreateDefaults()
        };
    }

    public static ExportRequest CreateDefaultsForSelection()
    {
        return new ExportRequest
        {
            Scope = ExportScope.SelectedObject,
            IncludeObj = true,
            IncludeElevations = false,
            IncludeProposal = false,
            IncludeSnapshots = false,
            ElevationMode = ElevationExportMode.PerAssembly,
            // Room include filters are irrelevant for a single selected object.
            ObjOptions = ObjExportOptions.CreateDefaults()
        };
    }
}
