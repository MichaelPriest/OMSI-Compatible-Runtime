Texture2D SceneTexture : register(t0);
SamplerState SceneSampler : register(s0);

cbuffer PostProcessConstants : register(b0)
{
    float2 TexelSize;
    float SharpenStrength;
    float Padding;
};

struct VSOut
{
    float4 Position : SV_POSITION;
    float2 Uv : TEXCOORD0;
};

VSOut VSMain(uint vertexId : SV_VertexID)
{
    VSOut output;

    float2 position =
        vertexId == 0
            ? float2(-1.0, -1.0)
            : vertexId == 1
                ? float2(-1.0, 3.0)
                : float2(3.0, -1.0);

    output.Position = float4(position, 0.0, 1.0);
    output.Uv = float2(
        position.x * 0.5 + 0.5,
        0.5 - position.y * 0.5);

    return output;
}

float4 PSMain(VSOut input) : SV_TARGET
{
    float4 center = SceneTexture.Sample(SceneSampler, input.Uv);

    if (SharpenStrength <= 0.0001)
    {
        return center;
    }

    float4 left = SceneTexture.Sample(SceneSampler, input.Uv + float2(-TexelSize.x, 0.0));
    float4 right = SceneTexture.Sample(SceneSampler, input.Uv + float2(TexelSize.x, 0.0));
    float4 up = SceneTexture.Sample(SceneSampler, input.Uv + float2(0.0, -TexelSize.y));
    float4 down = SceneTexture.Sample(SceneSampler, input.Uv + float2(0.0, TexelSize.y));

    float4 sharpened =
        center * (1.0 + 4.0 * SharpenStrength) -
        (left + right + up + down) * SharpenStrength;

    sharpened.a = center.a;

    return saturate(sharpened);
}
