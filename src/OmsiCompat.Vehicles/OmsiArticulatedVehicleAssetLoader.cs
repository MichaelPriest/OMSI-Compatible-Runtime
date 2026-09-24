using OmsiCompat.Core;

namespace OmsiCompat.Vehicles;

/// <summary>
/// Loads the complete OMSI vehicle chain described by [couple_back].
/// Each coupled .bus keeps its own model.cfg/materials, while its meshes are
/// translated into the coordinate space of the leading vehicle so the full
/// articulated bus is rendered by the existing vehicle pipeline.
/// </summary>
public static class OmsiArticulatedVehicleAssetLoader
{
    private const int MaximumSectionCount = 8;

    public static OmsiVehicleAsset Load(
        OmsiContentRoot contentRoot,
        OmsiBusInfo bus,
        IProgress<OmsiVehicleLoadProgress>? progress = null,
        OmsiVehicleRepaint? repaint = null)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentNullException.ThrowIfNull(bus);

        var leadingProgress =
            progress is null
                ? null
                : new RangeProgress(
                    progress,
                    0,
                    70);

        var leading =
            OmsiVehicleAssetLoader.Load(
                contentRoot,
                bus,
                leadingProgress,
                repaint);

        if (bus.CoupledBack is null)
        {
            progress?.Report(
                new OmsiVehicleLoadProgress(
                    100,
                    "Veículo sem seções acopladas."));
            return leading;
        }

        var meshes =
            new List<OmsiVehicleMeshAsset>(
                leading.Meshes);

        var textTextures =
            new List<OmsiVehicleTextTexture>(
                leading.TextTextures);

        var sections =
            new List<OmsiVehicleSectionAssetInfo>();

