using System.Globalization;
using OmsiCompat.Map;

namespace OmsiCompat.Vehicles;

public static class OmsiVehicleModelReader
{
    public static OmsiVehicleModel ReadFile(string path)
    {
        var document = OmsiSectionDocument.ParseFile(path);
        var meshes = new List<OmsiVehicleMeshReference>();

        string? currentMeshPath = null;
        var currentOrdinal = -1;
        var currentTransform =
            OmsiVehicleMeshTransform.Identity;
        var currentViewpointFlag = 0;
        double? currentLodThreshold = null;
        var visibilityConditions =
            new List<OmsiVehicleVisibilityCondition>();
        var animations =
            new List<OmsiVehicleAnimation>();
        var overrides = new List<OmsiVehicleMaterialOverride>();
        MaterialBuilder? material = null;
        AnimationBuilder? animation = null;

        void CommitMaterial()
        {
            if (material is not null)
            {
                overrides.Add(material.Build());
                material = null;
            }
        }

        void CommitAnimation()
        {
            if (animation is null)
            {
                return;
            }

            var built =
                animation.Build();

            if (built is not null)
            {
                animations.Add(
                    built);
            }

            animation = null;
        }

        void CommitMesh()
        {
            CommitMaterial();
            CommitAnimation();

            if (!string.IsNullOrWhiteSpace(currentMeshPath))
            {
                meshes.Add(new OmsiVehicleMeshReference(
                    currentOrdinal,
                    currentMeshPath,
                    currentTransform,
                    currentViewpointFlag,
                    currentLodThreshold,
                    visibilityConditions.ToArray(),
                    animations.ToArray(),
                    overrides.ToArray()));
            }

            currentMeshPath = null;
            visibilityConditions =
                new List<OmsiVehicleVisibilityCondition>();
            animations =
                new List<OmsiVehicleAnimation>();
            overrides = new List<OmsiVehicleMaterialOverride>();
        }

        foreach (var section in document.Sections)
        {
            if (section.Name.Equals(
                    "LOD",
                    StringComparison.OrdinalIgnoreCase))
            {
                CommitMesh();

                if (TrySingle(
                        Values(section).FirstOrDefault(),
                        out var lodThreshold))
                {
                    currentLodThreshold =
                        Math.Max(
                            lodThreshold,
                            0.0);
                }

                continue;
            }

            if (section.Name.Equals("mesh", StringComparison.OrdinalIgnoreCase))
            {
                CommitMesh();
                currentOrdinal++;
                currentTransform =
                    OmsiVehicleMeshTransform.Identity;
                currentViewpointFlag = 0;

                currentMeshPath = Values(section).FirstOrDefault();
                if (currentMeshPath is not null)
                {
                    currentMeshPath = currentMeshPath.Trim().Trim('"');
                }

                continue;
            }

            if (currentMeshPath is null)
            {
                continue;
            }

            if (section.Name.Equals("matl", StringComparison.OrdinalIgnoreCase))
            {
                CommitMaterial();
                var values = Values(section).ToArray();

                if (values.Length >= 2 &&
                    int.TryParse(
                        values[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var materialIndex) &&
                    materialIndex >= 0)
                {
                    material = new MaterialBuilder(
                        values[0].Trim().Trim('"'),
                        materialIndex);
                }

                continue;
            }

            if (section.Name.Equals(
                    "viewpoint",
                    StringComparison.OrdinalIgnoreCase))
            {
                var value =
                    Values(section)
                        .FirstOrDefault();

                if (int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var viewpoint) &&
                    viewpoint is >= 0 and <= 7)
                {
                    currentViewpointFlag =
                        viewpoint;
                }

                continue;
            }

            if (section.Name.Equals(
                    "visible",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section).ToArray();

                if (values.Length >= 2 &&
                    TrySingle(
                        values[1],
                        out var visibleValue))
                {
                    var variableName =
                        values[0]
                            .Trim()
                            .Trim('"');

                    if (variableName.Length > 0)
                    {
                        visibilityConditions.Add(
                            new OmsiVehicleVisibilityCondition(
                                variableName,
                                visibleValue));
                    }
                }

                continue;
            }

            if (section.Name.Equals(
                    "newanim",
                    StringComparison.OrdinalIgnoreCase))
            {
                CommitAnimation();

                animation =
                    new AnimationBuilder();

                ParseAnimationValues(
                    animation,
                    Values(section).ToArray());

                CommitAnimation();
                continue;
            }

            if (animation is not null &&
                section.Name.Equals(
                    "origin_from_mesh",
                    StringComparison.OrdinalIgnoreCase))
            {
                animation.OriginFromMesh = true;
                continue;
            }

            if (animation is not null &&
                section.Name.Equals(
                    "origin_trans",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section).ToArray();

                if (TryVector3(
                        values,
                        out var x,
                        out var y,
                        out var z))
                {
                    animation.OriginFromMesh = false;
                    animation.OriginX = x;
                    animation.OriginY = y;
                    animation.OriginZ = z;
                }

                continue;
            }

            if (animation is not null &&
                section.Name.Equals(
                    "origin_rot_x",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TrySingle(
                        Values(section).FirstOrDefault(),
                        out var value))
                {
                    animation.OriginRotationX =
                        value;
                }

                continue;
            }

            if (animation is not null &&
                section.Name.Equals(
                    "origin_rot_y",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TrySingle(
                        Values(section).FirstOrDefault(),
                        out var value))
                {
                    animation.OriginRotationY =
                        value;
                }

                continue;
            }

            if (animation is not null &&
                section.Name.Equals(
                    "origin_rot_z",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TrySingle(
                        Values(section).FirstOrDefault(),
                        out var value))
                {
                    animation.OriginRotationZ =
                        value;
                }

                continue;
            }

            if (animation is not null &&
                section.Name.Equals(
                    "anim_trans",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section).ToArray();

                if (values.Length >= 2 &&
                    TrySingle(
                        values[1],
                        out var delta))
                {
                    animation.Kind =
                        OmsiVehicleAnimationKind.Translation;
                    animation.VariableName =
                        values[0]
                            .Trim()
                            .Trim('"');
                    animation.Delta =
                        delta;
                }

                continue;
            }

            if (animation is not null &&
                section.Name.Equals(
                    "anim_rot",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section).ToArray();

                if (values.Length >= 2 &&
                    TrySingle(
                        values[1],
                        out var delta))
                {
                    animation.Kind =
                        OmsiVehicleAnimationKind.Rotation;
                    animation.VariableName =
                        values[0]
                            .Trim()
                            .Trim('"');
                    animation.Delta =
                        delta;
                }

                continue;
            }

            if (animation is not null &&
                section.Name.Equals(
                    "offset",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TrySingle(
                        Values(section).FirstOrDefault(),
                        out var value))
                {
                    animation.Offset =
                        value;
                }

                continue;
            }

            if (animation is not null &&
                section.Name.Equals(
                    "maxspeed",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TrySingle(
                        Values(section).FirstOrDefault(),
                        out var value))
                {
                    animation.MaxSpeed =
                        value;
                }

                continue;
            }

            if (animation is not null &&
                section.Name.Equals(
                    "delay",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TrySingle(
                        Values(section).FirstOrDefault(),
                        out var value))
                {
                    animation.Delay =
                        value;
                }

                continue;
            }

            if (section.Name.Equals(
                    "newpos",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section).ToArray();

                if (TryVector3(
                        values,
                        out var x,
                        out var y,
                        out var z))
                {
                    currentTransform =
                        currentTransform with
                        {
                            PositionX = x,
                            PositionY = y,
                            PositionZ = z
                        };
                }

                continue;
            }

            if (section.Name.Equals(
                    "newrot_x",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TrySingle(
                        Values(section).FirstOrDefault(),
                        out var value))
                {
                    currentTransform =
                        currentTransform with
                        {
                            RotationX = value
                        };
                }

                continue;
            }

            if (section.Name.Equals(
                    "newrot_y",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TrySingle(
                        Values(section).FirstOrDefault(),
                        out var value))
                {
                    currentTransform =
                        currentTransform with
                        {
                            RotationY = value
                        };
                }

                continue;
            }

            if (section.Name.Equals(
                    "newrot_z",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TrySingle(
                        Values(section).FirstOrDefault(),
                        out var value))
                {
                    currentTransform =
                        currentTransform with
                        {
                            RotationZ = value
                        };
                }

                continue;
            }

            if (section.Name.Equals(
                    "newscale",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section).ToArray();

                if (values.Length == 1 &&
                    TrySingle(
                        values[0],
                        out var uniform))
                {
                    currentTransform =
                        currentTransform with
                        {
                            ScaleX = uniform,
                            ScaleY = uniform,
                            ScaleZ = uniform
                        };
                }
                else if (TryVector3(
                             values,
                             out var sx,
                             out var sy,
                             out var sz))
                {
                    currentTransform =
                        currentTransform with
                        {
                            ScaleX = sx,
                            ScaleY = sy,
                            ScaleZ = sz
                        };
                }

                continue;
            }

            if (material is null)
            {
                continue;
            }

            if (section.Name.Equals("matl_alpha", StringComparison.OrdinalIgnoreCase))
            {
                var value = Values(section).FirstOrDefault();
                if (int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var alphaMode) &&
                    alphaMode is >= 0 and <= 2)
                {
                    material.AlphaMode = alphaMode;
                }

                continue;
            }

            if (section.Name.Equals("matl_transmap", StringComparison.OrdinalIgnoreCase))
            {
                material.HasTransMapDirective = true;
                material.TransMapSource =
                    Values(section)
                        .FirstOrDefault()?
                        .Trim()
                        .Trim('"');
                continue;
            }

            if (section.Name.Equals(
                    "alphascale",
                    StringComparison.OrdinalIgnoreCase))
            {
                material.AlphaScaleVariable =
                    Values(section)
                        .FirstOrDefault()?
                        .Trim()
                        .Trim('"');
                continue;
            }

            if (section.Name.Equals(
                    "matl_lightmap",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section).ToArray();

                material.LightMapSource =
                    values.FirstOrDefault()?
                        .Trim()
                        .Trim('"');

                material.LightMapVariable =
                    values.Length >= 2
                        ? values[1]
                            .Trim()
                            .Trim('"')
                        : null;

                continue;
            }

            if (section.Name.Equals("matl_noZwrite", StringComparison.OrdinalIgnoreCase))
            {
                material.NoZWrite = true;
                continue;
            }

            if (section.Name.Equals("matl_noZcheck", StringComparison.OrdinalIgnoreCase))
            {
                material.NoZCheck = true;
                continue;
            }
        }

