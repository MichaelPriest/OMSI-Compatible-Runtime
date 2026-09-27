using System.Numerics;

namespace OMSICompatible.Renderer.D3D11;

internal sealed class RuntimeSplineSurfaceSampler
{
    private const float CellSizeMeters = 12.0f;
    private const float CoordinateTolerance = 0.0005f;
    private const float HeightTolerance = 0.05f;

    private readonly Dictionary<(int X, int Z), SurfaceTriangle[]>
        _trianglesByCell;

    private RuntimeSplineSurfaceSampler(
        Dictionary<(int X, int Z), SurfaceTriangle[]> trianglesByCell,
        int triangleCount)
    {
        _trianglesByCell =
            trianglesByCell;
        TriangleCount =
            triangleCount;
    }

    public static RuntimeSplineSurfaceSampler Empty { get; } =
        new(
            new Dictionary<(int X, int Z), SurfaceTriangle[]>(),
            0);

    public int TriangleCount { get; }

    public static RuntimeSplineSurfaceSampler Create(
        RuntimeSplineGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(
            geometry);

        if (geometry.Vertices.Length < 3)
        {
            return Empty;
        }

        var cells =
            new Dictionary<
                (int X, int Z),
                List<SurfaceTriangle>>();

        var triangleCount =
            0;

        for (var index = 0;
             index + 2 < geometry.Vertices.Length;
             index += 3)
        {
            var a =
                geometry.Vertices[index].Position;
            var b =
                geometry.Vertices[index + 1].Position;
            var c =
                geometry.Vertices[index + 2].Position;

            if (!TryRegisterTriangle(
                    a,
                    b,
                    c,
                    cells))
            {
                continue;
            }

            triangleCount++;
        }

        return triangleCount ==
               0
            ? Empty
            : new RuntimeSplineSurfaceSampler(
                cells.ToDictionary(
                    static pair =>
                        pair.Key,
                    static pair =>
                        pair.Value.ToArray()),
                triangleCount);
    }

    public static RuntimeSplineSurfaceSampler CreateSurfaceObjects(
        RuntimeObjectGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(
            geometry);

        if (geometry.Vertices.Length <
                3 ||
            geometry.Batches.Count ==
                0)
        {
            return Empty;
        }

        var cells =
            new Dictionary<
                (int X, int Z),
                List<SurfaceTriangle>>();

        var triangleCount =
            0;

        foreach (var batch in
                 geometry.Batches)
        {
            if (!batch.Surface ||
                batch.ObjectId >=
                    0 ||
                batch.VertexCount <
                    3)
            {
                continue;
            }

            var start =
                checked(
                    (int)batch.StartVertex);

            var end =
                Math.Min(
                    checked(
                        start +
                        (int)batch.VertexCount),
                    geometry.Vertices.Length);

            for (var index = start;
                 index +
                     2 <
                 end;
                 index +=
                     3)
            {
                var a =
                    geometry.Vertices[
                        index]
                        .Position;

                var b =
                    geometry.Vertices[
                        index +
                        1]
                        .Position;

                var c =
                    geometry.Vertices[
                        index +
                        2]
                        .Position;

                if (!TryRegisterTriangle(
                        a,
                        b,
                        c,
                        cells))
                {
                    continue;
                }

                triangleCount++;
            }
        }

        return triangleCount ==
               0
            ? Empty
            : new RuntimeSplineSurfaceSampler(
                cells.ToDictionary(
                    static pair =>
                        pair.Key,
                    static pair =>
                        pair.Value.ToArray()),
                triangleCount);
    }

    public bool TrySampleBelow(
        double worldX,
        double worldZ,
        float maximumHeight,
        out float height)
    {
        height =
            float.NegativeInfinity;

        if (!double.IsFinite(
                worldX) ||
            !double.IsFinite(
                worldZ) ||
            !float.IsFinite(
                maximumHeight))
        {
            return false;
        }

        var key =
            (
                Cell(
                    (float)worldX),
                Cell(
                    (float)worldZ)
            );

        if (!_trianglesByCell.TryGetValue(
                key,
                out var triangles))
        {
            return false;
        }

        var x =
            (float)worldX;

        var z =
            (float)worldZ;

        var found =
            false;

        foreach (var triangle in
                 triangles)
        {
            if (!triangle.TryInterpolateHeight(
                    x,
                    z,
                    out var candidate) ||
                candidate >
                    maximumHeight +
                    HeightTolerance ||
                (found &&
                 candidate <=
                    height))
            {
                continue;
            }

            height =
                candidate;

            found =
                true;
        }

        return found;
    }

