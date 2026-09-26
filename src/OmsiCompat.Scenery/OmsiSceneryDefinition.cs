namespace OmsiCompat.Scenery;

public sealed record OmsiSceneryMeshTransform(
    double PositionX,
    double PositionY,
    double PositionZ,
    double RotationX,
    double RotationY,
    double RotationZ,
    double ScaleX,
    double ScaleY,
    double ScaleZ)
{
    public static OmsiSceneryMeshTransform Identity { get; } =
        new(
            0,
            0,
            0,
            0,
            0,
            0,
            1,
            1,
            1);
}

public sealed record OmsiSceneryMeshReference(
    string Path,
    double? LodThreshold,
    OmsiSceneryMeshTransform Transform);

public sealed record OmsiSceneryTreeDefinition(
    string TextureName,
    double MinimumHeight,
    double MaximumHeight,
    double MinimumAspect,
    double MaximumAspect);

public sealed record OmsiSceneryMaterialOverride(
    int MeshOrdinal,
    string TextureName,
    int MaterialIndex,
    int? AlphaMode,
    string? TransMapSource,
    bool NoZWrite,
    bool NoZCheck);

public sealed record OmsiSceneryTrafficLightPhase(
    int Phase,
    double DurationSeconds);

public sealed record OmsiSceneryTrafficLightProgram(
    string Name,
    IReadOnlyList<OmsiSceneryTrafficLightPhase> Phases,
    double ApproachDistanceMeters);

public sealed record OmsiSceneryFileReference(
    string DeclaredPath,
    string? ResolvedPath)
{
    public bool Exists =>
        ResolvedPath is not null;
}

public sealed record OmsiSceneryScriptManifest(
    IReadOnlyList<OmsiSceneryFileReference> ScriptFiles,
    IReadOnlyList<OmsiSceneryFileReference> VariableLists,
    IReadOnlyList<OmsiSceneryFileReference> StringVariableLists,
    IReadOnlyList<OmsiSceneryFileReference> ConstantFiles)
{
    public int RegisteredFileCount =>
        ScriptFiles.Count +
        VariableLists.Count +
        StringVariableLists.Count +
        ConstantFiles.Count;

    public int MissingFileCount =>
        ScriptFiles.Count(static file => !file.Exists) +
        VariableLists.Count(static file => !file.Exists) +
        StringVariableLists.Count(static file => !file.Exists) +
        ConstantFiles.Count(static file => !file.Exists);
}

public sealed record OmsiSceneryPathDefinition(
    double X,
    double Y,
    double Z,
    double HeadingDegrees,
    double RadiusMeters,
    double LengthMeters,
    double GradientStart,
    double GradientEnd,
    int Type,
    double WidthMeters,
    int Direction,
    IReadOnlyList<string> ExtraValues,
    int? TrafficLightIndex = null);

public sealed record OmsiSceneryDefinition(
    bool Exists,
    bool UsesAbsoluteHeight,
    bool OnlyEditor,
    string? RenderType,
    IReadOnlyList<OmsiSceneryMeshReference> Meshes,
    IReadOnlyList<OmsiSceneryMaterialOverride> MaterialOverrides,
    OmsiSceneryTreeDefinition? Tree,
    IReadOnlyList<OmsiSceneryPathDefinition> Paths,
    double? TrafficLightCycleSeconds = null,
    IReadOnlyList<OmsiSceneryTrafficLightProgram>? TrafficLights = null,
    OmsiSceneryScriptManifest? ScriptManifest = null)
{
    public static OmsiSceneryDefinition Missing { get; } =
        new(
            false,
            false,
            false,
            null,
            Array.Empty<OmsiSceneryMeshReference>(),
            Array.Empty<OmsiSceneryMaterialOverride>(),
            null,
            Array.Empty<OmsiSceneryPathDefinition>());
}
