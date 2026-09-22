cbuffer RuntimeCamera : register(b0)
{
    row_major float4x4 ViewProjection;
};

Texture2D DiffuseTexture : register(t0);
Texture2D MaskTexture : register(t1);
Texture2D DetailTexture : register(t2);

SamplerState DiffuseSampler : register(s0);
SamplerState MaskSampler : register(s1);

struct VertexInput
{
    float3 Position : POSITION;
    float4 Color : COLOR;
    float2 Uv : TEXCOORD0;
    float2 MaskUv : TEXCOORD1;
    float2 DetailUv : TEXCOORD2;
};

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR;
    float2 Uv : TEXCOORD0;
    float2 MaskUv : TEXCOORD1;
    float2 DetailUv : TEXCOORD2;
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    output.Position =
        mul(
            float4(input.Position, 1.0f),
            ViewProjection);
    output.Color = input.Color;
    output.Uv = input.Uv;
    output.MaskUv = input.MaskUv;
    output.DetailUv = input.DetailUv;
    return output;
}

float3 ComposeDetail(
    float3 baseRgb,
    float3 detailRgb)
{
    float3 modulation =
        lerp(
            float3(1.0f, 1.0f, 1.0f),
            saturate(detailRgb * 2.0f),
            0.35f);

    return saturate(
        baseRgb * modulation);
}

float4 PSColor(VertexOutput input) : SV_TARGET
{
    return input.Color;
}

float4 PSTextured(VertexOutput input) : SV_TARGET
{
    return
        DiffuseTexture.Sample(
            DiffuseSampler,
            input.Uv) *
        input.Color;
}

float4 PSBaseDetail(VertexOutput input) : SV_TARGET
{
    float4 baseColor =
        DiffuseTexture.Sample(
            DiffuseSampler,
            input.Uv);

    float4 detail =
        DetailTexture.Sample(
            DiffuseSampler,
            input.DetailUv);

    return float4(
        ComposeDetail(
            baseColor.rgb,
            detail.rgb) *
            input.Color.rgb,
        1.0f);
}

float4 PSLayer(VertexOutput input) : SV_TARGET
{
    float4 baseColor =
        DiffuseTexture.Sample(
            DiffuseSampler,
            input.Uv);

    float mask =
        MaskTexture.Sample(
            MaskSampler,
            input.MaskUv).a;

    return float4(
        baseColor.rgb *
        input.Color.rgb,
        baseColor.a *
        input.Color.a *
        mask);
}

float4 PSLayerDetail(VertexOutput input) : SV_TARGET
{
    float4 baseColor =
        DiffuseTexture.Sample(
            DiffuseSampler,
            input.Uv);

    float4 detail =
        DetailTexture.Sample(
            DiffuseSampler,
            input.DetailUv);

    float mask =
        MaskTexture.Sample(
            MaskSampler,
            input.MaskUv).a;

    return float4(
        ComposeDetail(
            baseColor.rgb,
            detail.rgb) *
            input.Color.rgb,
        baseColor.a *
        input.Color.a *
        mask);
}

float4 PSLightmap(VertexOutput input) : SV_TARGET
{
    float4 light =
        DiffuseTexture.Sample(
            DiffuseSampler,
            input.Uv);

    return float4(
        light.rgb,
        1.0f);
}
