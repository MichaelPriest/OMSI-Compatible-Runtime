namespace OmsiCompat.Models;

public sealed record OmsiO3dMaterial(
    float DiffuseR,
    float DiffuseG,
    float DiffuseB,
    float DiffuseA,
    float SpecularR,
    float SpecularG,
    float SpecularB,
    float EmissionR,
    float EmissionG,
    float EmissionB,
    float SpecularPower,
    string? TextureName);
