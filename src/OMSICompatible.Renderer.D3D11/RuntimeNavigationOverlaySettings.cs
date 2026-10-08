using System.Text.Json;

namespace OMSICompatible.Renderer.D3D11;

/// <summary>
/// Optional in-game presentation controls. All new overlays start disabled
/// so loading a stock OMSI bus or map preserves the original experience.
/// </summary>
internal sealed record RuntimeNavigationOverlaySettings
{
    public bool MiniMapEnabled { get; init; }
    public bool LiveBoardEnabled { get; init; }
    public bool GroundArrowsEnabled { get; init; }

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OMSI-Compatible-Runtime",
            "navigation-overlays.json");

    public static RuntimeNavigationOverlaySettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<RuntimeNavigationOverlaySettings>(
                    File.ReadAllText(SettingsPath)) ?? new();
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine(
                $"[navigation-overlays] Preferences could not be read: {exception.Message}");
        }

        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + ".tmp";
            try
            {
                File.WriteAllText(
                    temporary,
                    JsonSerializer.Serialize(
                        this,
                        new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, SettingsPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"[navigation-overlays] Preferences could not be saved: {exception.Message}");
        }
    }
}
