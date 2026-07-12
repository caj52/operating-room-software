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
            IncludeElevations = true,
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
