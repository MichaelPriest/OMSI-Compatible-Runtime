using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace OMSICompatible.Renderer.D3D12;

public sealed record D3D12BackendInfo(
    bool Available,
    string AdapterName,
    FeatureLevel FeatureLevel,
    bool SoftwareAdapter,
    string? Error);

public static class D3D12BackendProbe
{
    private static readonly FeatureLevel[] RequestedFeatureLevels =
    [
        FeatureLevel.Level_12_2,
        FeatureLevel.Level_12_1,
        FeatureLevel.Level_12_0,
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0
    ];

    public static D3D12BackendInfo Probe(
        bool preferHardware = true)
    {
        try
        {
            using var factory =
                CreateDXGIFactory2<IDXGIFactory4>(
                    false);

            D3D12BackendInfo? softwareFallback =
                null;

            for (uint index = 0;
                 factory.EnumAdapters1(
                         index,
                         out IDXGIAdapter1? adapter)
                     .Success;
                 index++)
            {
                using (adapter)
                {
                    if (adapter is null)
                    {
                        continue;
                    }

                    var description =
                        adapter.Description1;

                    var software =
                        (description.Flags &
                         AdapterFlags.Software) !=
                        AdapterFlags.None;

                    foreach (var featureLevel in
                             RequestedFeatureLevels)
                    {
                        var result =
                            D3D12CreateDevice<ID3D12Device>(
                                adapter,
                                featureLevel,
                                out var device);

                        if (result.Failure ||
                            device is null)
                        {
                            continue;
                        }

                        using (device)
                        {
                            var info =
                                new D3D12BackendInfo(
                                    true,
                                    description.Description,
                                    featureLevel,
                                    software,
                                    null);

                            if (!software ||
                                !preferHardware)
                            {
                                return info;
                            }

                            softwareFallback ??=
                                info;
                        }

                        break;
                    }
                }
            }

            if (softwareFallback is not null)
            {
                return softwareFallback;
            }

            return new D3D12BackendInfo(
                false,
                "<none>",
                FeatureLevel.Level_11_0,
                false,
                "No Direct3D 12 capable adapter was found.");
        }
        catch (Exception exception)
        {
            return new D3D12BackendInfo(
                false,
                "<none>",
                FeatureLevel.Level_11_0,
                false,
                exception.Message);
        }
    }
}
