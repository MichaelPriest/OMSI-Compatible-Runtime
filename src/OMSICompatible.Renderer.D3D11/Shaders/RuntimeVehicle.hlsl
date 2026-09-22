cbuffer RuntimeCamera : register(b0)
{
    row_major float4x4 ViewProjection;
};

cbuffer RuntimeModel : register(b1)
{
    row_major float4x4 World;
};

Texture2D DiffuseTexture : register(t0);
Texture2D TransMapTexture : register(t1);
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

    return output;
}

float4 SampleDiffuse(
    VertexOutput input)
{
    return
        DiffuseTexture.Sample(
            DiffuseSampler,
            input.Uv) *
        input.Color;
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
    return input.Color;
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
        input.Color.a;

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
        input.Color.a;

    return sampled;
}
