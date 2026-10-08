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

        byte? detectedVersion = null;
        byte detectedOptions = 0;
        uint detectedProtectionKey = uint.MaxValue;
        uint detectedVertexCount = 0;
        var detectedProtectedVertices = false;

        OmsiO3dGeometry Fail(string errorCode)
        {
            WriteDiagnostics(
                path,
                errorCode,
                detectedVersion,
                detectedOptions,
                detectedProtectionKey,
                detectedVertexCount,
                detectedProtectedVertices);

            return OmsiO3dGeometry.Error(
                errorCode);
        }

        if (!File.Exists(path))
        {
            return Fail("missingFile");
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
                return Fail("truncatedHeader");
            }

            if (reader.ReadByte() != 0x84 ||
                reader.ReadByte() != 0x19)
            {
                return Fail("invalidSignature");
            }

            var version = reader.ReadByte();
            detectedVersion = version;
            // openOMSI / original OMSI: version 3 already adds a flag
            // byte and 32-bit section counts. Version 4 additionally
            // adds the protection key. Treating v3 as v1/v2 made valid
            // stock and add-on meshes fail with bogus section tags.
            var wideCounts = version >= 3;
            var hasProtectionKey = version >= 4;
            var longTriangleIndices = false;
            var extendedOptions = (byte)0;
            var protectionKey = uint.MaxValue;

            if (wideCounts)
            {
                if (!HasRemaining(stream, 1))
                {
                    return Fail(
                        "truncatedExtendedHeader");
                }

                extendedOptions = reader.ReadByte();
                detectedOptions = extendedOptions;

                longTriangleIndices =
                    (extendedOptions & 0x01) != 0;
            }

            if (hasProtectionKey)
            {
                if (!HasRemaining(stream, 4))
                {
                    return Fail(
                        "truncatedExtendedHeader");
                }

                protectionKey = reader.ReadUInt32();
                detectedProtectionKey = protectionKey;
            }

            var encryptedVertices =
                hasProtectionKey &&
                protectionKey != uint.MaxValue;

            detectedProtectedVertices =
                encryptedVertices;

            float[]? positions = null;
            float[]? normals = null;
            float[]? uvs = null;
            uint[]? indices = null;
            ushort[]? triangleMaterialIndices = null;
            IReadOnlyList<OmsiO3dMaterial> materials =
                Array.Empty<OmsiO3dMaterial>();
            IReadOnlyList<OmsiO3dBone> bones =
                Array.Empty<OmsiO3dBone>();

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
                                wideCounts,
                                out vertexCount) ||
                            vertexCount > MaxVertices)
                        {
                            return Fail(
                                "invalidVertexSection");
                        }

                        detectedVertexCount =
                            vertexCount;

                        positions =
                            new float[checked((int)vertexCount * 3)];
                        normals =
                            new float[checked((int)vertexCount * 3)];
                        uvs =
                            new float[checked((int)vertexCount * 2)];

                        OmsiO3dProtectedVertexDecoder?
                            vertexDecoder = null;

                        if (encryptedVertices &&
                            !OmsiO3dProtectedVertexDecoder.TryCreate(
                                version,
                                protectionKey,
                                (extendedOptions & 0x02) != 0,
                                vertexCount,
                                out vertexDecoder))
                        {
                            return Fail(
                                "protectedVertexCountUnsupported");
                        }

                        for (var index = 0U;
                             index < vertexCount;
                             index++)
                        {
                            if (!HasRemaining(stream, 32))
                            {
                                return Fail(
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
                                wideCounts,
                                out var triangleCount) ||
                            triangleCount > MaxTriangles)
                        {
                            return Fail(
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
                                return Fail(
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
                            return Fail(
                                "invalidMaterialSection");
                        }

                        break;

                    case BoneSection:
                        if (!TryReadBones(
                                reader,
                                stream,
                                longTriangleIndices,
                                out bones))
                        {
                            return Fail(
                                "invalidBoneSection");
                        }

                        break;

                    case TransformSection:
                        if (!HasRemaining(
                                stream,
                                64))
                        {
                            return Fail(
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
                        // The original OMSI O3D loader and openOMSI skip
                        // unrecognised tag bytes, including the padding
                        // present inside protected v7 transforms. A strict
                        // error here rejected entire otherwise valid buses.
                        break;
                }
            }

            if (positions is null ||
                normals is null ||
                uvs is null ||
                indices is null ||
                triangleMaterialIndices is null)
            {
                return Fail(
                    "noRenderableGeometry");
            }

            // Follow openOMSI's drop_bad_triangles: a damaged triangle
            // must not cause all the correctly indexed body/panel meshes
            // from the same add-on file to disappear.
            var validTriangleCount = 0;
            for (var triangle = 0;
                 triangle < triangleMaterialIndices.Length;
                 triangle++)
            {
                var start = triangle * 3;
                if (indices[start] < vertexCount &&
                    indices[start + 1] < vertexCount &&
                    indices[start + 2] < vertexCount)
                {
                    validTriangleCount++;
                }
            }

            if (validTriangleCount != triangleMaterialIndices.Length)
            {
                Console.WriteLine(
                    $"[o3d] {path}: dropped {triangleMaterialIndices.Length - validTriangleCount} invalid triangles.");

                var validIndices =
                    new uint[validTriangleCount * 3];
                var validMaterials =
                    new ushort[validTriangleCount];
                var destination = 0;

                for (var triangle = 0;
                     triangle < triangleMaterialIndices.Length;
                     triangle++)
                {
                    var start = triangle * 3;
                    if (indices[start] >= vertexCount ||
                        indices[start + 1] >= vertexCount ||
                        indices[start + 2] >= vertexCount)
                    {
                        continue;
                    }

                    var destStart = destination * 3;
                    validIndices[destStart] = indices[start];
                    validIndices[destStart + 1] = indices[start + 1];
                    validIndices[destStart + 2] = indices[start + 2];
                    validMaterials[destination] =
                        triangleMaterialIndices[triangle];
                    destination++;
                }

                indices = validIndices;
                triangleMaterialIndices = validMaterials;
            }

            if (validTriangleCount == 0)
            {
                return Fail("noRenderableGeometry");
            }

            if (encryptedVertices)
            {
                WriteDiagnostics(
                    path,
                    "loaded",
                    detectedVersion,
                    detectedOptions,
                    detectedProtectionKey,
                    detectedVertexCount,
                    true);
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
                sourceTransform,
                bones);
        }
        catch (EndOfStreamException)
        {
            return Fail(
                "unexpectedEndOfFile");
        }
        catch (OverflowException)
        {
            return Fail(
                "geometryTooLarge");
        }
        catch (IOException)
        {
            return Fail(
                "ioError");
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(
                "accessDenied");
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

    private static bool TryReadBones(
        BinaryReader reader,
        Stream stream,
        bool longVertexIndices,
        out IReadOnlyList<OmsiO3dBone> bones)
    {
        bones =
            Array.Empty<OmsiO3dBone>();

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

        var result =
            new List<OmsiO3dBone>(
                checked((int)boneCount));

        for (var index = 0U;
             index < boneCount;
             index++)
        {
            if (!HasRemaining(stream, 1))
            {
                return false;
            }

            var nameLength =
                reader.ReadByte();

            if (!HasRemaining(
                    stream,
                    nameLength))
            {
                return false;
            }

            var name =
                nameLength == 0
                    ? string.Empty
                    : Encoding.Latin1.GetString(
                        reader.ReadBytes(
                            nameLength));

            if (!HasRemaining(stream, 2))
            {
                return false;
            }

            var weightCount =
                reader.ReadUInt16();

            var weights =
                new List<OmsiO3dBoneWeight>(
                    weightCount);

            for (var weightIndex = 0;
                 weightIndex < weightCount;
                 weightIndex++)
            {
                var indexBytes =
                    longVertexIndices
                        ? 4
                        : 2;

                if (!HasRemaining(
                        stream,
                        indexBytes + 4))
                {
                    return false;
                }

                var vertexIndex =
                    longVertexIndices
                        ? checked((int)reader.ReadUInt32())
                        : reader.ReadUInt16();

                var weight =
                    reader.ReadSingle();

                if (!float.IsFinite(weight) ||
                    weight <= 0.0f)
                {
                    continue;
                }

                weights.Add(
                    new OmsiO3dBoneWeight(
                        vertexIndex,
                        weight));
            }

            result.Add(
                new OmsiO3dBone(
                    name,
                    weights));
        }

        bones =
            result;
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

    private static void WriteDiagnostics(
        string path,
        string result,
        byte? version,
        byte options,
        uint protectionKey,
        uint vertexCount,
        bool protectedVertices)
    {
        if (!protectedVertices &&
            string.Equals(
                result,
                "loaded",
                StringComparison.Ordinal))
        {
            return;
        }

        var productId =
            protectionKey == uint.MaxValue
                ? "none"
                : $"0x{protectionKey:X8}";

        Console.WriteLine(
            $"[o3d] file={path}; version={(version.HasValue ? version.Value.ToString() : "unknown")}; options=0x{options:X2}; productId={productId}; protected={protectedVertices}; vertices={vertexCount}; result={result}");
    }

    private static bool HasRemaining(
        Stream stream,
        long bytes) =>
        bytes >= 0 &&
        stream.Position <=
        stream.Length - bytes;
}
