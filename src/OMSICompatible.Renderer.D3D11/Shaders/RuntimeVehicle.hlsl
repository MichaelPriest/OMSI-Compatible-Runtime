cbuffer RuntimeCamera : register(b0)
{
    row_major float4x4 ViewProjection;
};

cbuffer RuntimeModel : register(b1)
{
    row_major float4x4 World;
};

cbuffer RuntimeMaterial : register(b2)
{
    float AlphaScale;
    float LightMapStrength;
    float MaterialChangeStrength;
    float MaterialPadding;
};

Texture2D DiffuseTexture : register(t0);
Texture2D TransMapTexture : register(t1);
Texture2D LightMapTexture : register(t2);
Texture2D MaterialChangeTexture : register(t3);
SamplerState DiffuseSampler : register(s0);

struct VertexInput
{
    float3 Position : POSITION;
    float4 Color : COLOR;
    float2 Uv : TEXCOORD;
    float3 Normal : NORMAL;
};

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR;
    float2 Uv : TEXCOORD;
    float3 WorldNormal : TEXCOORD1;
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;

    float4 worldPosition =
        mul(
            float4(
                input.Position,
                1.0f),
            World);

    output.Position =
        mul(
            worldPosition,
            ViewProjection);

    output.Color =
        input.Color;

    output.Uv =
        input.Uv;

    output.WorldNormal =
        normalize(
            mul(
                input.Normal,
                (float3x3)World));

    return output;
}

float4 SampleDiffuse(
    VertexOutput input)
{
    float4 sampled =
        DiffuseTexture.Sample(
            DiffuseSampler,
            input.Uv) *
        input.Color;

    sampled.a *=
        AlphaScale;

    if (LightMapStrength > 0.0f)
    {
        sampled.rgb +=
            LightMapTexture.Sample(
                DiffuseSampler,
                input.Uv).rgb *
            LightMapStrength;
    }

    if (MaterialChangeStrength > 0.0f)
    {
        sampled.rgb +=
            MaterialChangeTexture.Sample(
                DiffuseSampler,
                input.Uv).rgb *
            MaterialChangeStrength;
    }

    return sampled;
}

float ResolveTransMapAlpha(
    VertexOutput input)
{
    float4 trans =
        TransMapTexture.Sample(
            DiffuseSampler,
            input.Uv);

    float luminance =
        dot(
            trans.rgb,
            float3(
                0.333333f,
                0.333333f,
                0.333333f));

    return min(
        trans.a,
        luminance);
}

float4 PSColor(
    VertexOutput input) : SV_TARGET
{
    float4 color =
        input.Color;

    color.a *=
        AlphaScale;

    if (LightMapStrength > 0.0f)
    {
        color.rgb +=
            LightMapTexture.Sample(
                DiffuseSampler,
                input.Uv).rgb *
            LightMapStrength;
    }

    if (MaterialChangeStrength > 0.0f)
    {
        color.rgb +=
            MaterialChangeTexture.Sample(
                DiffuseSampler,
                input.Uv).rgb *
            MaterialChangeStrength;
    }

    return color;
}

float4 PSTextured(
    VertexOutput input) : SV_TARGET
{
    return SampleDiffuse(input);
}

float4 PSAlphaCutout(
    VertexOutput input) : SV_TARGET
{
    float4 sampled =
        SampleDiffuse(input);

    clip(
        sampled.a -
        0.35f);

    return sampled;
}

float4 PSAlphaBlend(
    VertexOutput input) : SV_TARGET
{
    return SampleDiffuse(input);
}

float4 PSAlphaCutoutTransMap(
    VertexOutput input) : SV_TARGET
{
    float4 sampled =
        SampleDiffuse(input);

    float alpha =
        ResolveTransMapAlpha(input) *
        input.Color.a *
        AlphaScale;

    clip(
        alpha -
        0.35f);

    sampled.a = 1.0f;
    return sampled;
}

float4 PSAlphaBlendTransMap(
    VertexOutput input) : SV_TARGET
{
    float4 sampled =
        SampleDiffuse(input);

    sampled.a =
        ResolveTransMapAlpha(input) *
        input.Color.a *
        AlphaScale;

    return sampled;
}
