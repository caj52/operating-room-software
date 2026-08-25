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

/// <summary>
/// How <see cref="Selectable"/> length / ScaleLevels interact with transforms.
/// Tube isolation (<c>IsolateDirectChildrenPreservingWorldScale</c>) runs on
/// <see cref="LengthTube"/> only — never AuthoredIdentity / SkuAxisStretch / RowConfig.
/// </summary>
public enum LengthScaleKind
{
    /// <summary>Arms, drop tubes: ScaleZ = Size/authoredLength, Z-only stretch, AP inverse.</summary>
    LengthTube = 0,

    /// <summary>
    /// Service head cabinet. ScaleZ = Size/ModelDefault (body height).
    /// Body shells inherit Z. Other direct children keep authored world scale
    /// (lossy at local identity under ScaleZ=1). <see cref="BoomHeadScaleHandler.ReassembleRows"/>
    /// owns row show/hide + offsets via <c>OnScaleChange</c>.
    /// </summary>
    RowConfigAssembly = 1,

    /// <summary>
    /// SH rails / outlet plates / HV-LV mounts: root identity; mesh children keep prefab
    /// import scales (e.g. Rear Rail local 0.01). Size is catalog only — never tube-bake.
    /// </summary>
    AuthoredIdentity = 2,

    /// <summary>SH shelves: X-only SKU stretch vs base mesh; Y/Z stay 1.</summary>
    SkuAxisStretch = 3
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