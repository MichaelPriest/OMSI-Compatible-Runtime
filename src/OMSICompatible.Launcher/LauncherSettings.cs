using System.Text.Json;

namespace OMSICompatible.Launcher;

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
    private static string SettingsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "OMSI-Compatible-Runtime");

    private static string SettingsPath =>
        Path.Combine(
            SettingsDirectory,
            "launcher-settings.json");

    public static LauncherSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new LauncherSettings(
                    null,
                    null,
                    null,
                    null);
            }

            return JsonSerializer.Deserialize<LauncherSettings>(
                       File.ReadAllText(SettingsPath))
                   ?? new LauncherSettings(
                       null,
                       null,
                       null,
                       null);
        }
        catch
        {
            return new LauncherSettings(
                null,
                null,
                null,
                null);
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(
                SettingsDirectory);

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
}
