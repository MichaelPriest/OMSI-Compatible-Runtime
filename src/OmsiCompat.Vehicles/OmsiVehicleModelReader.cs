using System.Globalization;
using OmsiCompat.Map;

namespace OmsiCompat.Vehicles;

public static class OmsiVehicleModelReader
{
    public static OmsiVehicleModel ReadFile(string path)
    {
        var document = OmsiSectionDocument.ParseFile(path);
        var meshes = new List<OmsiVehicleMeshReference>();
        var textTextures =
            new List<OmsiVehicleTextTexture>();

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
        var lightEffects =
            new List<OmsiVehicleLightEffect>();
        var overrides = new List<OmsiVehicleMaterialOverride>();
        var materialChangeGroupIndex = -1;
        MaterialBuilder? material = null;
        AnimationBuilder? animation = null;

        void CommitMaterial()
        {
            if (material is null)
            {
                return;
            }

            // OMSI stores matl_change as a change set. Only matl_item
            // entries are selectable variants; the declaration itself is
            // not an item.
            if (string.IsNullOrWhiteSpace(
                    material.MaterialChangeVariable) ||
                material.MaterialChangeItemIndex > 0)
            {
                overrides.Add(
                    material.Build());
            }

            material = null;
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
                    overrides.ToArray(),
                    lightEffects.ToArray()));
            }

            currentMeshPath = null;
            visibilityConditions =
                new List<OmsiVehicleVisibilityCondition>();
            animations =
                new List<OmsiVehicleAnimation>();
            lightEffects =
                new List<OmsiVehicleLightEffect>();
            overrides = new List<OmsiVehicleMaterialOverride>();
            materialChangeGroupIndex = -1;
        }

        foreach (var section in document.Sections)
        {
            if (section.Name.Equals(
                    "texttexture",
                    StringComparison.OrdinalIgnoreCase) ||
                section.Name.Equals(
                    "texttexture_enh",
                    StringComparison.OrdinalIgnoreCase))
            {
                var definition =
                    TryParseTextTexture(
                        section,
                        textTextures.Count,
                        section.Name.Equals(
                            "texttexture_enh",
                            StringComparison.OrdinalIgnoreCase));

                if (definition is not null)
                {
                    textTextures.Add(
                        definition);
                }

                continue;
            }

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

            if (section.Name.Equals(
                    "light_enh",
                    StringComparison.OrdinalIgnoreCase))
            {
                var light =
                    TryParseLightEffect(
                        Values(section).ToArray(),
                        enhanced: false);

                if (light is not null)
                {
                    lightEffects.Add(
                        light);
                }

                continue;
            }

            if (section.Name.Equals(
                    "light_enh_2",
                    StringComparison.OrdinalIgnoreCase))
            {
                var light =
                    TryParseLightEffect(
                        Values(section).ToArray(),
                        enhanced: true);

                if (light is not null)
                {
                    lightEffects.Add(
                        light);
                }

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
                    "matl_change",
                    StringComparison.OrdinalIgnoreCase))
            {
                CommitMaterial();
                var values =
                    Values(section).ToArray();

                if (values.Length >= 3 &&
                    int.TryParse(
                        values[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var materialIndex) &&
                    materialIndex >= 0)
                {
                    materialChangeGroupIndex++;

                    material =
                        new MaterialBuilder(
                            values[0]
                                .Trim()
                                .Trim('"'),
                            materialIndex)
                        {
                            MaterialChangeVariable =
                                values[2]
                                    .Trim()
                                    .Trim('"'),
                            MaterialChangeGroupIndex =
                                materialChangeGroupIndex,
                            MaterialChangeItemIndex =
                                0
                        };
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

            if (section.Name.Equals(
                    "matl_item",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(
                        material.MaterialChangeVariable))
                {
                    // Native OMSI clones the current material when a
                    // matl_item is encountered. The script variable then
                    // selects item 1, 2, ... while value 0 keeps the base
                    // material. Preserve that inheritance here.
                    if (material.MaterialChangeItemIndex > 0)
                    {
                        overrides.Add(
                            material.Build());
                    }

                    material =
                        material.CloneForNextMaterialChangeItem();
                }

                continue;
            }

            if (section.Name.Equals(
                    "matl_allcolor",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section).ToArray();

                if (values.Length >= 14)
                {
                    var parsed =
                        new double[14];

                    var valid =
                        true;

                    for (var index = 0;
                         index < parsed.Length;
                         index++)
                    {
                        if (!TrySingle(
                                values[index],
                                out parsed[index]))
                        {
                            valid = false;
                            break;
                        }
                    }

                    if (valid)
                    {
                        material.AllColor =
                            new OmsiVehicleMaterialColor(
                                parsed[0],
                                parsed[1],
                                parsed[2],
                                parsed[3],
                                parsed[4],
                                parsed[5],
                                parsed[6],
                                parsed[7],
                                parsed[8],
                                parsed[9],
                                parsed[10],
                                parsed[11],
                                parsed[12],
                                parsed[13]);
                    }
                }

                continue;
            }

            if (section.Name.Equals(
                    "matl_nightmap",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(
                        material.MaterialChangeVariable))
                {
                    material.MaterialChangeMapSource =
                        Values(section)
                            .FirstOrDefault()?
                            .Trim()
                            .Trim('"');
                }

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
                    "matl_freetex",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section).ToArray();

                if (values.Length >= 2)
                {
                    var source =
                        values[0]
                            .Trim()
                            .Trim('"');

                    var variable =
                        values[1]
                            .Trim()
                            .Trim('"');

                    if (source.Length > 0 &&
                        variable.Length > 0)
                    {
                        material.FreeTextures.Add(
                            new OmsiVehicleFreeTexture(
                                source,
                                variable));
                    }
                }

                continue;
            }

            if (section.Name.Equals(
                    "useTextTexture",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(
                        Values(section)
                            .FirstOrDefault(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var textTextureIndex) &&
                    textTextureIndex >= 0)
                {
                    material.TextTextureIndex =
                        textTextureIndex;
                }

                continue;
            }

            if (section.Name.Equals(
                    "matl_bumpmap",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section).ToArray();

                material.BumpMapSource =
                    values
                        .FirstOrDefault()?
                        .Trim()
                        .Trim('"');

                if (values.Length >= 2 &&
                    TrySingle(
                        values[1],
                        out var bumpStrength))
                {
                    material.BumpMapStrength =
                        bumpStrength;
                }

                continue;
            }

            if (section.Name.Equals(
                    "matl_envmap",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section).ToArray();

                material.EnvMapSource =
                    values
                        .FirstOrDefault()?
                        .Trim()
                        .Trim('"');

                if (values.Length >= 2 &&
                    TrySingle(
                        values[1],
                        out var envStrength))
                {
                    material.EnvMapStrength =
                        envStrength;
                }

                continue;
            }

            if (section.Name.Equals(
                    "matl_envmap_mask",
                    StringComparison.OrdinalIgnoreCase))
            {
                material.EnvMapMaskSource =
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

                // Native OMSI keeps lightmaps separate from nightmaps even
                // inside a matl_change/matl_item state. A lightmap remains
                // an indirect additive lighting layer, optionally controlled
                // by its own variable.
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

        return new OmsiVehicleModel(
            path,
            meshes.ToArray(),
            textTextures.ToArray());
    }

    private static OmsiVehicleLightEffect?
        TryParseLightEffect(
            IReadOnlyList<string> values,
            bool enhanced)
    {
        if (enhanced)
        {
            if (values.Count < 23 ||
                !TrySingle(values[0], out var positionX) ||
                !TrySingle(values[1], out var positionY) ||
                !TrySingle(values[2], out var positionZ) ||
                !TrySingle(values[3], out var directionX) ||
                !TrySingle(values[4], out var directionY) ||
                !TrySingle(values[5], out var directionZ) ||
                !TrySingle(values[6], out var upX) ||
                !TrySingle(values[7], out var upY) ||
                !TrySingle(values[8], out var upZ) ||
                !TryInteger(values[9], out var omni) ||
                !TryInteger(values[10], out var rotating) ||
                !TryColor(values[11], out var red) ||
                !TryColor(values[12], out var green) ||
                !TryColor(values[13], out var blue) ||
                !TrySingle(values[14], out var size) ||
                !TrySingle(values[15], out var innerCone) ||
                !TrySingle(values[16], out var outerCone) ||
                !TrySingle(values[18], out var factor) ||
                !TrySingle(values[19], out var cameraOffset) ||
                !TryInteger(values[20], out var parameters) ||
                !TryInteger(values[21], out var cone) ||
                !TrySingle(values[22], out var timeConstant))
            {
                return null;
            }

            var variable =
                values[17]
                    .Trim()
                    .Trim('"');

            if (variable.Length == 0)
            {
                variable = "1";
            }

            var bitmap =
                values.Count >= 24
                    ? NormalizeOptionalValue(
                        values[23])
                    : null;

            return new OmsiVehicleLightEffect(
                positionX,
                positionY,
                positionZ,
                directionX,
                directionY,
                directionZ,
                upX,
                upY,
                upZ,
                Math.Clamp(
                    omni,
                    0,
                    1),
                Math.Clamp(
                    rotating,
                    0,
                    2),
                red,
                green,
                blue,
                Math.Max(
                    size,
                    0.0),
                innerCone,
                outerCone,
                variable,
                factor,
                cameraOffset,
                parameters,
                cone != 0,
                Math.Max(
                    timeConstant,
                    0.0),
                bitmap,
                true);
        }

        // The classic MAN SD200/SD202 light_enh form uses 12 values:
        // position xyz, RGB, size, brightness variable, multiplier,
        // camera offset, effect flags and time constant. Some add-ons append
        // an optional bitmap as a 13th value.
        if (values.Count < 12 ||
            !TrySingle(values[0], out var oldPositionX) ||
            !TrySingle(values[1], out var oldPositionY) ||
            !TrySingle(values[2], out var oldPositionZ) ||
            !TryColor(values[3], out var oldRed) ||
            !TryColor(values[4], out var oldGreen) ||
            !TryColor(values[5], out var oldBlue) ||
            !TrySingle(values[6], out var oldSize) ||
            !TrySingle(values[8], out var oldFactor) ||
            !TrySingle(values[9], out var oldCameraOffset) ||
            !TryInteger(values[10], out var oldParameters) ||
            !TrySingle(values[11], out var oldTimeConstant))
        {
            return null;
        }

        var oldVariable =
            values[7]
                .Trim()
                .Trim('"');

        if (oldVariable.Length == 0)
        {
            oldVariable = "1";
        }

        var oldBitmap =
            values.Count >= 13
                ? NormalizeOptionalValue(
                    values[12])
                : null;

        return new OmsiVehicleLightEffect(
            oldPositionX,
            oldPositionY,
            oldPositionZ,
            0.0,
            0.0,
            1.0,
            0.0,
            0.0,
            1.0,
            1,
            2,
            oldRed,
            oldGreen,
            oldBlue,
            Math.Max(
                oldSize,
                0.0),
            0.0,
            180.0,
            oldVariable,
            oldFactor,
            oldCameraOffset,
            oldParameters,
            false,
            Math.Max(
                oldTimeConstant,
                0.0),
            oldBitmap,
            false);
    }

    private static bool TryInteger(
        string? value,
        out int result)
    {
        result = 0;

        return !string.IsNullOrWhiteSpace(
                   value) &&
               int.TryParse(
                   value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out result);
    }

    private static bool TryColor(
        string? value,
        out byte result)
    {
        result = 0;

        return byte.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static string? NormalizeOptionalValue(
        string? value)
    {
        var normalized =
            value?
                .Trim()
                .Trim('"');

        return string.IsNullOrWhiteSpace(
                normalized)
            ? null
            : normalized;
    }

    private static OmsiVehicleTextTexture?
        TryParseTextTexture(
            OmsiSection section,
            int index,
            bool enhanced)
    {
        var values =
            Values(section).ToArray();

        if (values.Length < 8 ||
            !int.TryParse(
                values[2],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var width) ||
            !int.TryParse(
                values[3],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var height) ||
            width <= 0 ||
            height <= 0 ||
            !int.TryParse(
                values[4],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var fullColorValue) ||
            !byte.TryParse(
                values[5],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var red) ||
            !byte.TryParse(
                values[6],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var green) ||
            !byte.TryParse(
                values[7],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var blue))
        {
            return null;
        }

        int? alignment = null;
        bool? gridAligned = null;

        if (enhanced)
        {
            if (values.Length >= 9 &&
                int.TryParse(
                    values[8],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedAlignment))
            {
                alignment =
                    parsedAlignment;
            }

            if (values.Length >= 10 &&
                int.TryParse(
                    values[9],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedGrid))
            {
                gridAligned =
                    parsedGrid != 0;
            }
        }

        return new OmsiVehicleTextTexture(
            index,
            values[0]
                .Trim()
                .Trim('"'),
            values[1]
                .Trim()
                .Trim('"'),
            width,
            height,
            fullColorValue != 0,
            red,
            green,
            blue,
            alignment,
            gridAligned);
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
        public string? MaterialChangeVariable { get; set; }
        public string? MaterialChangeMapSource { get; set; }
        public OmsiVehicleMaterialColor? AllColor { get; set; }
        public string? EnvMapSource { get; set; }
        public double EnvMapStrength { get; set; }
        public string? EnvMapMaskSource { get; set; }
        public string? BumpMapSource { get; set; }
        public double BumpMapStrength { get; set; }
        public List<OmsiVehicleFreeTexture> FreeTextures { get; } = [];
        public int? TextTextureIndex { get; set; }
        public string? TextureCoordinateXVariable { get; set; }
        public string? TextureCoordinateYVariable { get; set; }
        public int MaterialChangeGroupIndex { get; set; } = -1;
        public int MaterialChangeItemIndex { get; set; }

        public MaterialBuilder CloneForNextMaterialChangeItem()
        {
            var clone =
                new MaterialBuilder(
                    TextureName,
                    MaterialIndex)
                {
                    AlphaMode = AlphaMode,
                    TransMapSource = TransMapSource,
                    HasTransMapDirective = HasTransMapDirective,
                    NoZWrite = NoZWrite,
                    NoZCheck = NoZCheck,
                    AlphaScaleVariable = AlphaScaleVariable,
                    LightMapSource = LightMapSource,
                    LightMapVariable = LightMapVariable,
                    MaterialChangeVariable = MaterialChangeVariable,
                    MaterialChangeMapSource = MaterialChangeMapSource,
                    AllColor = AllColor,
                    EnvMapSource = EnvMapSource,
                    EnvMapStrength = EnvMapStrength,
                    EnvMapMaskSource = EnvMapMaskSource,
                    BumpMapSource = BumpMapSource,
                    BumpMapStrength = BumpMapStrength,
                    TextTextureIndex = TextTextureIndex,
                    TextureCoordinateXVariable = TextureCoordinateXVariable,
                    TextureCoordinateYVariable = TextureCoordinateYVariable,
                    MaterialChangeGroupIndex = MaterialChangeGroupIndex,
                    MaterialChangeItemIndex =
                        MaterialChangeItemIndex + 1
                };

            clone.FreeTextures.AddRange(
                FreeTextures);

            return clone;
        }

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
                LightMapVariable,
                MaterialChangeVariable,
                MaterialChangeMapSource,
                AllColor,
                EnvMapSource,
                EnvMapStrength,
                EnvMapMaskSource,
                BumpMapSource,
                BumpMapStrength,
                FreeTextures.ToArray(),
                TextTextureIndex,
                TextureCoordinateXVariable,
                TextureCoordinateYVariable,
                MaterialChangeGroupIndex,
                MaterialChangeItemIndex);
    }
}