        var visited =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(
                    bus.FilePath)
            };

        var parent =
            bus;

        var parentOffset =
            new SectionOffset(
                0.0,
                0.0,
                0.0);

        var sectionCount = 1;

        while (sectionCount <
               MaximumSectionCount &&
               parent.CoupledBack is
                   { } coupledBack)
        {
            var coupledPath =
                ResolveCoupledBusPath(
                    contentRoot,
                    parent,
                    coupledBack.DeclaredBusPath);

            if (coupledPath is null)
            {
                Console.WriteLine(
                    $"[vehicle-articulation] Coupled section not found: {coupledBack.DeclaredBusPath}");
                break;
            }

            if (!visited.Add(
                    coupledPath))
            {
                Console.WriteLine(
                    $"[vehicle-articulation] Coupling cycle detected at {coupledPath}; chain stopped.");
                break;
            }

            var childBus =
                OmsiBusReader.ReadFile(
                    contentRoot.RootPath,
                    coupledPath);

            if (parent.BackCoupling is
                    not { } parentBack ||
                childBus.FrontCoupling is
                    not { } childFront)
            {
                Console.WriteLine(
                    $"[vehicle-articulation] Missing coupling point between {Path.GetFileName(parent.FilePath)} and {Path.GetFileName(childBus.FilePath)}; chain stopped to avoid overlapping sections.");
                break;
            }

            var childOffset =
                new SectionOffset(
                    parentOffset.X +
                    parentBack.X -
                    childFront.X,
                    parentOffset.Y +
                    parentBack.Y -
                    childFront.Y,
                    parentOffset.Z +
                    parentBack.Z -
                    childFront.Z);

            var childRepaint =
                ResolveMatchingRepaint(
                    childBus,
                    repaint);

            var child =
                OmsiVehicleAssetLoader.Load(
                    contentRoot,
                    childBus,
                    progress: null,
                    repaint:
                        childRepaint);

            var textTextureOffset =
                textTextures.Count == 0
                    ? 0
                    : textTextures.Max(
                          static texture =>
                              texture.Index) +
                      1;

            foreach (var texture in
                     child.TextTextures)
            {
                textTextures.Add(
                    texture with
                    {
                        Index =
                            texture.Index +
                            textTextureOffset
                    });
            }

            var sectionPrefix =
                $"section{sectionCount}:";

            meshes.AddRange(
                child.Meshes.Select(
                    mesh =>
                        OffsetMesh(
                            mesh,
                            childOffset,
                            textTextureOffset,
                            sectionPrefix,
                            sectionCount)));

            var joint =
                new SectionOffset(
                    parentOffset.X +
                    parentBack.X,
                    parentOffset.Y +
                    parentBack.Y,
                    parentOffset.Z +
                    parentBack.Z);

            var followerLength =
                ResolveFollowerLength(
                    childBus,
                    childFront);

            sections.Add(
                new OmsiVehicleSectionAssetInfo(
                    sectionCount,
                    sectionCount - 1,
                    joint.X,
                    joint.Y,
                    joint.Z,
                    followerLength,
                    Math.Clamp(
                        childBus.FrontCouplingCharacter?
                            .MaximumYawDegrees ??
                        55.0,
                        5.0,
                        89.0),
                    coupledBack.Reverse));

            sectionCount++;

            progress?.Report(
                new OmsiVehicleLoadProgress(
                    Math.Clamp(
                        70 +
                        sectionCount * 4,
                        72,
                        98),
                    $"Seção articulada {sectionCount}: {childBus.DisplayName}"));

            Console.WriteLine(
                $"[vehicle-articulation] section={sectionCount - 1}; file={childBus.RelativePath}; offset=({childOffset.X:0.###},{childOffset.Y:0.###},{childOffset.Z:0.###}); meshes={child.Meshes.Count}; reverse={coupledBack.Reverse}");

            parent =
                childBus;

            parentOffset =
                childOffset;
        }

        if (sectionCount ==
                MaximumSectionCount &&
            parent.CoupledBack is not null)
        {
            Console.WriteLine(
                $"[vehicle-articulation] Maximum section count ({MaximumSectionCount}) reached; remaining coupled sections were not loaded.");
        }

        progress?.Report(
            new OmsiVehicleLoadProgress(
                100,
                $"Veículo carregado com {sectionCount} seção(ões) e {meshes.Count} mesh(es)."));

        return leading with
        {
            Meshes =
                meshes.ToArray(),
            TextTextures =
                textTextures.ToArray(),
            SectionCount =
                sectionCount,
            Sections =
                sections.ToArray()
        };
    }

    private static OmsiVehicleMeshAsset OffsetMesh(
        OmsiVehicleMeshAsset mesh,
        SectionOffset offset,
        int textTextureOffset,
        string sectionPrefix,
        int sectionIndex)
    {
        var transform =
            mesh.Transform with
            {
                PositionX =
                    mesh.Transform.PositionX +
                    offset.X,
                PositionY =
                    mesh.Transform.PositionY +
                    offset.Y,
                PositionZ =
                    mesh.Transform.PositionZ +
                    offset.Z
            };

        var animations =
            mesh.Animations
                .Select(
                    animation =>
                        animation.OriginFromMesh
                            ? animation
                            : animation with
                            {
                                OriginX =
                                    animation.OriginX +
                                    offset.X,
                                OriginY =
                                    animation.OriginY +
                                    offset.Y,
                                OriginZ =
                                    animation.OriginZ +
                                    offset.Z
                            })
                .ToArray();

        var lights =
            mesh.LightEffects?
                .Select(
                    light =>
                        light with
                        {
                            PositionX =
                                light.PositionX +
                                offset.X,
                            PositionY =
                                light.PositionY +
                                offset.Y,
                            PositionZ =
                                light.PositionZ +
                                offset.Z
                        })
                .ToArray();

        var materials =
            mesh.Materials
                .Select(
                    material =>
                        RemapMaterialTextTextures(
                            material,
                            textTextureOffset))
                .ToArray();

        return mesh with
        {
            Transform =
                transform,
            Animations =
                animations,
            LightEffects =
                lights,
            Materials =
                materials,
            MeshIdentifier =
                string.IsNullOrWhiteSpace(
                    mesh.MeshIdentifier)
                    ? null
                    : sectionPrefix +
                      mesh.MeshIdentifier,
            AnimationParent =
                string.IsNullOrWhiteSpace(
                    mesh.AnimationParent)
                    ? null
                    : sectionPrefix +
                      mesh.AnimationParent,
            SectionIndex =
                sectionIndex
        };
    }

    private static OmsiVehicleMaterial RemapMaterialTextTextures(
        OmsiVehicleMaterial material,
        int offset)
    {
        if (offset <= 0)
        {
            return material;
        }

        var changeSets =
            material.MaterialChangeSets?
                .Select(
                    set =>
                        set with
                        {
                            Items =
                                set.Items
                                    .Select(
                                        item =>
                                            item.TextTextureIndex.HasValue
                                                ? item with
                                                {
                                                    TextTextureIndex =
                                                        item.TextTextureIndex.Value +
                                                        offset
                                                }
                                                : item)
                                    .ToArray()
                        })
                .ToArray();

        return material with
        {
            TextTextureIndex =
                material.TextTextureIndex.HasValue
                    ? material.TextTextureIndex.Value +
                      offset
                    : null,
            MaterialChangeSets =
                changeSets
        };
    }

    private static double ResolveFollowerLength(
        OmsiBusInfo childBus,
        OmsiVehicleCouplingPoint childFront)
    {
        var rotationPoint =
            childBus.Physics
                .RotationPointLongitudinalMeters;

        if (rotationPoint.HasValue)
        {
            var distance =
                Math.Abs(
                    childFront.Y -
                    rotationPoint.Value);

            if (double.IsFinite(
                    distance) &&
                distance > 0.75)
            {
                return Math.Clamp(
                    distance,
                    1.0,
                    15.0);
            }
        }

        if (childBus.Physics.WheelBaseMeters is
            { } wheelBase &&
            double.IsFinite(
                wheelBase))
        {
            return Math.Clamp(
                wheelBase,
                1.0,
                15.0);
        }

        return 5.5;
    }

    private static OmsiVehicleRepaint?
        ResolveMatchingRepaint(
            OmsiBusInfo childBus,
            OmsiVehicleRepaint? leadingRepaint)
    {
        if (leadingRepaint is null)
        {
            return null;
        }

        return OmsiVehicleRepaintCatalog
            .Discover(
                childBus)
            .FirstOrDefault(
                candidate =>
                    string.Equals(
                        candidate.Name,
                        leadingRepaint.Name,
                        StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveCoupledBusPath(
        OmsiContentRoot contentRoot,
        OmsiBusInfo parent,
        string declaredPath)
    {
        if (string.IsNullOrWhiteSpace(
                declaredPath))
        {
            return null;
        }

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

        if (Path.IsPathRooted(
                normalized))
        {
            return null;
        }

        var vehiclesRoot =
            Path.GetFullPath(
                Path.Combine(
                    contentRoot.RootPath,
                    "Vehicles"));

        var candidates =
            new[]
            {
                Path.Combine(
                    parent.DirectoryPath,
                    normalized),
                Path.Combine(
                    vehiclesRoot,
                    normalized)
            };

        foreach (var candidate in
                 candidates)
        {
            try
            {
                var full =
                    Path.GetFullPath(
                        candidate);

                if (!IsUnderRoot(
                        full,
                        vehiclesRoot) ||
                    !File.Exists(
                        full) ||
                    !string.Equals(
                        Path.GetExtension(
                            full),
                        ".bus",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return full;
            }
            catch
            {
                // Try the next OMSI-compatible resolution candidate.
            }
        }

        return null;
    }

    private static bool IsUnderRoot(
        string candidate,
        string root)
    {
        var normalizedRoot =
            root.EndsWith(
                Path.DirectorySeparatorChar)
                ? root
                : root +
                  Path.DirectorySeparatorChar;

        return candidate.StartsWith(
                   normalizedRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   candidate,
                   root,
                   StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct SectionOffset(
        double X,
        double Y,
        double Z);

    private sealed class RangeProgress :
        IProgress<OmsiVehicleLoadProgress>
    {
        private readonly IProgress<OmsiVehicleLoadProgress>
            _inner;

        private readonly int _minimum;
        private readonly int _maximum;

        public RangeProgress(
            IProgress<OmsiVehicleLoadProgress> inner,
            int minimum,
            int maximum)
        {
            _inner =
                inner;
            _minimum =
                minimum;
            _maximum =
                maximum;
        }

        public void Report(
            OmsiVehicleLoadProgress value)
        {
            var mapped =
                _minimum +
                (int)Math.Round(
                    Math.Clamp(
                        value.Percent,
                        0,
                        100) /
                    100.0 *
                    (_maximum -
                     _minimum));

            _inner.Report(
                new OmsiVehicleLoadProgress(
                    mapped,
                    value.Detail));
        }
    }
}
