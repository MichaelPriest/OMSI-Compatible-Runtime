namespace OmsiCompat.Map;

public sealed record OmsiMapInfo(
    string FolderName,
    string DirectoryPath,
    string GlobalConfigPath,
    long GlobalConfigBytes);
