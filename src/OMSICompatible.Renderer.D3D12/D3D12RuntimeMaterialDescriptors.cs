using Vortice.Direct3D12;

namespace OMSICompatible.Renderer.D3D12;

internal sealed class D3D12RuntimeMaterialDescriptors :
    IDisposable
{
    private readonly ID3D12DescriptorHeap _heap;

    private D3D12RuntimeMaterialDescriptors(
        ID3D12DescriptorHeap heap)
    {
        _heap = heap;
    }

    public ID3D12DescriptorHeap Heap =>
        _heap;

    public GpuDescriptorHandle GpuHandle =>
        _heap.GetGPUDescriptorHandleForHeapStart();

    public static D3D12RuntimeMaterialDescriptors Create(
        ID3D12Device device,
        D3D12RuntimeTexture diffuse,
        D3D12RuntimeTexture transMap)
    {
        ArgumentNullException.ThrowIfNull(
            device);

        ArgumentNullException.ThrowIfNull(
            diffuse);

        ArgumentNullException.ThrowIfNull(
            transMap);

        var heap =
            device.CreateDescriptorHeap(
                new DescriptorHeapDescription(
                    DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                    2,
                    DescriptorHeapFlags.ShaderVisible));

        device.CopyDescriptors(
            1,
            [
                heap.GetCPUDescriptorHandleForHeapStart()
            ],
            [
                2u
            ],
            2,
            [
                diffuse.CpuHandle,
                transMap.CpuHandle
            ],
            [
                1u,
                1u
            ],
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);

        return new D3D12RuntimeMaterialDescriptors(
            heap);
    }

    public void Dispose()
    {
        _heap.Dispose();
    }
}
