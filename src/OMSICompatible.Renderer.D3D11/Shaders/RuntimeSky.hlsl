cbuffer RuntimeSky : register(b0)
{
    // x = yaw, y = pitch, z = tan(verticalFov / 2), w = aspect.
    float4 SkyViewParameters;
};

Texture2D SkyTexture : register(t0);
SamplerState SkySampler : register(s0);

struct SkyVertexOutput
{
    float4 Position : SV_POSITION;
    float2 Uv : TEXCOORD0;
};

SkyVertexOutput VSMain(uint vertexId : SV_VertexID)
{
    SkyVertexOutput output;

    float2 position =
        float2(
            (vertexId << 1) & 2,
            vertexId & 2);

    output.Uv =
        float2(
            position.x,
            1.0f - position.y);

    output.Position =
        float4(
            position.x * 2.0f - 1.0f,
            1.0f - position.y * 2.0f,
            0.9999f,
            1.0f);

    return output;
}

float4 PSMain(SkyVertexOutput input) : SV_TARGET
{
    const float Pi =
        3.14159265358979323846f;
    const float TwoPi =
        Pi * 2.0f;

    float yaw =
        SkyViewParameters.x;
    float pitch =
        SkyViewParameters.y;
    float tanHalfFovY =
        max(
            SkyViewParameters.z,
            0.001f);
    float aspect =
        max(
            SkyViewParameters.w,
            0.1f);

    // Full-screen UV is reconstructed into the same camera ray used by the
    // 3D scene. The OMSI horizon therefore remains on the world horizon
    // instead of sliding up/down as a 2D backdrop when the camera pitches.
    float2 screen =
        float2(
            input.Uv.x * 2.0f - 1.0f,
            input.Uv.y * 2.0f - 1.0f);

    float sinYaw =
        sin(yaw);
    float cosYaw =
        cos(yaw);
    float sinPitch =
        sin(pitch);
    float cosPitch =
        cos(pitch);

    float3 forward =
        float3(
            sinYaw * cosPitch,
            sinPitch,
            cosYaw * cosPitch);

    float3 right =
        float3(
            cosYaw,
            0.0f,
            -sinYaw);

    float3 up =
        normalize(
            cross(
                forward,
                right));

    float3 direction =
        normalize(
            forward +
            right *
                screen.x *
                aspect *
                tanHalfFovY +
            up *
                screen.y *
                tanHalfFovY);

    float2 skyUv =
        float2(
            frac(
                0.5f -
                atan2(
                    direction.x,
                    direction.z) /
                TwoPi),
            saturate(
                0.5f -
                asin(
                    clamp(
                        direction.y,
                        -1.0f,
                        1.0f)) /
                Pi));

    float4 sampled =
        SkyTexture.Sample(
            SkySampler,
            skyUv);

    sampled.a = 1.0f;

    return sampled;
}
