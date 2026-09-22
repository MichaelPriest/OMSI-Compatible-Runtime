using Vortice.Direct3D11;
using Vortice.DXGI;

namespace OMSICompatible.Renderer.D3D11;

internal sealed class RuntimeReflectionTarget :
    IDisposable
{
    public RuntimeReflectionTarget(
        ID3D11Device device,
        RuntimeReflectionCameraInfo camera,
        uint textureSize)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(camera);

        Camera = camera;

        Texture =
            device.CreateTexture2D(
                Format.R8G8B8A8_UNorm,
                textureSize,
                textureSize,
                mipLevels: 1,
                bindFlags:
                    BindFlags.RenderTarget |
                    BindFlags.ShaderResource);

        RenderTargetView =
            device.CreateRenderTargetView(
                Texture);

        ShaderResourceView =
            device.CreateShaderResourceView(
                Texture);
    }

    public RuntimeReflectionCameraInfo Camera
    {
        get;
    }

    public ID3D11Texture2D Texture
    {
        get;
    }

    public ID3D11RenderTargetView RenderTargetView
    {
        get;
    }

    public ID3D11ShaderResourceView ShaderResourceView
    {
        get;
    }

    public void Dispose()
    {
        ShaderResourceView.Dispose();
        RenderTargetView.Dispose();
        Texture.Dispose();
    }
}
