using OMSICompatible.Renderer.Common;
using Vortice.Direct3D12;

namespace OMSICompatible.Renderer.D3D12;

public sealed class D3D12RuntimeTerrainResources :
    IDisposable
{
    private D3D12RuntimeTerrainResources(
        RuntimeTerrainGeometry geometry,
        D3D12RuntimeGeometryBuffer buffer)
    {
        Geometry = geometry;
        Buffer = buffer;
    }

    public RuntimeTerrainGeometry Geometry
    {
        get;
    }

    public D3D12RuntimeGeometryBuffer Buffer
    {
        get;
    }

    public IReadOnlyList<RuntimeTerrainBatch> Batches =>
        Geometry.Batches;

    public static D3D12RuntimeTerrainResources Create(
        ID3D12Device device,
        RuntimeTerrainGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(
            device);

        ArgumentNullException.ThrowIfNull(
            geometry);

        if (geometry.Vertices.Length ==
            0)
        {
            throw new ArgumentException(
                "Terrain geometry contains no vertices.",
                nameof(geometry));
        }

        return new D3D12RuntimeTerrainResources(
            geometry,
            D3D12RuntimeGeometryBuffer.Create(
                device,
                geometry.Vertices));
    }

    public void Dispose()
    {
        Buffer.Dispose();
    }
}
