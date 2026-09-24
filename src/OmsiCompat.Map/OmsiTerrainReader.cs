using System.Buffers.Binary;

namespace OmsiCompat.Map;

public static class OmsiTerrainReader
{
    private const int MaximumCellCount = 512;

    public static OmsiTerrainGrid ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Read(File.ReadAllBytes(path));
    }

    public static OmsiTerrainGrid Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8)
        {
            throw new InvalidDataException("Terrain data is too short.");
        }

        var cellCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[..4]);

        if (cellCount <= 0 || cellCount > MaximumCellCount)
        {
            throw new InvalidDataException("Terrain cell count is invalid.");
        }

        var sampleCount = cellCount + 1;
        var heightCount = checked(sampleCount * sampleCount);
        var expectedLength = checked(4 + heightCount * sizeof(float));

        if (bytes.Length != expectedLength)
        {
            throw new InvalidDataException(
                "Terrain byte length does not match its grid size.");
        }

        var heights = new float[heightCount];

        for (var index = 0; index < heightCount; index++)
        {
            var offset = 4 + index * sizeof(float);
            var bits = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.Slice(offset, sizeof(float)));
            var height = BitConverter.Int32BitsToSingle(bits);

            if (!float.IsFinite(height))
            {
                throw new InvalidDataException(
                    "Terrain contains a non-finite height.");
            }

            heights[index] = height;
        }

        return new OmsiTerrainGrid(cellCount, heights);
    }
}
