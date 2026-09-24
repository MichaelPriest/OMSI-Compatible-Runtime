namespace OMSICompatible.World;

public sealed record WorldDependency(
    WorldAssetKind Kind,
    string SourcePath,
    string? ResolvedPath,
    bool Exists);

public sealed record WorldDependencyReport(
    IReadOnlyList<WorldDependency> Dependencies)
{
    public int RequiredCount => Dependencies.Count;

    public int MissingCount => Dependencies.Count(static dependency => !dependency.Exists);

    public IReadOnlyList<WorldDependency> Missing =>
        Dependencies.Where(static dependency => !dependency.Exists).ToArray();
}