    private static bool TryRegisterTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        IDictionary<
            (int X, int Z),
            List<SurfaceTriangle>>
            cells)
    {
        if (!IsFinite(a) ||
            !IsFinite(b) ||
            !IsFinite(c))
        {
            return false;
        }

        var ab =
            b -
            a;

        var ac =
            c -
            a;

        var normal =
            Vector3.Cross(
                ab,
                ac);

        var normalLength =
            normal.Length();

        var projectedArea =
            Math.Abs(
                Cross2D(
                    ab.X,
                    ab.Z,
                    ac.X,
                    ac.Z));

        if (projectedArea <
                0.000001f ||
            normalLength <
                0.000001f ||
            Math.Abs(
                normal.Y) /
                normalLength <
                0.15f)
        {
            return false;
        }

        var triangle =
            new SurfaceTriangle(
                a,
                b,
                c);

        var minCellX =
            Cell(
                Math.Min(
                    a.X,
                    Math.Min(
                        b.X,
                        c.X)));

        var maxCellX =
            Cell(
                Math.Max(
                    a.X,
                    Math.Max(
                        b.X,
                        c.X)));

        var minCellZ =
            Cell(
                Math.Min(
                    a.Z,
                    Math.Min(
                        b.Z,
                        c.Z)));

        var maxCellZ =
            Cell(
                Math.Max(
                    a.Z,
                    Math.Max(
                        b.Z,
                        c.Z)));

        for (var cellX =
                 minCellX;
             cellX <=
                 maxCellX;
             cellX++)
        {
            for (var cellZ =
                     minCellZ;
                 cellZ <=
                     maxCellZ;
                 cellZ++)
            {
                var key =
                    (cellX, cellZ);

                if (!cells.TryGetValue(
                        key,
                        out var bucket))
                {
                    bucket = [];
                    cells[key] =
                        bucket;
                }

                bucket.Add(
                    triangle);
            }
        }

        return true;
    }

    private static int Cell(
        float value) =>
        (int)MathF.Floor(
            value /
            CellSizeMeters);

    private static bool IsFinite(
        Vector3 value) =>
        float.IsFinite(
            value.X) &&
        float.IsFinite(
            value.Y) &&
        float.IsFinite(
            value.Z);

    private static float Cross2D(
        float ax,
        float az,
        float bx,
        float bz) =>
        ax *
            bz -
        az *
            bx;

    private readonly record struct SurfaceTriangle(
        Vector3 A,
        Vector3 B,
        Vector3 C)
    {
        public bool TryInterpolateHeight(
            float x,
            float z,
            out float height)
        {
            height =
                0.0f;

            var denominator =
                (B.Z - C.Z) *
                    (A.X - C.X) +
                (C.X - B.X) *
                    (A.Z - C.Z);

            if (Math.Abs(
                    denominator) <
                0.000001f)
            {
                return false;
            }

            var aWeight =
                ((B.Z - C.Z) *
                     (x - C.X) +
                 (C.X - B.X) *
                     (z - C.Z)) /
                denominator;

            var bWeight =
                ((C.Z - A.Z) *
                     (x - C.X) +
                 (A.X - C.X) *
                     (z - C.Z)) /
                denominator;

            var cWeight =
                1.0f -
                aWeight -
                bWeight;

            if (aWeight <
                    -CoordinateTolerance ||
                bWeight <
                    -CoordinateTolerance ||
                cWeight <
                    -CoordinateTolerance ||
                aWeight >
                    1.0f +
                    CoordinateTolerance ||
                bWeight >
                    1.0f +
                    CoordinateTolerance ||
                cWeight >
                    1.0f +
                    CoordinateTolerance)
            {
                return false;
            }

            height =
                aWeight *
                    A.Y +
                bWeight *
                    B.Y +
                cWeight *
                    C.Y;

            return float.IsFinite(
                height);
        }
    }
}
