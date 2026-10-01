using OMSICompatible.Renderer.Common;
using Vortice.Direct3D12;

namespace OMSICompatible.Renderer.D3D12;

public sealed class D3D12RuntimeObjectResources :
    IDisposable
{
    private D3D12RuntimeObjectResources(
        D3D12RuntimeGeometryBuffer buffer,
        IReadOnlyList<RuntimeObjectDrawBatch> batches)
    {
        Buffer = buffer;
        Batches = batches;
    }

    public D3D12RuntimeGeometryBuffer Buffer
    {
        get;
    }

    public IReadOnlyList<RuntimeObjectDrawBatch> Batches
    {
        get;
    }

    public static D3D12RuntimeObjectResources Create(
        ID3D12Device device,
        ReadOnlySpan<RuntimeObjectVertex> vertices,
        IReadOnlyList<RuntimeObjectDrawBatch>? batches = null)
    {
        ArgumentNullException.ThrowIfNull(
            device);

        if (vertices.Length ==
            0)
        {
            throw new ArgumentException(
                "Object geometry contains no vertices.",
                nameof(vertices));
        }

        return new D3D12RuntimeObjectResources(
            D3D12RuntimeGeometryBuffer.Create(
                device,
                vertices),
            batches ??
            Array.Empty<RuntimeObjectDrawBatch>());
    }

    public void Dispose()
    {
        Buffer.Dispose();
    }
}
