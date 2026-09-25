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

        var trafficLightCycleSeconds =
            ReadTrafficLightCycleSeconds(
                document);

        var trafficLights =
            ReadTrafficLights(
                document);

        var scriptManifest =
            ReadScriptManifest(
                document,
                Path.GetDirectoryName(
                    Path.GetFullPath(
                        path)) ??
                string.Empty);

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
            ReadTree(document),
            ReadPaths(document),
            trafficLightCycleSeconds,
            trafficLights,
            scriptManifest);
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

    private static IReadOnlyList<OmsiSceneryPathDefinition>
        ReadPaths(
            OmsiSectionDocument document)
    {
        var result =
            new List<OmsiSceneryPathDefinition>();

        for (var sectionIndex = 0;
             sectionIndex <
                 document.Sections.Count;
             sectionIndex++)
        {
            var section =
                document.Sections[
                    sectionIndex];

            if (!section.Name.Equals(
                    "path",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values =
                Data(section)
                    .Select(
                        static line =>
                            line.Value)
                    .ToArray();

            // Crossing-editor paths store their geometric spline followed
            // by path type/width/direction. Some OMSI versions/tools append
            // additional numeric flags; keep those raw rather than guessing
            // their meaning.
            if (values.Length < 11 ||
                !TryDouble(values[0], out var x) ||
                !TryDouble(values[1], out var y) ||
                !TryDouble(values[2], out var z) ||
                !TryDouble(values[3], out var heading) ||
                !TryDouble(values[4], out var radius) ||
                !TryDouble(values[5], out var length) ||
                !TryDouble(values[6], out var gradientStart) ||
                !TryDouble(values[7], out var gradientEnd) ||
                !int.TryParse(
                    values[8],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var type) ||
                !TryDouble(values[9], out var width) ||
                !int.TryParse(
                    values[10],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var direction) ||
                type is < 0 or > 3 ||
                direction is < 0 or > 2 ||
                length < 0.0 ||
                width < 0.0)
            {
                continue;
            }

            result.Add(
                new OmsiSceneryPathDefinition(
                    x,
                    y,
                    z,
                    heading,
                    radius,
                    length,
                    gradientStart,
                    gradientEnd,
                    type,
                    width,
                    direction,
                    values
                        .Skip(11)
                        .ToArray(),
                    ReadPathTrafficLightIndex(
                        document,
                        sectionIndex)));
        }

        return result;
    }

    private static int? ReadPathTrafficLightIndex(
        OmsiSectionDocument document,
        int pathSectionIndex)
    {
        for (var index =
                 pathSectionIndex + 1;
             index <
                 document.Sections.Count;
             index++)
        {
            var section =
                document.Sections[index];

            if (section.Name.Equals(
                    "path",
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!section.Name.Equals(
                    "use_traffic_light",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value =
                Data(section)
                    .FirstOrDefault()
                    ?.Value;

            return int.TryParse(
                       value,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out var trafficLightIndex) &&
                   trafficLightIndex >=
                       0
                ? trafficLightIndex
                : null;
        }

        return null;
    }

    private static double? ReadTrafficLightCycleSeconds(
        OmsiSectionDocument document)
    {
        var section =
            document.Sections
                .FirstOrDefault(
                    static item =>
                        item.Name.Equals(
                            "traffic_lights_group",
                            StringComparison.OrdinalIgnoreCase));

        if (section is null ||
            !TryDouble(
                Data(section)
                    .FirstOrDefault()
                    ?.Value,
                out var cycleSeconds) ||
            cycleSeconds <=
                0.0)
        {
            return null;
        }

        return cycleSeconds;
    }

    private static IReadOnlyList<OmsiSceneryTrafficLightProgram>
        ReadTrafficLights(
            OmsiSectionDocument document)
    {
        var result =
            new List<OmsiSceneryTrafficLightProgram>();

        var inGroup =
            false;

        TrafficLightBuilder? current =
            null;

        foreach (var section in
                 document.Sections)
        {
            if (section.Name.Equals(
                    "traffic_lights_group",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (inGroup)
                {
                    FinalizeTrafficLight(
                        current,
                        result);
                    break;
                }

                inGroup =
                    true;
                continue;
            }

            if (!inGroup)
            {
                continue;
            }

            if (section.Name.Equals(
                    "traffic_light",
                    StringComparison.OrdinalIgnoreCase))
            {
                FinalizeTrafficLight(
                    current,
                    result);

                var name =
                    Data(section)
                        .FirstOrDefault()
                        ?.Value
                        .Trim();

                current =
                    string.IsNullOrWhiteSpace(
                        name)
                        ? null
                        : new TrafficLightBuilder(
                            name);

                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (section.Name.Equals(
                    "phase",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Data(section)
                        .Select(
                            static line =>
                                line.Value)
                        .ToArray();

                if (values.Length >=
                        2 &&
                    int.TryParse(
                        values[0],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var phase) &&
                    TryDouble(
                        values[1],
                        out var durationSeconds) &&
                    durationSeconds >
                        0.0)
                {
                    current.Phases.Add(
                        new OmsiSceneryTrafficLightPhase(
                            phase,
                            durationSeconds));
                }

                continue;
            }

            if (section.Name.Equals(
                    "approachdist",
                    StringComparison.OrdinalIgnoreCase) &&
                TryDouble(
                    Data(section)
                        .FirstOrDefault()
                        ?.Value,
                    out var approachDistance) &&
                approachDistance >=
                    0.0)
            {
                current.ApproachDistanceMeters =
                    approachDistance;
            }
        }

        FinalizeTrafficLight(
            current,
            result);

        return result;
    }

    private static void FinalizeTrafficLight(
        TrafficLightBuilder? builder,
        ICollection<OmsiSceneryTrafficLightProgram> target)
    {
        if (builder is null ||
            builder.Phases.Count ==
                0)
        {
            return;
        }

        target.Add(
            new OmsiSceneryTrafficLightProgram(
                builder.Name,
                builder.Phases.ToArray(),
                builder.ApproachDistanceMeters));
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

    private sealed class TrafficLightBuilder(
        string name)
    {
        public string Name { get; } =
            name;

        public List<OmsiSceneryTrafficLightPhase> Phases { get; } =
            [];

        public double ApproachDistanceMeters
        {
            get;
            set;
        }
    }

    private static OmsiSceneryScriptManifest
        ReadScriptManifest(
        OmsiSectionDocument document,
        string baseDirectory) =>
        new(
            ReadRegisteredFiles(
                document,
                baseDirectory,
                "script"),
            ReadRegisteredFiles(
                document,
                baseDirectory,
                "varnamelist"),
            ReadRegisteredFiles(
                document,
                baseDirectory,
                "stringvarnamelist"),
            ReadRegisteredFiles(
                document,
                baseDirectory,
                "constfile"));

    private static IReadOnlyList<OmsiSceneryFileReference>
        ReadRegisteredFiles(
        OmsiSectionDocument document,
        string baseDirectory,
        string sectionName)
    {
        var result =
            new List<OmsiSceneryFileReference>();

        foreach (var section in
                 document.Sections.Where(
                     section =>
                         section.Name.Equals(
                             sectionName,
                             StringComparison.OrdinalIgnoreCase)))
        {
            var values =
                Data(section)
                    .Select(
                        static line =>
                            line.Value
                                .Trim()
                                .Trim('"'))
                    .Where(
                        static value =>
                            value.Length >
                            0)
                    .ToArray();

            if (values.Length ==
                0)
            {
                continue;
            }

            var firstPathIndex =
                0;

            var declaredCount =
                values.Length;

            if (int.TryParse(
                    values[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedCount) &&
                parsedCount >=
                    0)
            {
                firstPathIndex =
                    1;

                declaredCount =
                    Math.Min(
                        parsedCount,
                        Math.Max(
                            values.Length -
                                1,
                            0));
            }

            for (var index = 0;
                 index <
                     declaredCount;
                 index++)
            {
                var declaredPath =
                    values[
                        firstPathIndex +
                        index];

                result.Add(
                    new OmsiSceneryFileReference(
                        declaredPath,
                        ResolveRelativeFile(
                            baseDirectory,
                            declaredPath)));
            }
        }

        return result;
    }

    private static string? ResolveRelativeFile(
        string baseDirectory,
        string declaredPath)
    {
        if (string.IsNullOrWhiteSpace(
                baseDirectory) ||
            string.IsNullOrWhiteSpace(
                declaredPath))
        {
            return null;
        }

        try
        {
            var normalized =
                declaredPath
                    .Trim()
                    .Trim('"')
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar);

            var fullPath =
                Path.IsPathRooted(
                    normalized)
                    ? Path.GetFullPath(
                        normalized)
                    : Path.GetFullPath(
                        Path.Combine(
                            baseDirectory,
                            normalized));

            return File.Exists(
                       fullPath)
                ? fullPath
                : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return null;
        }
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
