using OmsiCompat.Core;
using OmsiCompat.Scripting;
using OmsiCompat.Vehicles;
using OMSICompatible.Renderer.D3D11;

namespace OMSICompatible.Runtime;

internal sealed class VehiclePreviewApplicationContext :
    ApplicationContext
{
    private readonly D3D11RenderWindow _previewWindow;

    public VehiclePreviewApplicationContext(
        OmsiContentRoot contentRoot,
        OmsiBusInfo bus,
        string? repaintName = null,
        string? repaintCtiRelativePath = null)
    {
        ArgumentNullException.ThrowIfNull(
            contentRoot);
        ArgumentNullException.ThrowIfNull(
            bus);

        var repaint =
            ResolveRepaint(
                bus,
                repaintName,
                repaintCtiRelativePath);

        var vehicle =
            OmsiArticulatedVehicleAssetLoader.Load(
                contentRoot,
                bus,
                repaint:
                    repaint);

        if (vehicle.RenderableMeshCount <=
            0)
        {
            throw new InvalidOperationException(
                $"O veículo '{bus.SelectionLabel}' não possui meshes renderizáveis para a prévia 3D.");
        }

        var runtimeVehicle =
            RuntimeVehicleInfoFactory.FromAsset(
                vehicle);

        var runtimeInfo =
            new RuntimeWindowInfo(
                $"Prévia 3D · {bus.SelectionLabel}",
                0,
                0,
                null,
                0,
                0,
                contentRoot.RootPath,
                Array.Empty<RuntimeTileInfo>(),
                Array.Empty<RuntimeSplineInfo>(),
                Array.Empty<RuntimeObjectInfo>(),
                new Dictionary<
                    string,
                    RuntimeSceneryAssetInfo>(
                    StringComparer.OrdinalIgnoreCase),
                Array.Empty<RuntimeGroundTextureInfo>(),
                RuntimeTrafficPathNetworkInfo.Empty,
                RuntimeAiCatalogInfo.Empty,
                runtimeVehicle,
                new RuntimeSpawnInfo(
                    "Preview",
                    0.0,
                    0.0,
                    0.0,
                    0.0));

        OmsiScriptRuntime? scriptRuntime =
            null;

        if (bus.ScriptManifest.RegisteredFileCount >
            0)
        {
            try
            {
                scriptRuntime =
                    new OmsiScriptRuntime(
                        OmsiScriptCatalogLoader.Load(
                            contentRoot,
                            bus.ScriptManifest));
            }
            catch
            {
                // A script problem must not prevent a geometry-only preview.
            }
        }

        var sectionScriptRuntimes =
            new Dictionary<int, OmsiScriptRuntime>();

        foreach (var section in
                 vehicle.Sections ??
                 Array.Empty<OmsiVehicleSectionAssetInfo>())
        {
            if (section.ScriptManifest is not
                    { } manifest ||
                manifest.RegisteredFileCount <=
                    0)
            {
                continue;
            }

            try
            {
                sectionScriptRuntimes[
                    section.Index] =
                    new OmsiScriptRuntime(
                        OmsiScriptCatalogLoader.Load(
                            contentRoot,
                            manifest));
            }
            catch
            {
                // Keep preview available even when an articulated section
                // references an optional or broken script file.
            }
        }

        _previewWindow =
            new D3D11RenderWindow(
                runtimeInfo,
                scriptRuntime,
                targetFps:
                    60,
                vsync:
                    true,
                vehiclePreviewMode:
                    true,
                initialVehicleVariables:
                    repaint?.SetVariables,
                gameControllerEnabled:
                    false,
                sectionScriptRuntimes:
                    sectionScriptRuntimes,
                masterVolumePercent:
                    0,
                maximumSoundCount:
                    1,
                aiVehicleSoundsEnabled:
                    false,
                vehicleToVehicleCollisionsEnabled:
                    false,
                terrainCollisionsEnabled:
                    false);

        _previewWindow.Text =
            $"Prévia 3D · {bus.SelectionLabel}";

        _previewWindow.StartPosition =
            FormStartPosition.CenterScreen;

        _previewWindow.ClientSize =
            new Size(
                1100,
                700);

        _previewWindow.FormClosed +=
            (_, _) =>
                ExitThread();

        _previewWindow.PrepareForDisplay();

        MainForm =
            _previewWindow;

        _previewWindow.Show();
    }

    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            _previewWindow.Dispose();
        }

        base.Dispose(
            disposing);
    }

    private static OmsiVehicleRepaint? ResolveRepaint(
        OmsiBusInfo bus,
        string? repaintName,
        string? repaintCtiRelativePath)
    {
        if (string.IsNullOrWhiteSpace(
                repaintName))
        {
            return null;
        }

        return OmsiVehicleRepaintCatalog
            .Discover(
                bus)
            .FirstOrDefault(
                repaint =>
                    string.Equals(
                        repaint.Name,
                        repaintName,
                        StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(
                         repaintCtiRelativePath) ||
                     string.Equals(
                         repaint.RelativeCtiPath,
                         repaintCtiRelativePath,
                         StringComparison.OrdinalIgnoreCase)));
    }
}
