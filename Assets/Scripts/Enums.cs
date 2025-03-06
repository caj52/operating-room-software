using System;

public enum Axis
{
    X,
    Y,
    Z
}

public enum GizmoType
{
    Move,
    Rotate,
    Scale
}

public enum SpecialSelectableType
{
    [Obsolete]
    DropTube,

    Mount,

    [Obsolete]
    Furniture,

    [Obsolete]
    ArmSegment,

    [Obsolete]
    BoomSegment,

    [Obsolete]
    BoomHead,

    [Obsolete]
    Wall,

    [Obsolete]
    CeilingLight,

    Door,

    [Obsolete]
    ServiceHeadPanel,

    ServiceHeadShelves,

    [Obsolete]
    Tabletop
}

public enum MaterialGroup
{
    None,
    Walls,
    Baseboards,
    WallProtectors
}

public enum ScaleGroup
{
    Baseboards,
    WallProtectors
}