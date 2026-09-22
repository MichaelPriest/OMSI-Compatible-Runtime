using System.Globalization;
using OmsiCompat.Map;

namespace OmsiCompat.Vehicles;

public static class OmsiBusReader
{
    public static OmsiBusInfo ReadFile(
        string omsiRoot,
        string busFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(omsiRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(busFilePath);

        var fullPath = Path.GetFullPath(busFilePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("Bus file has no parent directory.");

        var document = OmsiSectionDocument.ParseFile(fullPath);
        var friendly = Values(document, "friendlyname").Take(3).ToArray();

        var displayName = friendly.Length switch
        {
            >= 2 => $"{friendly[0]} — {friendly[1]}",
            1 => friendly[0],
            _ => Path.GetFileNameWithoutExtension(fullPath)
        };

        var root = Path.GetFullPath(omsiRoot);
        var relative = Path.GetRelativePath(root, fullPath);

        return new OmsiBusInfo(
            displayName,
            fullPath,
            relative,
            directory,
            friendly,
            ResolveRelative(directory, First(document, "model")),
            ResolveRelative(directory, First(document, "passengercabin")),
            ResolveRelative(directory, First(document, "paths")),
            ResolveRelative(directory, First(document, "sound")),
            ReadDriverCameras(document),
            ReadPassengerCameras(document),
            ReadStandardDriverCameraIndex(document),
            ReadOutsideCameraCenter(document));
    }

    private static IReadOnlyList<OmsiDriverCamera>
        ReadDriverCameras(
            OmsiSectionDocument document)
    {
        var result =
            new List<OmsiDriverCamera>();

        foreach (var section in
                 document.Sections.Where(
                     static section =>
                         section.Name.Equals(
                             "add_camera_driver",
                             StringComparison.OrdinalIgnoreCase)))
        {
            var values =
                section.Lines
                    .Select(
                        static line =>
                            line.Value.Trim())
                    .Where(
                        static value =>
                            value.Length > 0 &&
                            !value.StartsWith('#') &&
                            !value.StartsWith(
                                "//",
                                StringComparison.Ordinal))
                    .Take(7)
                    .ToArray();

            if (values.Length < 7 ||
                !TryDouble(values[0], out var x) ||
                !TryDouble(values[1], out var y) ||
                !TryDouble(values[2], out var z) ||
                !TryDouble(values[3], out var eyeDistance) ||
                !TryDouble(values[4], out var fieldOfView) ||
                !TryDouble(values[5], out var heading) ||
                !TryDouble(values[6], out var pitch))
            {
                continue;
            }

            result.Add(
                new OmsiDriverCamera(
                    x,
                    y,
                    z,
                    eyeDistance,
                    fieldOfView,
                    heading,
                    pitch));
        }

        return result;
    }

    private static IReadOnlyList<OmsiPassengerCamera>
        ReadPassengerCameras(
            OmsiSectionDocument document)
    {
        var result =
            new List<OmsiPassengerCamera>();

        foreach (var section in
                 document.Sections.Where(
                     static section =>
                         section.Name.Equals(
                             "add_camera_pax",
                             StringComparison.OrdinalIgnoreCase)))
        {
            var values =
                section.Lines
                    .Select(
                        static line =>
                            line.Value.Trim())
                    .Where(
                        static value =>
                            value.Length > 0 &&
                            !value.StartsWith('#') &&
                            !value.StartsWith(
                                "//",
                                StringComparison.Ordinal))
                    .Take(7)
                    .ToArray();

            if (values.Length < 7 ||
                !TryDouble(values[0], out var x) ||
                !TryDouble(values[1], out var y) ||
                !TryDouble(values[2], out var z) ||
                !TryDouble(values[3], out var eyeDistance) ||
                !TryDouble(values[4], out var fieldOfView) ||
                !TryDouble(values[5], out var heading) ||
                !TryDouble(values[6], out var pitch))
            {
                continue;
            }

            result.Add(
                new OmsiPassengerCamera(
                    x,
                    y,
                    z,
                    eyeDistance,
                    fieldOfView,
                    heading,
                    pitch));
        }

        return result;
    }

    private static int ReadStandardDriverCameraIndex(
        OmsiSectionDocument document)
    {
        var value =
            First(
                document,
                "set_camera_std");

        return int.TryParse(
                   value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var index) &&
               index >= 0
            ? index
            : 0;
    }

    private static OmsiOutsideCameraCenter?
        ReadOutsideCameraCenter(
            OmsiSectionDocument document)
    {
        var section =
            document.Sections.FirstOrDefault(
                static section =>
                    section.Name.Equals(
                        "set_camera_outside_center",
                        StringComparison.OrdinalIgnoreCase));

        if (section is null)
        {
            return null;
        }

        var values =
            section.Lines
                .Select(
                    static line =>
                        line.Value.Trim())
                .Where(
                    static value =>
                        value.Length > 0 &&
                        !value.StartsWith('#') &&
                        !value.StartsWith(
                            "//",
                            StringComparison.Ordinal))
                .Take(3)
                .ToArray();

        if (values.Length < 3 ||
            !TryDouble(values[0], out var x) ||
            !TryDouble(values[1], out var y) ||
            !TryDouble(values[2], out var z))
        {
            return null;
        }

        return new OmsiOutsideCameraCenter(
            x,
            y,
            z);
    }

    private static bool TryDouble(
        string value,
        out double result) =>
        double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) &&
        double.IsFinite(result);

    private static string? First(
        OmsiSectionDocument document,
        string name) =>
        Values(document, name).FirstOrDefault();

    private static IEnumerable<string> Values(
        OmsiSectionDocument document,
        string name)
    {
        var section = document.Sections.FirstOrDefault(
            item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        return section is null
            ? Array.Empty<string>()
            : section.Lines
                .Select(static line => line.Value.Trim().Trim('"'))
                .Where(static value =>
                    value.Length > 0 &&
                    !value.StartsWith('#') &&
                    !value.StartsWith("//", StringComparison.Ordinal));
    }

    private static string? ResolveRelative(
        string baseDirectory,
        string? declaredPath)
    {
        if (string.IsNullOrWhiteSpace(declaredPath))
        {
            return null;
        }

        var normalized = declaredPath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Trim();

        if (Path.IsPathRooted(normalized))
        {
            return null;
        }

        try
        {
            var candidate = Path.GetFullPath(Path.Combine(baseDirectory, normalized));
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }
}
