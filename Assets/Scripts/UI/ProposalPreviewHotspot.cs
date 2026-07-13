using System;

/// <summary>
/// Clickable region on a rasterized proposal page, in normalized page space
/// (0,0 = bottom-left, 1,1 = top-right) matching PDF / Unity UI anchors.
/// </summary>
public readonly struct ProposalPreviewHotspot
{
    public ProposalPreviewHotspot(
        ProposalPreviewEditKind kind,
        int pageIndex,
        float x0,
        float y0,
        float x1,
        float y1,
        string tooltip)
    {
        Kind = kind;
        PageIndex = pageIndex;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        Tooltip = tooltip ?? "";
    }

    public ProposalPreviewEditKind Kind { get; }
    public int PageIndex { get; }
    public float X0 { get; }
    public float Y0 { get; }
    public float X1 { get; }
    public float Y1 { get; }
    public string Tooltip { get; }
}

public enum ProposalPreviewEditKind
{
    SalesRep,
    ClientData,
    Project,
    ConfigTitle,
    Options,
    Discount,
    Notes,
}
