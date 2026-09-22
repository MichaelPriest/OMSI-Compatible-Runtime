namespace OmsiCompat.Map;

public sealed record OmsiTerrainGrid(
    int CellCount,
    IReadOnlyList<float> Heights)
{
    public int SampleCount => CellCount + 1;
}
