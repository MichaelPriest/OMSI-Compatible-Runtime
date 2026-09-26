using System.Text.Json;

namespace OMSICompatible.Launcher.WinUI;

internal sealed record LauncherSettings(
    string? ContentPath,
    string? MapName,
    string? BusRelativePath,
    string? EntryPointName,
    bool StartWithoutBus = false,
    string? RepaintName = null,
    string? RepaintCtiRelativePath = null,
    string? LastSessionMapName = null,
    string? LastSessionBusRelativePath = null,
    string? LastSessionSkin = null,
    string? LastSessionRepaintName = null,
    string? LastSessionRepaintCtiRelativePath = null,
    string? LastSessionEntryPointName = null,
    bool LastSessionWithoutBus = false,
    DateTimeOffset? LastSessionStartedAt = null)
{
    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "OMSI-Compatible-Runtime",
            "launcher-settings.json");

    public static LauncherSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<LauncherSettings>(
                      File.ReadAllText(SettingsPath))
                  ?? Empty
                : Empty;
        }
        catch
        {
            return Empty;
        }
    }

    public void Save()
    {
        try
        {
            var directory =
                Path.GetDirectoryName(
                    SettingsPath)!;

            Directory.CreateDirectory(
                directory);

            File.WriteAllText(
                SettingsPath,
                JsonSerializer.Serialize(
                    this,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch
        {
        }
    }

    private static LauncherSettings Empty =>
        new(
            null,
            null,
            null,
            null);
}
