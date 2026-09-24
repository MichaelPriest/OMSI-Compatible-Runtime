namespace OmsiCompat.Map;

public sealed record OmsiGroundTexture(
    string MainTexturePath,
    string DetailTexturePath,
    int ResolutionCode,
    double MainTextureRepeating,
    double DetailTextureRepeating);
