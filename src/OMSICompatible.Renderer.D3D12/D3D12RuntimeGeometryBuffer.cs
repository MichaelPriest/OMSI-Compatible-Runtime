using OMSICompatible.Renderer.Common;
using Vortice.Direct3D12;

namespace OMSICompatible.Renderer.D3D12;

public sealed class D3D12RuntimeGeometryBuffer :
    IDisposable
{
    private readonly ID3D12Resource _resource;

    private D3D12RuntimeGeometryBuffer(
        ID3D12Resource resource,
        VertexBufferView view,
        int vertexCount)
    {
        _resource = resource;
        View = view;
        VertexCount = vertexCount;
    }

    public VertexBufferView View
    {
        get;
    }

    public int VertexCount
    {
        get;
    }

    public static D3D12RuntimeGeometryBuffer Create(
        ID3D12Device device,
        ReadOnlySpan<RuntimeTerrainVertex> vertices)
    {
        return CreateCore(
            device,
            vertices,
            RuntimeTerrainVertex.SizeInBytes);
    }

    public static D3D12RuntimeGeometryBuffer Create(
        ID3D12Device device,
        ReadOnlySpan<RuntimeObjectVertex> vertices)
    {
        return CreateCore(
            device,
            vertices,
            RuntimeObjectVertex.SizeInBytes);
    }

    private static D3D12RuntimeGeometryBuffer CreateCore<T>(
        ID3D12Device device,
        ReadOnlySpan<T> vertices,
        uint stride)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(
            device);

        if (vertices.Length ==
            0)
        {
            throw new ArgumentException(
                "At least one vertex is required.",
                nameof(vertices));
        }

        var byteCount =
            checked(
                (ulong)vertices.Length *
                stride);

        var resource =
            device.CreateCommittedResource(
                HeapType.Upload,
                ResourceDescription.Buffer(
                    byteCount),
                ResourceStates.GenericRead);

        try
        {
            resource.SetData(
                vertices);

            return new D3D12RuntimeGeometryBuffer(
                resource,
                new VertexBufferView(
                    resource.GPUVirtualAddress,
                    checked(
                        (uint)byteCount),
                    stride),
                vertices.Length);
        }
        catch
        {
            resource.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _resource.Dispose();
    }
}
