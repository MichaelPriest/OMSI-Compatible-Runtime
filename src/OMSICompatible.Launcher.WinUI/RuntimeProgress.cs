namespace OMSICompatible.Launcher.WinUI;

internal sealed record RuntimeProgress(
    int Percent,
    string Stage,
    string Detail)
{
    private const string Prefix =
        "[runtime-progress]|";

    public static bool TryParse(
        string line,
        out RuntimeProgress? progress)
    {
        progress = null;

        if (!line.StartsWith(
                Prefix,
                StringComparison.Ordinal))
        {
            return false;
        }

        var parts =
            line[Prefix.Length..]
                .Split(
                    '|',
                    3,
                    StringSplitOptions.None);

        if (parts.Length != 3 ||
            !int.TryParse(
                parts[0],
                out var percent))
        {
            return false;
        }

        progress =
            new RuntimeProgress(
                Math.Clamp(
                    percent,
                    0,
                    100),
                parts[1],
                parts[2]);

        return true;
    }
}
