using OMSICompatible.Renderer.Common;
using Vortice.Direct3D12;

namespace OMSICompatible.Renderer.D3D12;

public sealed class D3D12RuntimeObjectResources :
    IDisposable
{
    private D3D12RuntimeObjectResources(
        D3D12RuntimeGeometryBuffer buffer)
    {
        Buffer = buffer;
    }

    public D3D12RuntimeGeometryBuffer Buffer
    {
        get;
    }

    public static D3D12RuntimeObjectResources Create(
        ID3D12Device device,
        ReadOnlySpan<RuntimeObjectVertex> vertices)
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
                vertices));
    }

    public void Dispose()
    {
        Buffer.Dispose();
    }
}
