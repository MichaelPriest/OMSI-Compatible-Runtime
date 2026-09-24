namespace OMSICompatible.World;

public sealed record WorldLoadOptions(
    int? CenterTileX = null,
    int? CenterTileY = null,
    int ActiveTileRadius = 1,
    bool LoadEntireMap = false)
{
    public int SafeActiveTileRadius =>
        Math.Clamp(ActiveTileRadius, 0, 4);

    public bool HasCenter =>
        CenterTileX.HasValue &&
        CenterTileY.HasValue;
}