        CommitMesh();

        return new OmsiVehicleModel(path, meshes.ToArray());
    }

    private static IEnumerable<string> Values(OmsiSection section) =>
        section.Lines
            .Select(static line => line.Value.Trim())
            .Where(static value =>
                value.Length > 0 &&
                !value.StartsWith('#') &&
                !value.StartsWith("//", StringComparison.Ordinal));

    private static void ParseAnimationValues(
        AnimationBuilder animation,
        IReadOnlyList<string> values)
    {
        for (var index = 0;
             index < values.Count;
             index++)
        {
            var command =
                values[index]
                    .Trim();

            if (command.Equals(
                    "origin_from_mesh",
                    StringComparison.OrdinalIgnoreCase))
            {
                animation.OriginFromMesh = true;
                continue;
            }

            if (command.Equals(
                    "origin_trans",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 3 < values.Count &&
                    TrySingle(
                        values[index + 1],
                        out var x) &&
                    TrySingle(
                        values[index + 2],
                        out var y) &&
                    TrySingle(
                        values[index + 3],
                        out var z))
                {
                    animation.OriginFromMesh = false;
                    animation.OriginX = x;
                    animation.OriginY = y;
                    animation.OriginZ = z;
                    index += 3;
                }

                continue;
            }

            if (command.Equals(
                    "origin_rot_x",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < values.Count &&
                    TrySingle(
                        values[index + 1],
                        out var value))
                {
                    animation.OriginRotationX =
                        value;
                    index++;
                }

                continue;
            }

            if (command.Equals(
                    "origin_rot_y",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < values.Count &&
                    TrySingle(
                        values[index + 1],
                        out var value))
                {
                    animation.OriginRotationY =
                        value;
                    index++;
                }

                continue;
            }

            if (command.Equals(
                    "origin_rot_z",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < values.Count &&
                    TrySingle(
                        values[index + 1],
                        out var value))
                {
                    animation.OriginRotationZ =
                        value;
                    index++;
                }

                continue;
            }

            if (command.Equals(
                    "anim_trans",
                    StringComparison.OrdinalIgnoreCase) ||
                command.Equals(
                    "anim_rot",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 2 < values.Count &&
                    TrySingle(
                        values[index + 2],
                        out var delta))
                {
                    var variableName =
                        values[index + 1]
                            .Trim()
                            .Trim('"');

                    if (variableName.Length > 0)
                    {
                        animation.Kind =
                            command.Equals(
                                "anim_trans",
                                StringComparison.OrdinalIgnoreCase)
                                ? OmsiVehicleAnimationKind.Translation
                                : OmsiVehicleAnimationKind.Rotation;

                        animation.VariableName =
                            variableName;

                        animation.Delta =
                            delta;
                    }

                    index += 2;
                }

                continue;
            }

            if (command.Equals(
                    "offset",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < values.Count &&
                    TrySingle(
                        values[index + 1],
                        out var value))
                {
                    animation.Offset =
                        value;
                    index++;
                }

                continue;
            }

            if (command.Equals(
                    "delay",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < values.Count &&
                    TrySingle(
                        values[index + 1],
                        out var value))
                {
                    animation.Delay =
                        value;
                    index++;
                }

                continue;
            }

            if (command.Equals(
                    "maxspeed",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < values.Count &&
                    TrySingle(
                        values[index + 1],
                        out var value))
                {
                    animation.MaxSpeed =
                        value;
                    index++;
                }
            }
        }
    }

    private static bool TryVector3(
        IReadOnlyList<string> values,
        out double x,
        out double y,
        out double z)
    {
        x = 0;
        y = 0;
        z = 0;

        return values.Count >= 3 &&
               TrySingle(values[0], out x) &&
               TrySingle(values[1], out y) &&
               TrySingle(values[2], out z);
    }

    private static bool TrySingle(
        string? value,
        out double result)
    {
        result = 0;

        return !string.IsNullOrWhiteSpace(value) &&
               double.TryParse(
                   value,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out result) &&
               double.IsFinite(result);
    }

    private sealed class AnimationBuilder
    {
        public OmsiVehicleAnimationKind?
            Kind
        {
            get;
            set;
        }

        public string VariableName
        {
            get;
            set;
        } = string.Empty;

        public double Delta
        {
            get;
            set;
        }

        public bool OriginFromMesh
        {
            get;
            set;
        } = true;

        public double OriginX
        {
            get;
            set;
        }

        public double OriginY
        {
            get;
            set;
        }

        public double OriginZ
        {
            get;
            set;
        }

        public double OriginRotationX
        {
            get;
            set;
        }

        public double OriginRotationY
        {
            get;
            set;
        }

        public double OriginRotationZ
        {
            get;
            set;
        }

        public double Offset
        {
            get;
            set;
        }

        public double? MaxSpeed
        {
            get;
            set;
        }

        public double? Delay
        {
            get;
            set;
        }

        public OmsiVehicleAnimation?
            Build()
        {
            if (!Kind.HasValue ||
                string.IsNullOrWhiteSpace(
                    VariableName))
            {
                return null;
            }

            return new OmsiVehicleAnimation(
                Kind.Value,
                VariableName,
                Delta,
                OriginFromMesh,
                OriginX,
                OriginY,
                OriginZ,
                OriginRotationX,
                OriginRotationY,
                OriginRotationZ,
                Offset,
                MaxSpeed,
                Delay);
        }
    }

    private sealed class MaterialBuilder(
        string textureName,
        int materialIndex)
    {
        public string TextureName { get; } = textureName;
        public int MaterialIndex { get; } = materialIndex;
        public int? AlphaMode { get; set; }
        public string? TransMapSource { get; set; }
        public bool HasTransMapDirective { get; set; }
        public bool NoZWrite { get; set; }
        public bool NoZCheck { get; set; }
        public string? AlphaScaleVariable { get; set; }
        public string? LightMapSource { get; set; }
        public string? LightMapVariable { get; set; }

        public OmsiVehicleMaterialOverride Build() =>
            new(
                TextureName,
                MaterialIndex,
                AlphaMode,
                TransMapSource,
                HasTransMapDirective,
                NoZWrite,
                NoZCheck,
                AlphaScaleVariable,
                LightMapSource,
                LightMapVariable);
    }
}
