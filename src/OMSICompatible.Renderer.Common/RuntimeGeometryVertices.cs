using System.Numerics;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.Common;

public readonly struct RuntimeTerrainVertex
{
    public const uint SizeInBytes = 52;

    public RuntimeTerrainVertex(
        Vector3 position,
        Color4 color,
        Vector2 uv,
        Vector2 maskUv,
        Vector2 detailUv)
    {
        Position = position;
        Color = color;
        Uv = uv;
        MaskUv = maskUv;
        DetailUv = detailUv;
    }

    public readonly Vector3 Position;
    public readonly Color4 Color;
    public readonly Vector2 Uv;
    public readonly Vector2 MaskUv;
    public readonly Vector2 DetailUv;
}

public readonly struct RuntimeObjectVertex
{
    public const uint SizeInBytes = 64;

    public RuntimeObjectVertex(
        Vector3 position,
        Color4 color,
        Vector2 uv,
        Vector3 normal,
        Vector4 skinWeights)
    {
        Position = position;
        Color = color;
        Uv = uv;
        Normal = normal;
        SkinWeights = skinWeights;
    }

    public RuntimeObjectVertex(
        Vector3 position,
        Color4 color,
        Vector2 uv,
        Vector3 normal)
        : this(
            position,
            color,
            uv,
            normal,
            Vector4.Zero)
    {
    }

    public RuntimeObjectVertex(
        Vector3 position,
        Color4 color,
        Vector2 uv)
        : this(
            position,
            color,
            uv,
            Vector3.UnitY,
            Vector4.Zero)
    {
    }

    public readonly Vector3 Position;
    public readonly Color4 Color;
    public readonly Vector2 Uv;
    public readonly Vector3 Normal;
    public readonly Vector4 SkinWeights;
}
