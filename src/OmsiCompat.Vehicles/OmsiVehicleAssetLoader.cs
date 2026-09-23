using System.Numerics;
using OmsiCompat.Core;
using OmsiCompat.Models;

namespace OmsiCompat.Vehicles;

public static class OmsiVehicleAssetLoader
{
    public static OmsiVehicleAsset Load(
        OmsiContentRoot contentRoot,
        OmsiBusInfo bus,
        IProgress<OmsiVehicleLoadProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentNullException.ThrowIfNull(bus);

        progress?.Report(
            new OmsiVehicleLoadProgress(
                2,
                $"Preparando definição do veículo · {bus.ScriptManifest.RegisteredFileCount:N0} arquivo(s) de script registrados · {bus.ScriptManifest.MissingFileCount:N0} ausente(s)..."));

        if (string.IsNullOrWhiteSpace(bus.ModelConfigPath) ||
            !File.Exists(bus.ModelConfigPath))
        {
            progress?.Report(
                new OmsiVehicleLoadProgress(
                    100,
                    "Veículo sem model.cfg renderizável."));

            return new OmsiVehicleAsset(
                bus,
                Array.Empty<OmsiVehicleMeshAsset>(),
                OmsiDriverPositionReader.ReadFile(bus.PassengerCabinPath),
                Array.Empty<OmsiVehicleTextTexture>());
        }

        var model = OmsiVehicleModelReader.ReadFile(bus.ModelConfigPath);
        var meshes = new List<OmsiVehicleMeshAsset>();

        progress?.Report(
            new OmsiVehicleLoadProgress(
                8,
                $"Modelo encontrado · {model.Meshes.Count:N0} mesh(es)."));

        for (var meshIndex = 0;
             meshIndex < model.Meshes.Count;
             meshIndex++)
        {
            var mesh =
                model.Meshes[meshIndex];

            var meshPercent =
                10 +
                (int)Math.Round(
                    84.0 *
                    meshIndex /
                    Math.Max(
                        model.Meshes.Count,
                        1));

            progress?.Report(
                new OmsiVehicleLoadProgress(
                    meshPercent,
                    $"Mesh {meshIndex + 1:N0}/{model.Meshes.Count:N0} · {Path.GetFileName(mesh.DeclaredPath)}"));
            var meshPath = ResolveMeshPath(contentRoot.RootPath, bus, model.SourcePath, mesh.DeclaredPath);

            if (meshPath is null)
            {
                meshes.Add(new OmsiVehicleMeshAsset(
                    mesh.DeclaredPath,
                    null,
                    "missingMesh",
                    mesh.Transform,
                    mesh.ViewpointFlag,
                    mesh.LodThreshold,
                    mesh.VisibilityConditions,
                    mesh.Animations,
                    Matrix4x4.Identity,
                    [],
                    [],
                    [],
                    [],
                    [],
                    Array.Empty<OmsiVehicleMaterial>()));
                continue;
            }

            var geometry = string.Equals(
                    Path.GetExtension(meshPath),
                    ".x",
                    StringComparison.OrdinalIgnoreCase)
                ? new OmsiDirectXTextGeometryReader().Read(meshPath)
                : OmsiO3dGeometryReader.ReadFile(meshPath);

            var materials = geometry.Materials
                .Select((material, materialIndex) =>
                {
                    var textureOccurrence =
                        GetTextureOccurrenceIndex(
                            geometry.Materials,
                            materialIndex);

                    var matchingOverrides =
                        mesh.MaterialOverrides
                            .Where(
                                item =>
                                    item.MaterialIndex ==
                                        textureOccurrence &&
                                    MaterialTextureMatches(
                                        item.TextureName,
                                        material.TextureName))
                            .ToArray();

                    if (matchingOverrides.Length == 0)
                    {
                        // Compatibility fallback for unusual add-ons that
                        // appear to use the absolute O3D material slot.
                        matchingOverrides =
                            mesh.MaterialOverrides
                                .Where(
                                    item =>
                                        item.MaterialIndex ==
                                            materialIndex &&
                                        MaterialTextureMatches(
                                            item.TextureName,
                                            material.TextureName))
                                .ToArray();
                    }

                    var materialOverride =
                        matchingOverrides
                            .FirstOrDefault(
                                static item =>
                                    string.IsNullOrWhiteSpace(
                                        item.MaterialChangeVariable)) ??
                        matchingOverrides
                            .FirstOrDefault();

                    var materialChangeOverride =
                        matchingOverrides
                            .FirstOrDefault(
                                static item =>
                                    !string.IsNullOrWhiteSpace(
                                        item.MaterialChangeVariable) &&
                                    (!string.IsNullOrWhiteSpace(
                                         item.MaterialChangeMapSource) ||
                                     item.AllColor is not null));

                    string? texturePath = null;
                    if (!string.IsNullOrWhiteSpace(material.TextureName))
                    {
                        var reflectionCamera =
                            bus.ReflectionCameras.FirstOrDefault(
                                camera =>
                                    string.Equals(
                                        camera.RuntimeTextureName,
                                        Path.GetFileName(
                                            material.TextureName),
                                        StringComparison.OrdinalIgnoreCase));

                        if (reflectionCamera is not null)
                        {
                            texturePath =
                                reflectionCamera.RuntimeTextureKey;
                        }
                        else
                        {
                            OmsiTextureAssetPathResolver.TryResolveVehicleTexture(
                                contentRoot.RootPath,
                                bus.DirectoryPath,
                                model.SourcePath,
                                meshPath,
                                material.TextureName,
                                out texturePath);
                        }
                    }

                    string? transMapPath = null;
                    var transMapSource =
                        materialOverride?.TransMapSource;

                    if (!string.IsNullOrWhiteSpace(
                            transMapSource) &&
                        !transMapSource.StartsWith(
                            "\\",
                            StringComparison.Ordinal))
                    {
                        OmsiTextureAssetPathResolver.TryResolveVehicleTexture(
                            contentRoot.RootPath,
                            bus.DirectoryPath,
                            model.SourcePath,
                            meshPath,
                            transMapSource,
                            out transMapPath);
                    }

                    string? lightMapPath = null;
                    var lightMapSource =
                        materialOverride?.LightMapSource;

                    if (!string.IsNullOrWhiteSpace(
                            lightMapSource))
                    {
                        OmsiTextureAssetPathResolver.TryResolveVehicleTexture(
                            contentRoot.RootPath,
                            bus.DirectoryPath,
                            model.SourcePath,
                            meshPath,
                            lightMapSource,
                            out lightMapPath);
                    }

                    string? materialChangePath = null;
                    var materialChangeSource =
                        materialChangeOverride?
                            .MaterialChangeMapSource;

                    if (!string.IsNullOrWhiteSpace(
                            materialChangeSource))
                    {
                        OmsiTextureAssetPathResolver.TryResolveVehicleTexture(
                            contentRoot.RootPath,
                            bus.DirectoryPath,
                            model.SourcePath,
                            meshPath,
                            materialChangeSource,
                            out materialChangePath);
                    }

                    string? envMapPath = null;
                    var envMapSource =
                        materialOverride?
                            .EnvMapSource;

                    if (!string.IsNullOrWhiteSpace(
                            envMapSource))
                    {
                        OmsiTextureAssetPathResolver.TryResolveVehicleTexture(
                            contentRoot.RootPath,
                            bus.DirectoryPath,
                            model.SourcePath,
                            meshPath,
                            envMapSource,
                            out envMapPath);
                    }

                    string? envMapMaskPath = null;
                    var envMapMaskSource =
                        materialOverride?
                            .EnvMapMaskSource;

                    if (!string.IsNullOrWhiteSpace(
                            envMapMaskSource))
                    {
                        OmsiTextureAssetPathResolver.TryResolveVehicleTexture(
                            contentRoot.RootPath,
                            bus.DirectoryPath,
                            model.SourcePath,
                            meshPath,
                            envMapMaskSource,
                            out envMapMaskPath);
                    }

                    string? bumpMapPath = null;
                    var bumpMapSource =
                        materialOverride?
                            .BumpMapSource;

                    if (!string.IsNullOrWhiteSpace(
                            bumpMapSource))
                    {
                        OmsiTextureAssetPathResolver.TryResolveVehicleTexture(
                            contentRoot.RootPath,
                            bus.DirectoryPath,
                            model.SourcePath,
                            meshPath,
                            bumpMapSource,
                            out bumpMapPath);
                    }

                    var alphaMode =
                        materialOverride?.AlphaMode ??
                        (material.DiffuseA < 0.999f
                            ? 2
                            : 0);

                    if (materialOverride?.HasTransMapDirective == true &&
                        string.IsNullOrWhiteSpace(
                            transMapSource))
                    {
                        alphaMode = 0;
                    }

                    return new OmsiVehicleMaterial(
                        material.DiffuseR,
                        material.DiffuseG,
                        material.DiffuseB,
                        material.DiffuseA,
                        texturePath,
                        alphaMode,
                        transMapPath,
                        materialOverride?.NoZWrite ?? false,
                        materialOverride?.NoZCheck ?? false,
                        materialOverride?.AlphaScaleVariable,
                        lightMapPath,
                        materialOverride?.LightMapVariable,
                        materialChangePath,
                        materialChangeOverride?
                            .MaterialChangeVariable,
                        materialOverride?
                            .AllColor,
                        materialChangeOverride?
                            .AllColor,
                        envMapPath,
                        materialOverride?.EnvMapStrength ?? 0.0,
                        envMapMaskPath,
                        bumpMapPath,
                        materialOverride?.BumpMapStrength ?? 0.0,
                        materialOverride?.FreeTextures ??
                            Array.Empty<OmsiVehicleFreeTexture>(),
                        materialOverride?.TextTextureIndex);
                })
                .ToArray();

            meshes.Add(new OmsiVehicleMeshAsset(
                mesh.DeclaredPath,
                meshPath,
                geometry.ErrorCode,
                mesh.Transform,
                mesh.ViewpointFlag,
                mesh.LodThreshold,
                mesh.VisibilityConditions,
                mesh.Animations,
                geometry.SourceTransform,
                geometry.Positions,
                geometry.Normals,
                geometry.Uvs,
                geometry.Indices,
                geometry.TriangleMaterialIndices,
                materials));
        }

        progress?.Report(
            new OmsiVehicleLoadProgress(
                100,
                $"{meshes.Count:N0} mesh(es) processadas · finalizando veículo."));

        return new OmsiVehicleAsset(
            bus,
            meshes.ToArray(),
            OmsiDriverPositionReader.ReadFile(bus.PassengerCabinPath),
            model.TextTextures);
    }

