cbuffer RuntimeCamera : register(b0)
{
    row_major float4x4 ViewProjection;
};

Texture2D DiffuseTexture : register(t0);
SamplerState DiffuseSampler : register(s0);

struct VertexInput
{
    float3 Position : POSITION;
    float4 Color : COLOR;
    float2 Uv : TEXCOORD;
};

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR;
    float2 Uv : TEXCOORD;
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(
        float4(input.Position, 1.0f),
        ViewProjection);
    output.Color = input.Color;
    output.Uv = input.Uv;
    return output;
}

float4 PSColor(VertexOutput input) : SV_TARGET
{
    return input.Color;
}

float4 PSTextured(VertexOutput input) : SV_TARGET
{
    return DiffuseTexture.Sample(
        DiffuseSampler,
        input.Uv) * input.Color;
}

float4 PSAlphaCutout(VertexOutput input) : SV_TARGET
{
    float4 sampled =
        DiffuseTexture.Sample(
            DiffuseSampler,
            input.Uv) * input.Color;

    clip(sampled.a - 0.35f);
    return sampled;
}
