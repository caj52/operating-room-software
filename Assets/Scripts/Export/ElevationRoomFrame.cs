using UnityEngine;

/// <summary>
/// Single vertical scale contract for elevation capture, dim placement, and PDF.
/// Floor top → ceiling underside drives ortho crop, photo height, and the room-height bar.
/// </summary>
public readonly struct ElevationRoomFrame
{
    public float FloorY { get; }
    public float CeilingY { get; }
    public float RoomHeightM => CeilingY - FloorY;

    public ElevationRoomFrame(float floorY, float ceilingY)
    {
        FloorY = floorY;
        CeilingY = ceilingY >= floorY + 0.01f ? ceilingY : floorY + 3f;
    }

    /// <summary>Room boundaries at capture/export time — one source, no PDF-only fallbacks.</summary>
    public static ElevationRoomFrame Current
    {
        get
        {
            float floorY = ComputeFloorTopY();
            return new ElevationRoomFrame(floorY, ComputeCeilingUndersideY(floorY));
        }
    }

    public static float ComputeFloorTopY()
    {
        var floor = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Floor);
        if (floor == null)
            return 0f;
        return floor.transform.position.y + floor.transform.localScale.y * 0.5f;
    }

    public static float ComputeCeilingUndersideY(float floorY)
    {
        var ceiling = RoomBoundary.GetRoomBoundary(RoomBoundaryType.Ceiling);
        if (ceiling == null)
            return floorY + 3f;

        float underside = ceiling.transform.position.y - ceiling.transform.localScale.y * 0.5f;
        if (underside > floorY + 0.1f)
            return underside;
        if (ceiling.Height > 0.1f)
            return floorY + ceiling.Height;
        return floorY + 3f;
    }

    public Bounds LockVertical(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        min.y = FloorY;
        max.y = CeilingY;
        bounds.SetMinMax(min, max);
        return bounds;
    }

    public float OrthoHalfHeight => 0.5f * Mathf.Max(0.01f, RoomHeightM);

    public Vector3 LookAtCenter(Bounds horizontalFrame) =>
        new Vector3(horizontalFrame.center.x, 0.5f * (FloorY + CeilingY), horizontalFrame.center.z);
}