    private static int GetTextureOccurrenceIndex(
        IReadOnlyList<OmsiO3dMaterial> materials,
        int materialIndex)
    {
        if (materialIndex <= 0 ||
            materialIndex >= materials.Count)
        {
            return 0;
        }

        var textureName =
            Path.GetFileName(
                materials[materialIndex]
                    .TextureName);

        var occurrence =
            0;

        for (var index = 0;
             index < materialIndex;
             index++)
        {
            if (string.Equals(
                    Path.GetFileName(
                        materials[index]
                            .TextureName),
                    textureName,
                    StringComparison.OrdinalIgnoreCase))
            {
                occurrence++;
            }
        }

        return occurrence;
    }

    private static bool MaterialTextureMatches(
        string? declaredTexture,
        string? o3dTexture) =>
        string.Equals(
            Path.GetFileName(
                declaredTexture),
            Path.GetFileName(
                o3dTexture),
            StringComparison.OrdinalIgnoreCase);

    private static string? ResolveMeshPath(
        string omsiRoot,
        OmsiBusInfo bus,
        string modelConfigPath,
        string declaredPath)
    {
        if (string.IsNullOrWhiteSpace(declaredPath))
        {
            return null;
        }

        var normalized = declaredPath
            .Trim()
            .Trim('"')
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalized))
        {
            return null;
        }

        var modelDirectory = Path.GetDirectoryName(modelConfigPath) ?? bus.DirectoryPath;
        var vehiclesRoot = EnsureTrailingSeparator(
            Path.GetFullPath(Path.Combine(omsiRoot, "Vehicles")));

        foreach (var candidate in new[]
        {
            Path.Combine(modelDirectory, normalized),
            Path.Combine(bus.DirectoryPath, normalized),
            Path.Combine(bus.DirectoryPath, "model", normalized)
        })
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (full.StartsWith(vehiclesRoot, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(full))
                {
                    return full;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ||
        path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
