namespace OmsiCompat.Models;

internal sealed class OmsiO3dProtectedVertexDecoder
{
    private const uint SeedModulo = 8_000;
    private const uint ProductModulo = 65_000;

    private readonly uint _protectionKey;
    private readonly bool _usesAlternativeSeed;
    private readonly bool _hasProductIdentifier;
    private readonly ushort _vertexSeedCount;

    private ushort _rollingSeed;
    private byte _previousFractionSeed;

    private OmsiO3dProtectedVertexDecoder(
        byte version,
        uint protectionKey,
        bool usesAlternativeSeed,
        uint vertexCount)
    {
        _protectionKey = protectionKey;
        _usesAlternativeSeed = usesAlternativeSeed;
        _hasProductIdentifier =
            protectionKey != 0x0000FFFFu;
        _vertexSeedCount =
            checked((ushort)vertexCount);

        if (!_hasProductIdentifier)
        {
            _rollingSeed = 0;
            return;
        }

        var versionOffset =
            version > 3
                ? (uint)(version - 4)
                : 0u;

        var initial =
            unchecked(
                (ushort)(
                    protectionKey +
                    versionOffset));

        _rollingSeed =
            checked(
                (ushort)(
                    (
                        initial +
                        (usesAlternativeSeed
                            ? 381u
                            : 0u)
                    ) %
                    ProductModulo));
    }

    public static bool TryCreate(
        byte version,
        uint protectionKey,
        bool usesAlternativeSeed,
        uint vertexCount,
        out OmsiO3dProtectedVertexDecoder? decoder)
    {
        decoder = null;

        if (vertexCount == 0)
        {
            return true;
        }

        if (vertexCount >= ProductModulo)
        {
            return false;
        }

        decoder =
            new OmsiO3dProtectedVertexDecoder(
                version,
                protectionKey,
                usesAlternativeSeed,
                vertexCount);

        return true;
    }

    public void Decode(
        ref float x,
        ref float y,
        ref float z,
        ref float normalX,
        ref float normalY,
        ref float normalZ,
        ref float u,
        ref float v)
    {
        if (!_hasProductIdentifier)
        {
            return;
        }

        if (_protectionKey == 0)
        {
            _rollingSeed =
                _usesAlternativeSeed
                    ? (ushort)304
                    : (ushort)0;
        }

        var mixed =
            (
                (uint)_previousFractionSeed *
                _vertexSeedCount +
                (uint)_vertexSeedCount *
                _rollingSeed
            ) %
            SeedModulo;

        _rollingSeed =
            checked((ushort)mixed);

        var fractionalX =
            x - MathF.Truncate(x);
        var fractionalY =
            y - MathF.Truncate(y);
        var fractionalZ =
            z - MathF.Truncate(z);

        var nextFractionSeed =
            (int)(
                MathF.Abs(
                    fractionalX *
                    fractionalY *
                    fractionalZ) *
                600f);

        _previousFractionSeed =
            unchecked((byte)nextFractionSeed);

        if (_rollingSeed < 1_000)
        {
            Swap(ref x, ref y);
        }
        else if (_rollingSeed < 3_000)
        {
            Swap(ref x, ref z);
        }
        else if (_rollingSeed > 7_000)
        {
            Swap(ref y, ref z);
        }

        if (_rollingSeed % 4 == 0)
        {
            normalX = -normalX;
        }

        if (_rollingSeed % 6 == 0)
        {
            normalY = -normalY;
        }

        if (_rollingSeed % 7 == 0)
        {
            normalZ = -normalZ;
        }

        if (_rollingSeed < 600)
        {
            Swap(
                ref normalY,
                ref normalZ);
        }
        else if (_rollingSeed > 4_500)
        {
            Swap(
                ref normalX,
                ref normalY);
        }

        if (_rollingSeed % 5 == 0)
        {
            var delta =
                _rollingSeed % 100;

            u -=
                delta * delta /
                10_000f;
        }

        if (_rollingSeed % 3 == 0)
        {
            var delta =
                _rollingSeed % 50;

            v -=
                delta * delta /
                2_500f;
        }
    }

    private static void Swap(
        ref float left,
        ref float right)
    {
        (left, right) =
            (right, left);
    }
}
