namespace OmsiCompat.Map;

public readonly record struct OmsiTileCoordinate(int X, int Y)
{
    public override string ToString() => $"{X},{Y}";
}

public sealed record OmsiMapTileInfo(
    OmsiTileCoordinate Coordinate,
    string FilePath,
    long Bytes);
