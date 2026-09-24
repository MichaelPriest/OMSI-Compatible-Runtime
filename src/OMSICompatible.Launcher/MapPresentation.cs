using OmsiCompat.Core;
using OmsiCompat.Map;

namespace OMSICompatible.Launcher;

internal sealed record MapPresentation(
    string Title,
    string Description,
    string? ImagePath);

internal static class MapPresentationReader
{
    private static readonly string[] ImageNames =
    [
        "picture.jpg",
        "picture.jpeg",
        "picture.png",
        "picture.bmp",
        "preview.jpg",
        "preview.png"
    ];

    public static MapPresentation Read(
        OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var title = map.FolderName;
        var description =
            "Mapa OMSI compatível selecionado.";

        try
        {
            var lines =
                OmsiText.ReadAllLines(
                    map.GlobalConfigPath);

            title =
                ReadSingleValue(
                    lines,
                    "friendlyname")
                ?? ReadSingleValue(
                    lines,
                    "name")
                ?? map.FolderName;

            description =
                ReadDescription(lines)
                ?? description;
        }
        catch
        {
        }

        string? imagePath = null;

        foreach (var fileName in ImageNames)
        {
            var candidate =
                Path.Combine(
                    map.DirectoryPath,
                    fileName);

            if (File.Exists(candidate))
            {
                imagePath = candidate;
                break;
            }
        }

        return new MapPresentation(
            title.Trim(),
            description.Trim(),
            imagePath);
    }

    private static string? ReadSingleValue(
        IReadOnlyList<string> lines,
        string section)
    {
        for (var index = 0;
             index < lines.Count - 1;
             index++)
        {
            if (!string.Equals(
                    lines[index].Trim(),
                    $"[{section}]",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (var next = index + 1;
                 next < lines.Count;
                 next++)
            {
                var value =
                    lines[next].Trim();

                if (value.StartsWith('[') &&
                    value.EndsWith(']'))
                {
                    return null;
                }

                if (value.Length > 0 &&
                    !value.StartsWith('#'))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? ReadDescription(
        IReadOnlyList<string> lines)
    {
        var values =
            new List<string>();

        var reading = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (!reading)
            {
                reading =
                    line.Equals(
                        "[description]",
                        StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.Equals(
                    "[end]",
                    StringComparison.OrdinalIgnoreCase) ||
                (line.StartsWith('[') &&
                 line.EndsWith(']')))
            {
                break;
            }

            if (line.Length > 0)
            {
                values.Add(line);
            }
        }

        return values.Count == 0
            ? null
            : string.Join(
                Environment.NewLine,
                values.Take(6));
    }
}
