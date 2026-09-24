cbuffer RuntimeSky : register(b0)
{
    float2 SkyUvOffset;
    float2 SkyPadding;
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
    float2 skyUv =
        float2(
            frac(
                input.Uv.x +
                SkyUvOffset.x),
            saturate(
                input.Uv.y +
                SkyUvOffset.y));

    float4 sampled =
        SkyTexture.Sample(
            SkySampler,
            skyUv);

    sampled.a = 1.0f;

    return sampled;
}
