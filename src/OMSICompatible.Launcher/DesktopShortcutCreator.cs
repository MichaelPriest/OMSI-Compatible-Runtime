using System.Diagnostics;

namespace OMSICompatible.Launcher;

internal static class DesktopShortcutCreator
{
    public static string Create()
    {
        var launcherPath =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(launcherPath) ||
            !File.Exists(launcherPath))
        {
            throw new InvalidOperationException(
                "Não foi possível localizar o executável do launcher.");
        }

        var desktop =
            Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);

        var shortcutPath =
            Path.Combine(
                desktop,
                "OMSI Compatible Runtime x64.lnk");

        var escapedShortcut =
            EscapePowerShellLiteral(shortcutPath);
        var escapedTarget =
            EscapePowerShellLiteral(launcherPath);
        var escapedWorkingDirectory =
            EscapePowerShellLiteral(
                Path.GetDirectoryName(launcherPath)
                ?? AppContext.BaseDirectory);

        var command =
            "$ws=New-Object -ComObject WScript.Shell;" +
            $"$s=$ws.CreateShortcut('{escapedShortcut}');" +
            $"$s.TargetPath='{escapedTarget}';" +
            $"$s.WorkingDirectory='{escapedWorkingDirectory}';" +
            "$s.Description='OMSI Compatible Runtime x64';" +
            $"$s.IconLocation='{escapedTarget},0';" +
            "$s.Save();";

        using var process =
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        "-NoProfile -NonInteractive -Command "" +
                        command.Replace(
                            """,
                            "\"",
                            StringComparison.Ordinal) +
                        """,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

        process?.WaitForExit();

        if (!File.Exists(shortcutPath))
        {
            throw new InvalidOperationException(
                "O Windows não conseguiu criar o atalho.");
        }

        return shortcutPath;
    }

    private static string EscapePowerShellLiteral(
        string value) =>
        value.Replace(
            "'",
            "''",
            StringComparison.Ordinal);
}
