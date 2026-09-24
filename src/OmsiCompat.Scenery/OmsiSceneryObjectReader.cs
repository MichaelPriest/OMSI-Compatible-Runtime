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
            document.Sections.Any(
                static section =>
                    section.Name.Equals(
                        "onlyeditor",
                        StringComparison.OrdinalIgnoreCase)),
            string.IsNullOrWhiteSpace(
                renderTypeValue)
                ? null
                : renderTypeValue.Trim(),
            meshes.ToArray(),
            ReadMaterialOverrides(document),
            ReadTree(document));
    }

    private static IReadOnlyList<OmsiSceneryMaterialOverride>
        ReadMaterialOverrides(
            OmsiSectionDocument document)
    {
        var builders =
            new List<MaterialOverrideBuilder>();

        var meshOrdinal = -1;
        MaterialOverrideBuilder? current = null;

        foreach (var section in document.Sections)
        {
            if (section.Name.Equals(
                    "mesh",
                    StringComparison.OrdinalIgnoreCase))
            {
                meshOrdinal++;
                current = null;
                continue;
            }

            if (section.Name.Equals(
                    "matl_change",
                    StringComparison.OrdinalIgnoreCase))
            {
                current = null;
                continue;
            }

            if (section.Name.Equals(
                    "matl",
                    StringComparison.OrdinalIgnoreCase))
            {
                current = null;

                if (meshOrdinal < 0)
                {
                    continue;
                }

                var values =
                    Data(section)
                        .Select(static line => line.Value)
                        .ToArray();

                if (values.Length < 2 ||
                    string.IsNullOrWhiteSpace(values[0]) ||
                    !int.TryParse(
                        values[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var materialIndex) ||
                    materialIndex < 0)
                {
                    continue;
                }

                current =
                    new MaterialOverrideBuilder(
                        meshOrdinal,
                        values[0].Trim().Trim('"'),
                        materialIndex);

                builders.Add(current);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (section.Name.Equals(
                    "matl_alpha",
                    StringComparison.OrdinalIgnoreCase))
            {
                var value =
                    Data(section)
                        .FirstOrDefault()
                        ?.Value;

                if (int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var alphaMode) &&
                    alphaMode is >= 0 and <= 2)
                {
                    current.AlphaMode =
                        alphaMode;
                }

                continue;
            }

            if (section.Name.Equals(
                    "matl_transmap",
                    StringComparison.OrdinalIgnoreCase))
            {
                var raw =
                    Data(section)
                        .FirstOrDefault()
                        ?.Value;

                var value =
                    raw?
                        .Trim()
                        .Trim('"');

                current.TransMapSource =
                    string.IsNullOrWhiteSpace(
                        value)
                        ? null
                        : value;

                continue;
            }

            if (section.Name.Equals(
                    "matl_noZwrite",
                    StringComparison.OrdinalIgnoreCase))
            {
                current.NoZWrite = true;
                continue;
            }

            if (section.Name.Equals(
                    "matl_noZcheck",
                    StringComparison.OrdinalIgnoreCase))
            {
                current.NoZCheck = true;
            }
        }

        return builders
            .Select(
                static builder =>
                    builder.ToImmutable())
            .ToArray();
    }

    private sealed class MaterialOverrideBuilder(
        int meshOrdinal,
        string textureName,
        int materialIndex)
    {
        public int MeshOrdinal { get; } =
            meshOrdinal;

        public string TextureName { get; } =
            textureName;

        public int MaterialIndex { get; } =
            materialIndex;

        public int? AlphaMode { get; set; }

        public string? TransMapSource { get; set; }

        public bool NoZWrite { get; set; }

        public bool NoZCheck { get; set; }

        public OmsiSceneryMaterialOverride
            ToImmutable() =>
            new(
                MeshOrdinal,
                TextureName,
                MaterialIndex,
                AlphaMode,
                TransMapSource,
                NoZWrite,
                NoZCheck);
    }

    private static OmsiSceneryTreeDefinition? ReadTree(
        OmsiSectionDocument document)
    {
        var section =
            document.Sections.FirstOrDefault(
                static item =>
                    item.Name.Equals(
                        "tree",
                        StringComparison.OrdinalIgnoreCase));

        if (section is null)
        {
            return null;
        }

        var values =
            Data(section)
                .Select(static line => line.Value)
                .ToArray();

        if (values.Length < 5 ||
            string.IsNullOrWhiteSpace(values[0]) ||
            !TryDouble(values[1], out var minimumHeight) ||
            !TryDouble(values[2], out var maximumHeight) ||
            !TryDouble(values[3], out var minimumAspect) ||
            !TryDouble(values[4], out var maximumAspect) ||
            minimumHeight <= 0 ||
            maximumHeight < minimumHeight ||
            minimumAspect <= 0 ||
            maximumAspect < minimumAspect)
        {
            return null;
        }

        return new OmsiSceneryTreeDefinition(
            values[0].Trim().Trim('"'),
            minimumHeight,
            maximumHeight,
            minimumAspect,
            maximumAspect);
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
