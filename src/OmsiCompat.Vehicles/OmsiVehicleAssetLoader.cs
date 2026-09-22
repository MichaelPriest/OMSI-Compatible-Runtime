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
                "Preparando definição do veículo..."));

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
                OmsiDriverPositionReader.ReadFile(bus.PassengerCabinPath));
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
                    var materialOverride = mesh.MaterialOverrides
                        .Where(item => item.MaterialIndex == materialIndex)
                        .OrderByDescending(item =>
                            string.Equals(
                                Path.GetFileName(item.TextureName),
                                Path.GetFileName(material.TextureName),
                                StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault();

                    string? texturePath = null;
                    if (!string.IsNullOrWhiteSpace(material.TextureName))
                    {
                        OmsiTextureAssetPathResolver.TryResolveVehicleTexture(
                            contentRoot.RootPath,
                            bus.DirectoryPath,
                            model.SourcePath,
                            meshPath,
                            material.TextureName,
                            out texturePath);
                    }

                    string? transMapPath = null;
                    if (materialOverride?.TransMapSource is { Length: > 0 } transMap &&
                        !transMap.StartsWith("\\", StringComparison.Ordinal))
                    {
                        OmsiTextureAssetPathResolver.TryResolveVehicleTexture(
                            contentRoot.RootPath,
                            bus.DirectoryPath,
                            model.SourcePath,
                            meshPath,
                            transMap,
                            out transMapPath);
                    }

                    return new OmsiVehicleMaterial(
                        material.DiffuseR,
                        material.DiffuseG,
                        material.DiffuseB,
                        material.DiffuseA,
                        texturePath,
                        materialOverride?.AlphaMode ??
                            (material.DiffuseA < 0.999f ? 2 : 0),
                        transMapPath);
                })
                .ToArray();

            meshes.Add(new OmsiVehicleMeshAsset(
                mesh.DeclaredPath,
                meshPath,
                geometry.ErrorCode,
                mesh.Transform,
                mesh.ViewpointFlag,
                geometry.Positions,
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
            OmsiDriverPositionReader.ReadFile(bus.PassengerCabinPath));
    }

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
