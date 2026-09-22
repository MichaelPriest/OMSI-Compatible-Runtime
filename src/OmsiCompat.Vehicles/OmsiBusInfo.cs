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
    IReadOnlyList<OmsiDriverCamera> DriverCameras,
    IReadOnlyList<OmsiPassengerCamera> PassengerCameras,
    int StandardDriverCameraIndex,
    OmsiOutsideCameraCenter? OutsideCameraCenter)
{
    public override string ToString() => DisplayName;
}
