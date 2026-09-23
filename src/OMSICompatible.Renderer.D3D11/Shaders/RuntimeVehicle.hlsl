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
    float BumpMapStrength;
    float MaterialChangeTextureEnabled;
    float MaterialChangeColorEnabled;

    float4 MaterialChangeDiffuse;
    float4 BaseEmissive;
    float4 MaterialChangeEmissive;
};

Texture2D DiffuseTexture : register(t0);
Texture2D TransMapTexture : register(t1);
Texture2D LightMapTexture : register(t2);
Texture2D MaterialChangeTexture : register(t3);
Texture2D EnvMapTexture : register(t4);
Texture2D EnvMapMaskTexture : register(t5);
Texture2D BumpMapTexture : register(t6);
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

float3 ResolveSurfaceNormal(
    VertexOutput input)
{
    float3 normal =
        normalize(
            input.WorldNormal);

    if (BumpMapStrength <= 0.0f)
    {
        return normal;
    }

    float3 dpdx =
        ddx(
            input.WorldPosition);
    float3 dpdy =
        ddy(
            input.WorldPosition);

    float2 duvdx =
        ddx(
            input.Uv);
    float2 duvdy =
        ddy(
            input.Uv);

    float determinant =
        duvdx.x *
            duvdy.y -
        duvdx.y *
            duvdy.x;

    if (abs(
            determinant) <
        0.000001f)
    {
        return normal;
    }

    float inverseDeterminant =
        1.0f /
        determinant;

    float3 tangent =
        normalize(
            (dpdx *
                duvdy.y -
             dpdy *
                duvdx.y) *
            inverseDeterminant);

    float3 bitangent =
        normalize(
            (-dpdx *
                duvdy.x +
             dpdy *
                duvdx.x) *
            inverseDeterminant);

    const float sampleOffset =
        0.02f;

    float heightLeft =
        BumpMapTexture.Sample(
            DiffuseSampler,
            input.Uv +
                float2(
                    -sampleOffset,
                    0.0f)).r;

    float heightRight =
        BumpMapTexture.Sample(
            DiffuseSampler,
            input.Uv +
                float2(
                    sampleOffset,
                    0.0f)).r;

    float heightDown =
        BumpMapTexture.Sample(
            DiffuseSampler,
            input.Uv +
                float2(
                    0.0f,
                    -sampleOffset)).r;

    float heightUp =
        BumpMapTexture.Sample(
            DiffuseSampler,
            input.Uv +
                float2(
                    0.0f,
                    sampleOffset)).r;

    float2 gradient =
        float2(
            heightRight -
                heightLeft,
            heightUp -
                heightDown) *
        BumpMapStrength;

    return normalize(
        normal +
        tangent *
            gradient.x +
        bitangent *
            gradient.y);
}

float ResolveEnvMapMask(
    VertexOutput input)
{
    if (EnvMapMaskEnabled >= 1.5f)
    {
        // Empty OMSI [matl_transmap]: diffuse alpha controls reflection.
        return saturate(
            DiffuseTexture.Sample(
                DiffuseSampler,
                input.Uv).a);
    }

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
        ResolveSurfaceNormal(
            input);

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
            EnvMapStrength *
            mask);

    color.rgb =
        lerp(
            color.rgb,
            env,
            strength);

    return color;
}

float4 ResolveMaterialColor(
    float4 baseColor)
{
    if (MaterialChangeColorEnabled <= 0.0f ||
        MaterialChangeStrength <= 0.0f)
    {
        return baseColor;
    }

    return lerp(
        baseColor,
        MaterialChangeDiffuse,
        saturate(
            MaterialChangeStrength));
}

float3 ResolveMaterialEmissive()
{
    if (MaterialChangeColorEnabled <= 0.0f ||
        MaterialChangeStrength <= 0.0f)
    {
        return BaseEmissive.rgb;
    }

    return lerp(
        BaseEmissive.rgb,
        MaterialChangeEmissive.rgb,
        saturate(
            MaterialChangeStrength));
}

float4 SampleDiffuse(
    VertexOutput input)
{
    float4 materialColor =
        ResolveMaterialColor(
            input.Color);

    float4 sampled =
        DiffuseTexture.Sample(
            DiffuseSampler,
            input.Uv) *
        materialColor;

    sampled.rgb +=
        ResolveMaterialEmissive();

    if (LightMapStrength > 0.0f)
    {
        sampled.rgb +=
            LightMapTexture.Sample(
                DiffuseSampler,
                input.Uv).rgb *
            LightMapStrength;
    }

    if (MaterialChangeTextureEnabled > 0.0f &&
        MaterialChangeStrength > 0.0f)
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
    return saturate(
        TransMapTexture.Sample(
            DiffuseSampler,
            input.Uv).a);
}

float4 PSColor(
    VertexOutput input) : SV_TARGET
{
    float4 color =
        ResolveMaterialColor(
            input.Color);

    color.rgb +=
        ResolveMaterialEmissive();

    if (LightMapStrength > 0.0f)
    {
        color.rgb +=
            LightMapTexture.Sample(
                DiffuseSampler,
                input.Uv).rgb *
            LightMapStrength;
    }

    if (MaterialChangeTextureEnabled > 0.0f &&
        MaterialChangeStrength > 0.0f)
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

float4 PSLightEffect(
    VertexOutput input) : SV_TARGET
{
    float2 centered =
        input.Uv * 2.0f - 1.0f;

    float radiusSquared =
        dot(
            centered,
            centered);

    clip(
        1.0f -
        radiusSquared);

    float falloff =
        saturate(
            1.0f -
            radiusSquared);

    falloff *=
        falloff;

    return float4(
        MaterialChangeDiffuse.rgb *
            falloff,
        falloff);
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
