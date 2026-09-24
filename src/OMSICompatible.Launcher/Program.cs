namespace OMSICompatible.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var explicitContent =
            GetOption(
                args,
                "--content");

        Application.Run(
            new LauncherForm(
                explicitContent));
    }

    private static string? GetOption(
        IReadOnlyList<string> args,
        string name)
    {
        for (var index = 0;
             index < args.Count - 1;
             index++)
        {
            if (string.Equals(
                    args[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
