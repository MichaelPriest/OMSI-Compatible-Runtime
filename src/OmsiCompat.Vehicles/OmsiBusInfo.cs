namespace OmsiCompat.Vehicles;

public sealed record OmsiDriverCamera(
    double X,
    double Y,
    double Z,
    double EyeDistance,
    double FieldOfViewDegrees,
    double HeadingDegrees,
    double PitchDegrees);

public sealed record OmsiPassengerCamera(
    double X,
    double Y,
    double Z,
    double EyeDistance,
    double FieldOfViewDegrees,
    double HeadingDegrees,
    double PitchDegrees);

public sealed record OmsiOutsideCameraCenter(
    double X,
    double Y,
    double Z);

public sealed record OmsiReflectionCamera(
    int Index,
    double X,
    double Y,
    double Z,
    double EyeDistance,
    double FieldOfViewDegrees,
    double HeadingDegrees,
    double PitchDegrees,
    double? MaximumRenderDistanceMeters)
{
    public string RuntimeTextureName =>
        $"reflexion{Index}.bmp";

    public string RuntimeTextureKey =>
        $"runtime-reflection://{Index}";
}

public sealed record OmsiVehicleFileReference(
    string DeclaredPath,
    string? ResolvedPath)
{
    public bool Exists =>
        ResolvedPath is not null;
}

public sealed record OmsiVehicleScriptManifest(
    IReadOnlyList<OmsiVehicleFileReference> ScriptFiles,
    IReadOnlyList<OmsiVehicleFileReference> VariableLists,
    IReadOnlyList<OmsiVehicleFileReference> StringVariableLists,
    IReadOnlyList<OmsiVehicleFileReference> ConstantFiles)
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

public sealed record OmsiVehicleAxle(
    double LongitudinalPositionMeters,
    double? WheelDiameterMeters,
    double? DriveFactor);

public sealed record OmsiVehiclePhysics(
    IReadOnlyList<OmsiVehicleAxle> Axles,
    double? RotationPointLongitudinalMeters,
    double? InverseMinimumTurnRadius)
{
    public double? WheelBaseMeters
    {
        get
        {
            if (Axles.Count < 2)
            {
                return null;
            }

            var minimum =
                Axles.Min(
                    static axle =>
                        axle.LongitudinalPositionMeters);

            var maximum =
                Axles.Max(
                    static axle =>
                        axle.LongitudinalPositionMeters);

            var span =
                maximum - minimum;

            return double.IsFinite(span) &&
                   span > 0.5
                ? span
                : null;
        }
    }

    public double? MaximumSteeringAngleDegrees
    {
        get
        {
            if (!RotationPointLongitudinalMeters.HasValue ||
                !InverseMinimumTurnRadius.HasValue ||
                Axles.Count == 0)
            {
                return null;
            }

            var rotationPoint =
                RotationPointLongitudinalMeters.Value;

            var steeringDistance =
                Axles.Max(
                    axle =>
                        Math.Abs(
                            axle.LongitudinalPositionMeters -
                            rotationPoint));

            if (!double.IsFinite(steeringDistance) ||
                steeringDistance <= 0.1)
            {
                return null;
            }

            var radians =
                Math.Atan(
                    Math.Abs(
                        InverseMinimumTurnRadius.Value) *
                    steeringDistance);

            var degrees =
                radians *
                180.0 /
                Math.PI;

            return double.IsFinite(degrees) &&
                   degrees > 0.0
                ? degrees
                : null;
        }
    }
}

public sealed record OmsiBusInfo(
    string DisplayName,
    string FilePath,
    string RelativePath,
    string DirectoryPath,
    IReadOnlyList<string> FriendlyNameLines,
    string? ModelConfigPath,
    string? PassengerCabinPath,
    string? PathConfigPath,
    string? SoundConfigPath,
    OmsiVehicleScriptManifest ScriptManifest,
    IReadOnlyList<OmsiDriverCamera> DriverCameras,
    IReadOnlyList<OmsiPassengerCamera> PassengerCameras,
    int StandardDriverCameraIndex,
    int? ScheduleDriverCameraIndex,
    int? TicketSellingDriverCameraIndex,
    OmsiOutsideCameraCenter? OutsideCameraCenter,
    IReadOnlyList<OmsiReflectionCamera> ReflectionCameras,
    OmsiVehiclePhysics Physics)
{
    public override string ToString() => DisplayName;
}
