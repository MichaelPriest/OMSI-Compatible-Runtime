using System.Globalization;
using OmsiCompat.Map;

namespace OmsiCompat.Scenery;

public static class OmsiSceneryObjectReader
{
    public static OmsiSceneryDefinition ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return OmsiSceneryDefinition.Missing;
        }

        var document =
            OmsiSectionDocument.ParseFile(path);

        var meshes =
            new List<OmsiSceneryMeshReference>();

        double? currentLod = null;
        var currentMesh = -1;

        foreach (var section in document.Sections)
        {
            if (section.Name.Equals(
                    "LOD",
                    StringComparison.OrdinalIgnoreCase))
            {
                currentLod = null;

                var value =
                    Data(section)
                        .FirstOrDefault()
                        ?.Value;

                if (TryDouble(
                        value,
                        out var threshold) &&
                    threshold >= 0)
                {
                    currentLod = threshold;
                }

                continue;
            }

            if (section.Name.Equals(
                    "mesh",
                    StringComparison.OrdinalIgnoreCase))
            {
                var meshPath =
                    Data(section)
                        .FirstOrDefault()
                        ?.Value
                        .Trim()
                        .Trim('"');

                if (string.IsNullOrWhiteSpace(
                        meshPath))
                {
                    currentMesh = -1;
                    continue;
                }

                meshes.Add(
                    new OmsiSceneryMeshReference(
                        meshPath,
                        currentLod,
                        OmsiSceneryMeshTransform.Identity));

                currentMesh =
                    meshes.Count - 1;

                continue;
            }

            if (currentMesh < 0 ||
                currentMesh >= meshes.Count)
            {
                continue;
            }

            var current = meshes[currentMesh];
            var transform = current.Transform;

            if (section.Name.Equals(
                    "new_pos",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Data(section)
                        .Take(3)
                        .Select(static line => line.Value)
                        .ToArray();

                if (values.Length == 3 &&
                    TryDouble(values[0], out var x) &&
                    TryDouble(values[1], out var y) &&
                    TryDouble(values[2], out var z))
                {
                    transform =
                        transform with
                        {
                            PositionX = x,
                            PositionY = y,
                            PositionZ = z
                        };
                }
            }
            else if (section.Name.Equals(
                         "rot_x",
                         StringComparison.OrdinalIgnoreCase) ||
                     section.Name.Equals(
                         "rotx",
                         StringComparison.OrdinalIgnoreCase))
            {
                if (TryDouble(
                        Data(section)
                            .FirstOrDefault()
                            ?.Value,
                        out var value))
                {
                    transform =
                        transform with
                        {
                            RotationX = value
                        };
                }
            }
            else if (section.Name.Equals(
                         "rot_y",
                         StringComparison.OrdinalIgnoreCase) ||
                     section.Name.Equals(
                         "roty",
                         StringComparison.OrdinalIgnoreCase))
            {
                if (TryDouble(
                        Data(section)
                            .FirstOrDefault()
                            ?.Value,
                        out var value))
                {
                    transform =
                        transform with
                        {
                            RotationY = value
                        };
                }
            }
            else if (section.Name.Equals(
                         "rot_z",
                         StringComparison.OrdinalIgnoreCase) ||
                     section.Name.Equals(
                         "rotz",
                         StringComparison.OrdinalIgnoreCase))
            {
                if (TryDouble(
                        Data(section)
                            .FirstOrDefault()
                            ?.Value,
                        out var value))
                {
                    transform =
                        transform with
                        {
                            RotationZ = value
                        };
                }
            }
            else if (section.Name.Equals(
                         "scale",
                         StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Data(section)
                        .Select(static line => line.Value)
                        .ToArray();

                if (values.Length > 0 &&
                    TryDouble(
                        values[0],
                        out var scaleX))
                {
                    var scaleY = scaleX;
                    var scaleZ = scaleX;

                    if (values.Length >= 3 &&
                        TryDouble(
                            values[1],
                            out var parsedY) &&
                        TryDouble(
                            values[2],
                            out var parsedZ))
                    {
                        scaleY = parsedY;
                        scaleZ = parsedZ;
                    }

                    transform =
                        transform with
                        {
                            ScaleX = scaleX,
                            ScaleY = scaleY,
                            ScaleZ = scaleZ
                        };
                }
            }

            meshes[currentMesh] =
                current with
                {
                    Transform = transform
                };
        }

        var renderType =
            document.Sections
                .FirstOrDefault(
                    static section =>
                        section.Name.Equals(
                            "rendertype",
                            StringComparison.OrdinalIgnoreCase));

        var renderTypeValue =
            renderType is null
                ? null
                : Data(renderType)
                    .FirstOrDefault()
                    ?.Value;

        return new OmsiSceneryDefinition(
            true,
            document.Sections.Any(
                static section =>
                    section.Name.Equals(
                        "absheight",
                        StringComparison.OrdinalIgnoreCase)),
            string.IsNullOrWhiteSpace(
                renderTypeValue)
                ? null
                : renderTypeValue.Trim(),
            meshes.ToArray());
    }

    private static IReadOnlyList<OmsiSectionLine> Data(
        OmsiSection section) =>
        section.Lines
            .Where(
                static line =>
                {
                    var value = line.Value.Trim();
                    return value.Length > 0 &&
                           !value.StartsWith('#');
                })
            .ToArray();

    private static bool TryDouble(
        string? value,
        out double result) =>
        double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) &&
        double.IsFinite(result);
}
