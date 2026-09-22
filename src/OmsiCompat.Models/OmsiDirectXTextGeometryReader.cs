using System.Globalization;
using System.Numerics;
using System.Text;

namespace OmsiCompat.Models;

/// <summary>
/// Reads the legacy DirectX .x text format used by older OMSI assets.
/// Binary and compressed .x encodings are intentionally rejected.
/// </summary>
public sealed class OmsiDirectXTextGeometryReader
{
    private const long MaxFileBytes =
        32L * 1024L * 1024L;

    private const int MaxVertices =
        150_000;

    private const int MaxFaces =
        300_000;

    private const int MaxTriangles =
        300_000;

    private const int MaxMaterials =
        4_096;

    private const int MaxFaceVertices =
        4_096;

    public OmsiO3dGeometry Read(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path);

        if (!File.Exists(path))
        {
            return OmsiO3dGeometry.Error(
                "missingFile");
        }

        try
        {
            var fileInfo =
                new FileInfo(path);

            if (fileInfo.Length >
                MaxFileBytes)
            {
                return OmsiO3dGeometry.Error(
                    "legacyDirectXTooLarge");
            }

            var text =
                File.ReadAllText(
                    path,
                    Encoding.Latin1)
                    .TrimStart(
                        '\uFEFF',
                        ' ',
                        '\t',
                        '\r',
                        '\n');

            if (
                text.Length < 16 ||
                !text.StartsWith(
                    "xof ",
                    StringComparison.OrdinalIgnoreCase))
            {
                return OmsiO3dGeometry.Error(
                    "legacyDirectXInvalidHeader");
            }

            var encoding =
                text.Substring(
                        8,
                        4)
                    .Trim();

            if (!string.Equals(
                    encoding,
                    "txt",
                    StringComparison.OrdinalIgnoreCase))
            {
                return OmsiO3dGeometry.Error(
                    "legacyDirectXUnsupportedEncoding");
            }

            var parser =
                new DirectXParser(
                    text[16..]);

            return parser.Parse();
        }
        catch (DirectXParseException exception)
        {
            return OmsiO3dGeometry.Error(
                exception.Code);
        }
        catch (OverflowException)
        {
            return OmsiO3dGeometry.Error(
                "legacyDirectXGeometryTooLarge");
        }
    }

    private sealed class DirectXParser
    {
        private readonly Tokenizer _tokens;

        private readonly Dictionary<
            string,
            OmsiO3dMaterial>
            _namedMaterials =
                new(
                    StringComparer
                        .OrdinalIgnoreCase);

        private readonly List<float>
            _positions = [];

        private readonly List<float>
            _normals = [];

        private readonly List<float>
            _uvs = [];

        private readonly List<uint>
            _indices = [];

        private readonly List<ushort>
            _triangleMaterialIndices =
                [];

        private readonly List<
            OmsiO3dMaterial>
            _materials = [];

        public DirectXParser(
            string text)
        {
            _tokens =
                new Tokenizer(text);
        }

        public OmsiO3dGeometry Parse()
        {
            while (true)
            {
                var token =
                    NextMeaningful();

                if (token is null)
                {
                    break;
                }

                if (EqualsToken(
                        token,
                        "template"))
                {
                    SkipObjectAfterKeyword();
                    continue;
                }

                if (EqualsToken(
                        token,
                        "Material"))
                {
                    _ = ParseMaterial();
                    continue;
                }

                if (EqualsToken(
                        token,
                        "Frame"))
                {
                    ParseFrame(
                        Matrix4x4.Identity);
                    continue;
                }

                if (EqualsToken(
                        token,
                        "Mesh"))
                {
                    ParseMesh(
                        Matrix4x4.Identity);
                    continue;
                }

                SkipObjectAfterKeyword();
            }

            if (
                _positions.Count == 0 ||
                _indices.Count == 0)
            {
                return OmsiO3dGeometry.Error(
                    "legacyDirectXNoRenderableGeometry");
            }

            return new OmsiO3dGeometry(
                IsLoaded: true,
                ErrorCode: null,
                Positions:
                    _positions.ToArray(),
                Normals:
                    _normals.ToArray(),
                Uvs:
                    _uvs.ToArray(),
                Indices:
                    _indices.ToArray(),
                TriangleMaterialIndices:
                    _triangleMaterialIndices
                        .ToArray(),
                Materials:
                    _materials.ToArray(),
                SourceTransform:
                    Matrix4x4.Identity);
        }

        private void ParseFrame(
            Matrix4x4 parentTransform)
        {
            _ = OpenBlock();

            var localTransform =
                Matrix4x4.Identity;

            while (true)
            {
                var token =
                    NextMeaningful();

                if (token is null)
                {
                    Throw(
                        "legacyDirectXInvalidFrame");
                }

                if (token == "}")
                {
                    return;
                }

                if (EqualsToken(
                        token,
                        "FrameTransformMatrix"))
                {
                    localTransform =
                        ParseFrameTransform();
                    continue;
                }

                var combinedTransform =
                    localTransform *
                    parentTransform;

                if (EqualsToken(
                        token,
                        "Mesh"))
                {
                    ParseMesh(
                        combinedTransform);
                    continue;
                }

                if (EqualsToken(
                        token,
                        "Frame"))
                {
                    ParseFrame(
                        combinedTransform);
                    continue;
                }

                if (EqualsToken(
                        token,
                        "Material"))
                {
                    _ = ParseMaterial();
                    continue;
                }

                SkipObjectAfterKeyword();
            }
        }

        private Matrix4x4
            ParseFrameTransform()
        {
            _ = OpenBlock();

            Span<float> values =
                stackalloc float[16];

            for (
                var index = 0;
                index < values.Length;
                index++)
            {
                values[index] =
                    ReadFloat(
                        "legacyDirectXInvalidTransform");
            }

            CloseCurrentBlock(
                "legacyDirectXInvalidTransform");

            return new Matrix4x4(
                values[0],
                values[1],
                values[2],
                values[3],
                values[4],
                values[5],
                values[6],
                values[7],
                values[8],
                values[9],
                values[10],
                values[11],
                values[12],
                values[13],
                values[14],
                values[15]);
        }

        private void ParseMesh(
            Matrix4x4 transform)
        {
            _ = OpenBlock();

            var vertexCount =
                ReadCount(
                    MaxVertices,
                    "legacyDirectXInvalidVertexCount",
                    "legacyDirectXTooManyVertices");

            if (
                _positions.Count / 3 +
                    vertexCount >
                MaxVertices)
            {
                Throw(
                    "legacyDirectXTooManyVertices");
            }

            var sourceVertices =
                new Vector3[vertexCount];

            for (
                var index = 0;
                index < vertexCount;
                index++)
            {
                sourceVertices[index] =
                    new Vector3(
                        ReadFloat(
                            "legacyDirectXInvalidVertex"),
                        ReadFloat(
                            "legacyDirectXInvalidVertex"),
                        ReadFloat(
                            "legacyDirectXInvalidVertex"));
            }

            var faceCount =
                ReadCount(
                    MaxFaces,
                    "legacyDirectXInvalidFaceCount",
                    "legacyDirectXTooManyFaces");

            var faces =
                new int[faceCount][];

            var triangleCount = 0;

            for (
                var faceIndex = 0;
                faceIndex < faceCount;
                faceIndex++)
            {
                var faceVertexCount =
                    ReadCount(
                        MaxFaceVertices,
                        "legacyDirectXInvalidFace",
                        "legacyDirectXFaceTooLarge");

                if (faceVertexCount < 3)
                {
                    Throw(
                        "legacyDirectXInvalidFace");
                }

                var face =
                    new int[faceVertexCount];

                for (
                    var vertexIndex = 0;
                    vertexIndex <
                        faceVertexCount;
                    vertexIndex++)
                {
                    var value =
                        ReadInt(
                            "legacyDirectXInvalidFace");

                    if (
                        value < 0 ||
                        value >= vertexCount)
                    {
                        Throw(
                            "legacyDirectXIndexOutOfRange");
                    }

                    face[vertexIndex] =
                        value;
                }

                triangleCount =
                    checked(
                        triangleCount +
                        faceVertexCount -
                        2);

                if (
                    triangleCount >
                    MaxTriangles ||
                    _indices.Count / 3 +
                        triangleCount >
                    MaxTriangles)
                {
                    Throw(
                        "legacyDirectXTooManyTriangles");
                }

                faces[faceIndex] =
                    face;
            }

            float[]? localUvs =
                null;

            int[]? faceMaterialIndices =
                null;

            List<OmsiO3dMaterial>?
                localMaterials =
                    null;

            while (true)
            {
                var token =
                    NextMeaningful();

                if (token is null)
                {
                    Throw(
                        "legacyDirectXInvalidMesh");
                }

                if (token == "}")
                {
                    break;
                }

                if (EqualsToken(
                        token,
                        "MeshTextureCoords"))
                {
                    localUvs =
                        ParseTextureCoordinates(
                            vertexCount);
                    continue;
                }

                if (EqualsToken(
                        token,
                        "MeshMaterialList"))
                {
                    (
                        faceMaterialIndices,
                        localMaterials
                    ) =
                        ParseMaterialList(
                            faceCount);
                    continue;
                }

                if (EqualsToken(
                        token,
                        "Material"))
                {
                    _ = ParseMaterial();
                    continue;
                }

                SkipObjectAfterKeyword();
            }

            localMaterials ??=
                [CreateDefaultMaterial()];

            if (localMaterials.Count == 0)
            {
                localMaterials.Add(
                    CreateDefaultMaterial());
            }

            if (
                _materials.Count +
                    localMaterials.Count >
                MaxMaterials)
            {
                Throw(
                    "legacyDirectXTooManyMaterials");
            }

            var materialOffset =
                _materials.Count;

            _materials.AddRange(
                localMaterials);

            faceMaterialIndices ??=
                new int[faceCount];

            if (
                faceMaterialIndices.Length !=
                faceCount)
            {
                Throw(
                    "legacyDirectXInvalidMaterialList");
            }

            var convertedVertices =
                new Vector3[vertexCount];

            for (
                var index = 0;
                index < vertexCount;
                index++)
            {
                var transformed =
                    Vector3.Transform(
                        sourceVertices[index],
                        transform);

                // Legacy OMSI DirectX .x geometry is already expressed
                // in the DirectX/Y-up model coordinate system used by
                // Babylon. Do not apply the map Z-up conversion here;
                // that conversion belongs only to placed map objects.
                convertedVertices[index] =
                    transformed;
            }

            var localIndices =
                new List<uint>(
                    checked(
                        triangleCount * 3));

            var localTriangleMaterials =
                new List<ushort>(
                    triangleCount);

            for (
                var faceIndex = 0;
                faceIndex < faces.Length;
                faceIndex++)
            {
                var materialIndex =
                    faceMaterialIndices[
                        faceIndex];

                if (
                    materialIndex < 0 ||
                    materialIndex >=
                        localMaterials.Count)
                {
                    Throw(
                        "legacyDirectXInvalidMaterialIndex");
                }

                var combinedMaterialIndex =
                    checked(
                        materialOffset +
                        materialIndex);

                if (
                    combinedMaterialIndex >
                    ushort.MaxValue)
                {
                    Throw(
                        "legacyDirectXTooManyMaterials");
                }

                var face =
                    faces[faceIndex];

                for (
                    var triangle = 1;
                    triangle <
                        face.Length - 1;
                    triangle++)
                {
                    // No axis swap is performed for legacy .x data,
                    // so preserve the source winding.
                    localIndices.Add(
                        checked(
                            (uint)face[0]));
                    localIndices.Add(
                        checked(
                            (uint)face[
                                triangle]));
                    localIndices.Add(
                        checked(
                            (uint)face[
                                triangle + 1]));

                    localTriangleMaterials.Add(
                        checked(
                            (ushort)
                                combinedMaterialIndex));
                }
            }

            var localNormals =
                BuildNormals(
                    convertedVertices,
                    localIndices);

            var vertexOffset =
                checked(
                    (uint)(
                        _positions.Count /
                        3));

            foreach (var vertex in
                convertedVertices)
            {
                _positions.Add(vertex.X);
                _positions.Add(vertex.Y);
                _positions.Add(vertex.Z);
            }

            _normals.AddRange(
                localNormals);

            if (localUvs is null)
            {
                for (
                    var index = 0;
                    index < vertexCount;
                    index++)
                {
                    _uvs.Add(0);
                    _uvs.Add(0);
                }
            }
            else
            {
                _uvs.AddRange(
                    localUvs);
            }

            foreach (var index in
                localIndices)
            {
                _indices.Add(
                    checked(
                        index +
                        vertexOffset));
            }

            _triangleMaterialIndices
                .AddRange(
                    localTriangleMaterials);
        }

        private float[]
            ParseTextureCoordinates(
                int vertexCount)
        {
            _ = OpenBlock();

            var count =
                ReadCount(
                    MaxVertices,
                    "legacyDirectXInvalidTextureCoordinates",
                    "legacyDirectXTooManyVertices");

            if (count != vertexCount)
            {
                Throw(
                    "legacyDirectXInvalidTextureCoordinates");
            }

            var result =
                new float[
                    checked(count * 2)];

            for (
                var index = 0;
                index < count;
                index++)
            {
                var offset =
                    checked(index * 2);

                result[offset] =
                    ReadFloat(
                        "legacyDirectXInvalidTextureCoordinates");

                result[offset + 1] =
                    1 -
                    ReadFloat(
                        "legacyDirectXInvalidTextureCoordinates");
            }

            CloseCurrentBlock(
                "legacyDirectXInvalidTextureCoordinates");

            return result;
        }

        private (
            int[] FaceMaterials,
            List<OmsiO3dMaterial>
                Materials)
            ParseMaterialList(
                int faceCount)
        {
            _ = OpenBlock();

            var declaredMaterialCount =
                ReadCount(
                    MaxMaterials,
                    "legacyDirectXInvalidMaterialList",
                    "legacyDirectXTooManyMaterials");

            var faceIndexCount =
                ReadCount(
                    MaxFaces,
                    "legacyDirectXInvalidMaterialList",
                    "legacyDirectXTooManyFaces");

            var rawFaceMaterials =
                new int[faceIndexCount];

            for (
                var index = 0;
                index < faceIndexCount;
                index++)
            {
                rawFaceMaterials[index] =
                    ReadInt(
                        "legacyDirectXInvalidMaterialList");
            }

            var materials =
                new List<OmsiO3dMaterial>(
                    declaredMaterialCount);

            while (true)
            {
                var token =
                    NextMeaningful();

                if (token is null)
                {
                    Throw(
                        "legacyDirectXInvalidMaterialList");
                }

                if (token == "}")
                {
                    break;
                }

                if (EqualsToken(
                        token,
                        "Material"))
                {
                    materials.Add(
                        ParseMaterial());
                    continue;
                }

                if (token == "{")
                {
                    var materialName =
                        NextMeaningful();

                    if (
                        materialName is not null &&
                        materialName != "}" &&
                        _namedMaterials.TryGetValue(
                            materialName,
                            out var material))
                    {
                        materials.Add(
                            material);
                    }
                    else
                    {
                        materials.Add(
                            CreateDefaultMaterial());
                    }

                    SkipUntilCurrentBlockEnd();
                    continue;
                }

                SkipObjectAfterKeyword();
            }

            while (
                materials.Count <
                declaredMaterialCount)
            {
                materials.Add(
                    CreateDefaultMaterial());
            }

            if (
                declaredMaterialCount > 0 &&
                materials.Count >
                    declaredMaterialCount)
            {
                materials.RemoveRange(
                    declaredMaterialCount,
                    materials.Count -
                        declaredMaterialCount);
            }

            if (materials.Count == 0)
            {
                materials.Add(
                    CreateDefaultMaterial());
            }

            var faceMaterials =
                new int[faceCount];

            if (faceIndexCount == 0)
            {
                return (
                    faceMaterials,
                    materials);
            }

            if (faceIndexCount == 1)
            {
                Array.Fill(
                    faceMaterials,
                    rawFaceMaterials[0]);

                return (
                    faceMaterials,
                    materials);
            }

            if (faceIndexCount != faceCount)
            {
                Throw(
                    "legacyDirectXInvalidMaterialList");
            }

            Array.Copy(
                rawFaceMaterials,
                faceMaterials,
                faceCount);

            return (
                faceMaterials,
                materials);
        }

        private OmsiO3dMaterial
            ParseMaterial()
        {
            var name =
                OpenBlock();

            var material =
                new MutableMaterial
                {
                    DiffuseR =
                        ReadFloat(
                            "legacyDirectXInvalidMaterial"),
                    DiffuseG =
                        ReadFloat(
                            "legacyDirectXInvalidMaterial"),
                    DiffuseB =
                        ReadFloat(
                            "legacyDirectXInvalidMaterial"),
                    DiffuseA =
                        ReadFloat(
                            "legacyDirectXInvalidMaterial"),
                    SpecularPower =
                        ReadFloat(
                            "legacyDirectXInvalidMaterial"),
                    SpecularR =
                        ReadFloat(
                            "legacyDirectXInvalidMaterial"),
                    SpecularG =
                        ReadFloat(
                            "legacyDirectXInvalidMaterial"),
                    SpecularB =
                        ReadFloat(
                            "legacyDirectXInvalidMaterial"),
                    EmissionR =
                        ReadFloat(
                            "legacyDirectXInvalidMaterial"),
                    EmissionG =
                        ReadFloat(
                            "legacyDirectXInvalidMaterial"),
                    EmissionB =
                        ReadFloat(
                            "legacyDirectXInvalidMaterial")
                };

            while (true)
            {
                var token =
                    NextMeaningful();

                if (token is null)
                {
                    Throw(
                        "legacyDirectXInvalidMaterial");
                }

                if (token == "}")
                {
                    break;
                }

                if (EqualsToken(
                        token,
                        "TextureFilename"))
                {
                    _ = OpenBlock();

                    var textureName =
                        NextMeaningful();

                    material.TextureName =
                        string.IsNullOrWhiteSpace(
                            textureName) ||
                        textureName == "}"
                            ? null
                            : textureName;

                    CloseCurrentBlock(
                        "legacyDirectXInvalidMaterial");
                    continue;
                }

                SkipObjectAfterKeyword();
            }

            var result =
                material.ToImmutable();

            if (!string.IsNullOrWhiteSpace(
                    name))
            {
                _namedMaterials[name] =
                    result;
            }

            return result;
        }

        private static float[]
            BuildNormals(
                IReadOnlyList<Vector3>
                    vertices,
                IReadOnlyList<uint>
                    indices)
        {
            var values =
                new Vector3[
                    vertices.Count];

            for (
                var index = 0;
                index + 2 <
                    indices.Count;
                index += 3)
            {
                var a =
                    checked(
                        (int)indices[index]);
                var b =
                    checked(
                        (int)indices[
                            index + 1]);
                var c =
                    checked(
                        (int)indices[
                            index + 2]);

                var normal =
                    Vector3.Cross(
                        vertices[b] -
                            vertices[a],
                        vertices[c] -
                            vertices[a]);

                if (
                    normal.LengthSquared() <
                    0.0000001f)
                {
                    continue;
                }

                values[a] += normal;
                values[b] += normal;
                values[c] += normal;
            }

            var result =
                new float[
                    checked(
                        vertices.Count *
                        3)];

            for (
                var index = 0;
                index < values.Length;
                index++)
            {
                var normal =
                    values[index];

                if (
                    normal.LengthSquared() >
                    0.0000001f)
                {
                    normal =
                        Vector3.Normalize(
                            normal);
                }
                else
                {
                    normal =
                        Vector3.UnitY;
                }

                var offset =
                    checked(index * 3);

                result[offset] =
                    normal.X;
                result[offset + 1] =
                    normal.Y;
                result[offset + 2] =
                    normal.Z;
            }

            return result;
        }

        private string? OpenBlock()
        {
            var token =
                NextMeaningful();

            if (token == "{")
            {
                return null;
            }

            if (
                token is null ||
                token == "}")
            {
                Throw(
                    "legacyDirectXInvalidSyntax");
            }

            var name =
                token;

            var open =
                NextMeaningful();

            if (open != "{")
            {
                Throw(
                    "legacyDirectXInvalidSyntax");
            }

            return name;
        }

        private void CloseCurrentBlock(
            string errorCode)
        {
            while (true)
            {
                var token =
                    NextMeaningful();

                if (token is null)
                {
                    Throw(errorCode);
                }

                if (token == "}")
                {
                    return;
                }

                if (token == "{")
                {
                    SkipOpenedBlock();
                }
            }
        }

        private void SkipObjectAfterKeyword()
        {
            SkipSeparators();

            var next =
                _tokens.Peek();

            if (next == "{")
            {
                _ = _tokens.Next();
                SkipOpenedBlock();
                return;
            }

            if (
                next is null ||
                next == "}")
            {
                return;
            }

            _ = _tokens.Next();

            SkipSeparators();

            if (_tokens.Peek() == "{")
            {
                _ = _tokens.Next();
                SkipOpenedBlock();
            }
        }

        private void SkipOpenedBlock()
        {
            var depth = 1;

            while (depth > 0)
            {
                var token =
                    _tokens.Next();

                if (token is null)
                {
                    Throw(
                        "legacyDirectXInvalidSyntax");
                }

                if (token == "{")
                {
                    depth++;
                }
                else if (token == "}")
                {
                    depth--;
                }
            }
        }

        private void
            SkipUntilCurrentBlockEnd()
        {
            var depth = 1;

            while (depth > 0)
            {
                var token =
                    _tokens.Next();

                if (token is null)
                {
                    Throw(
                        "legacyDirectXInvalidMaterialList");
                }

                if (token == "{")
                {
                    depth++;
                }
                else if (token == "}")
                {
                    depth--;
                }
            }
        }

        private int ReadCount(
            int maximum,
            string invalidCode,
            string tooLargeCode)
        {
            var value =
                ReadInt(invalidCode);

            if (value < 0)
            {
                Throw(invalidCode);
            }

            if (value > maximum)
            {
                Throw(tooLargeCode);
            }

            return value;
        }

        private int ReadInt(
            string errorCode)
        {
            var token =
                NextMeaningful();

            var value = 0;

            if (
                token is null ||
                !int.TryParse(
                    token,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                Throw(errorCode);
            }

            return value;
        }

        private float ReadFloat(
            string errorCode)
        {
            var token =
                NextMeaningful();

            var value = 0f;

            if (
                token is null ||
                !float.TryParse(
                    token,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value) ||
                !float.IsFinite(value))
            {
                Throw(errorCode);
            }

            return value;
        }

        private string? NextMeaningful()
        {
            while (true)
            {
                var token =
                    _tokens.Next();

                if (
                    token != ";" &&
                    token != ",")
                {
                    return token;
                }
            }
        }

        private void SkipSeparators()
        {
            while (
                _tokens.Peek() is
                    ";" or ",")
            {
                _ = _tokens.Next();
            }
        }

        private static bool EqualsToken(
            string? left,
            string right) =>
            string.Equals(
                left,
                right,
                StringComparison.OrdinalIgnoreCase);

        private static void Throw(
            string code) =>
            throw new DirectXParseException(
                code);

        private static OmsiO3dMaterial
            CreateDefaultMaterial() =>
            new(
                DiffuseR: 0.75f,
                DiffuseG: 0.75f,
                DiffuseB: 0.75f,
                DiffuseA: 1,
                SpecularR: 0,
                SpecularG: 0,
                SpecularB: 0,
                EmissionR: 0,
                EmissionG: 0,
                EmissionB: 0,
                SpecularPower: 0,
                TextureName: null);
    }

    private sealed class MutableMaterial
    {
        public float DiffuseR { get; set; }
        public float DiffuseG { get; set; }
        public float DiffuseB { get; set; }
        public float DiffuseA { get; set; }
        public float SpecularR { get; set; }
        public float SpecularG { get; set; }
        public float SpecularB { get; set; }
        public float EmissionR { get; set; }
        public float EmissionG { get; set; }
        public float EmissionB { get; set; }
        public float SpecularPower { get; set; }
        public string? TextureName { get; set; }

        public OmsiO3dMaterial
            ToImmutable() =>
            new(
                DiffuseR,
                DiffuseG,
                DiffuseB,
                DiffuseA,
                SpecularR,
                SpecularG,
                SpecularB,
                EmissionR,
                EmissionG,
                EmissionB,
                SpecularPower,
                TextureName);
    }

    private sealed class Tokenizer
    {
        private readonly string _text;
        private int _position;
        private string? _peeked;
        private bool _hasPeeked;

        public Tokenizer(
            string text)
        {
            _text = text;
        }

        public string? Peek()
        {
            if (!_hasPeeked)
            {
                _peeked =
                    ReadNext();
                _hasPeeked =
                    true;
            }

            return _peeked;
        }

        public string? Next()
        {
            if (_hasPeeked)
            {
                var value =
                    _peeked;

                _peeked = null;
                _hasPeeked = false;

                return value;
            }

            return ReadNext();
        }

        private string? ReadNext()
        {
            SkipTrivia();

            if (
                _position >=
                _text.Length)
            {
                return null;
            }

            var current =
                _text[_position];

            if (
                current is
                    '{' or
                    '}' or
                    ';' or
                    ',')
            {
                _position++;
                return current.ToString();
            }

            if (current == '"')
            {
                return ReadQuotedString();
            }

            var start =
                _position;

            while (
                _position <
                _text.Length)
            {
                current =
                    _text[_position];

                if (
                    char.IsWhiteSpace(
                        current) ||
                    current is
                        '{' or
                        '}' or
                        ';' or
                        ',' or
                        '"')
                {
                    break;
                }

                if (
                    current == '#' ||
                    (
                        current == '/' &&
                        _position + 1 <
                            _text.Length &&
                        _text[
                            _position + 1] ==
                            '/'
                    ))
                {
                    break;
                }

                _position++;
            }

            if (_position == start)
            {
                _position++;
                return current.ToString();
            }

            return _text[
                start.._position];
        }

        private string
            ReadQuotedString()
        {
            _position++;

            var builder =
                new StringBuilder();

            while (
                _position <
                _text.Length)
            {
                var current =
                    _text[
                        _position++];

                if (current == '"')
                {
                    return builder
                        .ToString();
                }

                if (
                    current == '\\' &&
                    _position <
                        _text.Length &&
                    _text[
                        _position] == '"')
                {
                    builder.Append('"');
                    _position++;
                    continue;
                }

                builder.Append(
                    current);
            }

            throw new DirectXParseException(
                "legacyDirectXInvalidString");
        }

        private void SkipTrivia()
        {
            while (
                _position <
                _text.Length)
            {
                if (
                    char.IsWhiteSpace(
                        _text[_position]))
                {
                    _position++;
                    continue;
                }

                if (
                    _text[_position] ==
                    '#')
                {
                    SkipLine();
                    continue;
                }

                if (
                    _text[_position] ==
                        '/' &&
                    _position + 1 <
                        _text.Length &&
                    _text[
                        _position + 1] ==
                        '/')
                {
                    _position += 2;
                    SkipLine();
                    continue;
                }

                break;
            }
        }

        private void SkipLine()
        {
            while (
                _position <
                    _text.Length &&
                _text[_position] !=
                    '\n')
            {
                _position++;
            }
        }
    }

    private sealed class
        DirectXParseException :
        Exception
    {
        public DirectXParseException(
            string code)
            : base(code)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
