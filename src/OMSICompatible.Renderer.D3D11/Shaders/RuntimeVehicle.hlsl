cbuffer RuntimeCamera : register(b0)
{
    row_major float4x4 ViewProjection;
};

cbuffer RuntimeModel : register(b1)
{
    row_major float4x4 World;
};

struct VertexInput
{
    float3 Position : POSITION;
    float4 Color : COLOR;
};

struct VertexOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR;
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    float4 worldPosition =
        mul(float4(input.Position, 1.0f), World);
    output.Position =
        mul(worldPosition, ViewProjection);
    output.Color = input.Color;
    return output;
}

float4 PSMain(VertexOutput input) : SV_TARGET
{
    return input.Color;
}
