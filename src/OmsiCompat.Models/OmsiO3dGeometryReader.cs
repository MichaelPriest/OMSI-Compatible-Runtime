using System.Numerics;
using System.Text;

namespace OmsiCompat.Models;

public static class OmsiO3dGeometryReader
{
    private const byte VertexSection = 0x17;
    private const byte TriangleSection = 0x49;
    private const byte MaterialSection = 0x26;
    private const byte BoneSection = 0x54;
    private const byte TransformSection = 0x79;

    // Detailed modern OMSI vehicle meshes can be substantially larger than
    // scenery meshes. Keep a bounded parser, but align the geometry ceiling
    // with mature O3D tooling instead of rejecting valid bus bodies early.
    private const uint MaxVertices = 1_000_000;
    private const uint MaxTriangles = 1_000_000;
    private const uint MaxBones = 1_000_000;
    private const ushort MaxMaterials = 10_000;

    public static OmsiO3dGeometry ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return OmsiO3dGeometry.Error("missingFile");
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            using var reader = new BinaryReader(stream);

            if (!HasRemaining(stream, 3))
            {
                return OmsiO3dGeometry.Error("truncatedHeader");
            }

            if (reader.ReadByte() != 0x84 ||
                reader.ReadByte() != 0x19)
            {
                return OmsiO3dGeometry.Error("invalidSignature");
            }

            var version = reader.ReadByte();
            var longHeader = version > 3;
            var longTriangleIndices = false;
            var extendedOptions = (byte)0;
            var protectionKey = uint.MaxValue;

            if (longHeader)
            {
                if (!HasRemaining(stream, 5))
                {
                    return OmsiO3dGeometry.Error(
                        "truncatedExtendedHeader");
                }

                extendedOptions = reader.ReadByte();
                protectionKey = reader.ReadUInt32();

                longTriangleIndices =
                    (extendedOptions & 0x01) != 0;
            }

            var encryptedVertices =
                longHeader &&
                protectionKey != uint.MaxValue;

            float[]? positions = null;
            float[]? normals = null;
            float[]? uvs = null;
            uint[]? indices = null;
            ushort[]? triangleMaterialIndices = null;
            IReadOnlyList<OmsiO3dMaterial> materials =
                Array.Empty<OmsiO3dMaterial>();

            var sourceTransform =
                Matrix4x4.Identity;

            uint vertexCount = 0;

            while (stream.Position < stream.Length)
            {
                var section = reader.ReadByte();

                switch (section)
                {
                    case VertexSection:
                        if (!TryReadCount(
                                reader,
                                longHeader,
                                out vertexCount) ||
                            vertexCount > MaxVertices)
                        {
                            return OmsiO3dGeometry.Error(
                                "invalidVertexSection");
                        }

                        positions =
                            new float[checked((int)vertexCount * 3)];
                        normals =
                            new float[checked((int)vertexCount * 3)];
                        uvs =
                            new float[checked((int)vertexCount * 2)];

                        var vertexDecoder =
                            encryptedVertices
                                ? new EncryptedVertexDecoder(
                                    protectionKey,
                                    version,
                                    extendedOptions,
                                    vertexCount)
                                : null;

                        for (var index = 0U;
                             index < vertexCount;
                             index++)
                        {
                            if (!HasRemaining(stream, 32))
                            {
                                return OmsiO3dGeometry.Error(
                                    "invalidVertexSection");
                            }

                            var x = reader.ReadSingle();
                            var y = reader.ReadSingle();
                            var z = reader.ReadSingle();

                            var normalX = reader.ReadSingle();
                            var normalY = reader.ReadSingle();
                            var normalZ = reader.ReadSingle();

                            var u = reader.ReadSingle();
                            var v = reader.ReadSingle();

                            vertexDecoder?.Decode(
                                ref x,
                                ref y,
                                ref z,
                                ref normalX,
                                ref normalY,
                                ref normalZ,
                                ref u,
                                ref v);

                            var p = checked((int)index * 3);
                            positions[p] = x;
                            positions[p + 1] = y;
                            positions[p + 2] = z;

                            normals[p] = normalX;
                            normals[p + 1] = normalY;
                            normals[p + 2] = normalZ;

                            var t = checked((int)index * 2);
                            uvs[t] = u;
                            uvs[t + 1] = v;
                        }

                        break;

                    case TriangleSection:
                        if (!TryReadCount(
                                reader,
                                longHeader,
                                out var triangleCount) ||
                            triangleCount > MaxTriangles)
                        {
                            return OmsiO3dGeometry.Error(
                                "invalidTriangleSection");
                        }

                        indices =
                            new uint[checked((int)triangleCount * 3)];

                        triangleMaterialIndices =
                            new ushort[checked((int)triangleCount)];

                        for (var index = 0U;
                             index < triangleCount;
                             index++)
                        {
                            if (!HasRemaining(
                                    stream,
                                    longTriangleIndices
                                        ? 14
                                        : 8))
                            {
                                return OmsiO3dGeometry.Error(
                                    "invalidTriangleSection");
                            }

                            uint a;
                            uint b;
                            uint c;

                            if (longTriangleIndices)
                            {
                                a = reader.ReadUInt32();
                                b = reader.ReadUInt32();
                                c = reader.ReadUInt32();
                            }
                            else
                            {
                                a = reader.ReadUInt16();
                                b = reader.ReadUInt16();
                                c = reader.ReadUInt16();
                            }

                            var materialIndex =
                                reader.ReadUInt16();

                            var t = checked((int)index * 3);
                            indices[t] = a;
                            indices[t + 1] = b;
                            indices[t + 2] = c;

                            triangleMaterialIndices[
                                checked((int)index)] =
                                materialIndex;
                        }

                        break;

                    case MaterialSection:
                        if (!TryReadMaterials(
                                reader,
                                stream,
                                out materials))
                        {
                            return OmsiO3dGeometry.Error(
                                "invalidMaterialSection");
                        }

                        break;

                    case BoneSection:
                        if (!SkipBones(
                                reader,
                                stream,
                                longTriangleIndices))
                        {
                            return OmsiO3dGeometry.Error(
                                "invalidBoneSection");
                        }

                        break;

                    case TransformSection:
                        if (!HasRemaining(
                                stream,
                                64))
                        {
                            return OmsiO3dGeometry.Error(
                                "invalidTransformSection");
                        }

                        sourceTransform =
                            new Matrix4x4(
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle(),
                                reader.ReadSingle());

                        break;

                    default:
                        return OmsiO3dGeometry.Error(
                            $"unexpectedSection_{section:X2}");
                }
            }

