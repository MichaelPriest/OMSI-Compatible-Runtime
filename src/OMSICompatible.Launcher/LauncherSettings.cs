using System.Text.Json;

namespace OMSICompatible.Launcher;

internal sealed record LauncherSettings(
    string? ContentPath,
    string? MapName)
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
                    null);
            }

            return JsonSerializer.Deserialize<LauncherSettings>(
                       File.ReadAllText(SettingsPath))
                   ?? new LauncherSettings(
                       null,
                       null);
        }
        catch
        {
            return new LauncherSettings(
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
