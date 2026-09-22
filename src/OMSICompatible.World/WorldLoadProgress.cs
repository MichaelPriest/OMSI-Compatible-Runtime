namespace OMSICompatible.World;

public sealed record WorldLoadProgress(
    int Percent,
    string Stage,
    string Detail);