            if (positions is null ||
                normals is null ||
                uvs is null ||
                indices is null ||
                triangleMaterialIndices is null)
            {
                return OmsiO3dGeometry.Error(
                    "noRenderableGeometry");
            }

            foreach (var index in indices)
            {
                if (index >= vertexCount)
                {
                    return OmsiO3dGeometry.Error(
                        "triangleIndexOutOfRange");
                }
            }

            return new OmsiO3dGeometry(
                true,
                null,
                positions,
                normals,
                uvs,
                indices,
                triangleMaterialIndices,
                materials,
                sourceTransform);
        }
        catch (EndOfStreamException)
        {
            return OmsiO3dGeometry.Error(
                "unexpectedEndOfFile");
        }
        catch (OverflowException)
        {
            return OmsiO3dGeometry.Error(
                "geometryTooLarge");
        }
        catch (IOException)
        {
            return OmsiO3dGeometry.Error(
                "ioError");
        }
        catch (UnauthorizedAccessException)
        {
            return OmsiO3dGeometry.Error(
                "accessDenied");
        }
    }

    private sealed class EncryptedVertexDecoder
    {
        private readonly uint _productId;
        private readonly bool _hasProductId;
        private readonly bool _alternateSeed;
        private readonly ushort _vertexCountSalt;

        private ushort _productSalt;
        private byte _salt;

        public EncryptedVertexDecoder(
            uint productId,
            byte version,
            byte options,
            uint vertexCount)
        {
            _hasProductId =
                productId != 0x0000FFFFu;

            _productId =
                _hasProductId
                    ? productId
                    : 0u;

            _alternateSeed =
                (options & 0x02) != 0;

            _vertexCountSalt =
                unchecked(
                    (ushort)(
                        vertexCount %
                        65000u));

            if (!_hasProductId)
            {
                _productSalt = 0;
                return;
            }

            var initial =
                unchecked(
                    (ushort)(
                        productId +
                        version -
                        4u));

            _productSalt =
                unchecked(
                    (ushort)(
                        (initial +
                         (_alternateSeed
                             ? 381u
                             : 0u)) %
                        65000u));
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
            if (!_hasProductId)
            {
                return;
            }

            if (_productId == 0)
            {
                _productSalt =
                    (ushort)(
                        _alternateSeed
                            ? 304
                            : 0);
            }

            MixSalt();

            var fractionalX =
                x - MathF.Truncate(x);
            var fractionalY =
                y - MathF.Truncate(y);
            var fractionalZ =
                z - MathF.Truncate(z);

            var nextSalt =
                (int)(
                    MathF.Abs(
                        fractionalX *
                        fractionalY *
                        fractionalZ) *
                    600.0f);

            _salt =
                unchecked(
                    (byte)(
                        nextSalt &
                        0xFF));

            if (_productSalt >= 1000)
            {
                if (_productSalt >= 3000)
                {
                    if (_productSalt > 7000)
                    {
                        (y, z) =
                            (z, y);
                    }
                }
                else
                {
                    (x, z) =
                        (z, x);
                }
            }
            else
            {
                (x, y) =
                    (y, x);
            }

            if ((_productSalt & 3) == 0)
            {
                normalX =
                    -normalX;
            }

            if (_productSalt % 6 == 0)
            {
                normalY =
                    -normalY;
            }

            if (_productSalt % 7 == 0)
            {
                normalZ =
                    -normalZ;
            }

            if (_productSalt >= 600)
            {
                if (_productSalt > 4500)
                {
                    (normalX, normalY) =
                        (normalY, normalX);
                }
            }
            else
            {
                (normalY, normalZ) =
                    (normalZ, normalY);
            }

            if (_productSalt % 5 == 0)
            {
                var uvSalt =
                    _productSalt %
                    100u;

                u -=
                    uvSalt *
                    uvSalt /
                    10000.0f;
            }

            if (_productSalt % 3 == 0)
            {
                var uvSalt =
                    _productSalt %
                    50u;

                v -=
                    uvSalt *
                    uvSalt /
                    2500.0f;
            }
        }

        private void MixSalt()
        {
            var mixed =
                ((int)_salt *
                 _vertexCountSalt +
                 _vertexCountSalt *
                 _productSalt) %
                8000;

            _productSalt =
                unchecked(
                    (ushort)mixed);

            _salt =
                unchecked(
                    (byte)(
                        mixed /
                        8000));
        }
    }

    private static bool TryReadMaterials(
        BinaryReader reader,
        Stream stream,
        out IReadOnlyList<OmsiO3dMaterial> materials)
    {
        materials = Array.Empty<OmsiO3dMaterial>();

        if (!HasRemaining(stream, 2))
        {
            return false;
        }

        var count = reader.ReadUInt16();

        if (count > MaxMaterials)
        {
            return false;
        }

        var result =
            new List<OmsiO3dMaterial>(count);

        for (var index = 0;
             index < count;
             index++)
        {
            if (!HasRemaining(stream, 45))
            {
                return false;
            }

            var diffuseR = reader.ReadSingle();
            var diffuseG = reader.ReadSingle();
            var diffuseB = reader.ReadSingle();
            var diffuseA = reader.ReadSingle();

            var specularR = reader.ReadSingle();
            var specularG = reader.ReadSingle();
            var specularB = reader.ReadSingle();

            var emissionR = reader.ReadSingle();
            var emissionG = reader.ReadSingle();
            var emissionB = reader.ReadSingle();

            var specularPower = reader.ReadSingle();
            var textureLength = reader.ReadByte();

            if (!HasRemaining(
                    stream,
                    textureLength))
            {
                return false;
            }

            var textureName =
                textureLength == 0
                    ? null
                    : Encoding.Latin1.GetString(
                        reader.ReadBytes(
                            textureLength));

            result.Add(
                new OmsiO3dMaterial(
                    diffuseR,
                    diffuseG,
                    diffuseB,
                    diffuseA,
                    specularR,
                    specularG,
                    specularB,
                    emissionR,
                    emissionG,
                    emissionB,
                    specularPower,
                    string.IsNullOrWhiteSpace(
                        textureName)
                        ? null
                        : textureName));
        }

        materials = result;
        return true;
    }

    private static bool SkipBones(
        BinaryReader reader,
        Stream stream,
        bool longTriangleIndices)
    {
        if (!HasRemaining(
                stream,
                2))
        {
            return false;
        }

        var boneCount =
            (uint)reader.ReadUInt16();

        if (boneCount > MaxBones)
        {
            return false;
        }

        for (var index = 0U;
             index < boneCount;
             index++)
        {
            if (!HasRemaining(stream, 1))
            {
                return false;
            }

            var nameLength = reader.ReadByte();

            if (!TrySkip(stream, nameLength) ||
                !HasRemaining(stream, 2))
            {
                return false;
            }

            var weightCount = reader.ReadUInt16();
            var weightSize =
                longTriangleIndices
                    ? 8L
                    : 6L;

            if (!TrySkip(
                    stream,
                    checked(
                        (long)weightCount *
                        weightSize)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadCount(
        BinaryReader reader,
        bool longHeader,
        out uint count)
    {
        count = 0;
        var bytes =
            longHeader
                ? 4
                : 2;

        if (!HasRemaining(
                reader.BaseStream,
                bytes))
        {
            return false;
        }

        count =
            longHeader
                ? reader.ReadUInt32()
                : reader.ReadUInt16();

        return true;
    }

    private static bool TrySkip(
        Stream stream,
        long bytes)
    {
        if (!HasRemaining(
                stream,
                bytes))
        {
            return false;
        }

        stream.Seek(
            bytes,
            SeekOrigin.Current);

        return true;
    }

    private static bool HasRemaining(
        Stream stream,
        long bytes) =>
        bytes >= 0 &&
        stream.Position <=
        stream.Length - bytes;
}
