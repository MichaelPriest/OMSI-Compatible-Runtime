cbuffer RuntimeCamera : register(b0)
{
    row_major float4x4 ViewProjection;
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
    output.Position = mul(
        float4(input.Position, 1.0f),
        ViewProjection);
    output.Color = input.Color;
    output.Uv = input.Uv;
    return output;
}

float4 SampleDiffuse(VertexOutput input)
{
    return DiffuseTexture.Sample(
        DiffuseSampler,
        input.Uv) * input.Color;
}

float ResolveTransMapAlpha(VertexOutput input)
{
    // Match OMSI's Direct3D material behavior: [matl_transmap] uses the
    // texture alpha channel. A 24-bit mask has no alpha channel and is
    // sampled as opaque; treating its RGB luminance as opacity makes black
    // RGB masks (including transmap_null.tga) erase otherwise solid meshes.
    return saturate(
        TransMapTexture.Sample(
            DiffuseSampler,
            input.Uv).a);
}

float4 PSColor(VertexOutput input) : SV_TARGET
{
    return input.Color;
}

float4 PSTextured(VertexOutput input) : SV_TARGET
{
    return SampleDiffuse(input);
}

float4 PSAlphaCutout(VertexOutput input) : SV_TARGET
{
    float4 sampled =
        SampleDiffuse(input);

    clip(sampled.a - 0.35f);
    return sampled;
}

float4 PSAlphaBlend(VertexOutput input) : SV_TARGET
{
    return SampleDiffuse(input);
}

float4 PSAlphaCutoutTransMap(VertexOutput input) : SV_TARGET
{
    float4 sampled =
        SampleDiffuse(input);

    float alpha =
        ResolveTransMapAlpha(input) *
        input.Color.a;

    clip(alpha - 0.35f);
    sampled.a = 1.0f;
    return sampled;
}

float4 PSAlphaBlendTransMap(VertexOutput input) : SV_TARGET
{
    float4 sampled =
        SampleDiffuse(input);

    sampled.a =
        ResolveTransMapAlpha(input) *
        input.Color.a;

    return sampled;
}
