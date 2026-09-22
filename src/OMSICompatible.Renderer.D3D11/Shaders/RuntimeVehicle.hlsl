cbuffer RuntimeCamera : register(b0)
{
    row_major float4x4 ViewProjection;
    float3 CameraPosition;
    float CameraPadding;
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
    float EnvMapStrength;
    float EnvMapMaskEnabled;
    float3 MaterialPadding;
};

Texture2D DiffuseTexture : register(t0);
Texture2D TransMapTexture : register(t1);
Texture2D LightMapTexture : register(t2);
Texture2D MaterialChangeTexture : register(t3);
Texture2D EnvMapTexture : register(t4);
Texture2D EnvMapMaskTexture : register(t5);
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
    float3 WorldPosition : TEXCOORD2;
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

    output.WorldPosition =
        worldPosition.xyz;

    return output;
}

float ResolveEnvMapMask(
    VertexOutput input)
{
    if (EnvMapMaskEnabled <= 0.0f)
    {
        return 1.0f;
    }

    float3 mask =
        EnvMapMaskTexture.Sample(
            DiffuseSampler,
            input.Uv).rgb;

    return saturate(
        dot(
            mask,
            float3(
                0.333333f,
                0.333333f,
                0.333333f)));
}

float4 ApplyEnvMap(
    float4 color,
    VertexOutput input)
{
    if (EnvMapStrength <= 0.0f)
    {
        return color;
    }

    float3 normal =
        normalize(
            input.WorldNormal);

    float3 viewDir =
        normalize(
            input.WorldPosition -
            CameraPosition);

    float2 envUv =
        clamp(
            (viewDir + normal).xy *
                0.5f +
            0.5f,
            0.01f,
            0.99f);

    float3 env =
        EnvMapTexture.Sample(
            DiffuseSampler,
            envUv).rgb;

    float mask =
        ResolveEnvMapMask(
            input);

    float strength =
        saturate(
            color.a *
            EnvMapStrength *
            mask);

    color.rgb =
        lerp(
            color.rgb,
            env,
            strength);

    return color;
}

float4 SampleDiffuse(
    VertexOutput input)
{
    float4 sampled =
        DiffuseTexture.Sample(
            DiffuseSampler,
            input.Uv) *
        input.Color;

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

    sampled =
        ApplyEnvMap(
            sampled,
            input);

    sampled.a *=
        AlphaScale;

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

    color =
        ApplyEnvMap(
            color,
            input);

    color.a *=
        AlphaScale;

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
