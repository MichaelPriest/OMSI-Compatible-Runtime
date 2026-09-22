namespace OmsiCompat.Vehicles;

public sealed record OmsiBusInfo(
    string DisplayName,
    string FilePath,
    string RelativePath,
    string DirectoryPath,
    IReadOnlyList<string> FriendlyNameLines,
    string? ModelConfigPath,
    string? PassengerCabinPath,
    string? PathConfigPath,
    string? SoundConfigPath)
{
    public override string ToString() => DisplayName;
}
