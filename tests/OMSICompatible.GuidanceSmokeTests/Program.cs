using System.Numerics;
using OMSICompatible.Renderer.Common;
using OMSICompatible.Renderer.D3D11;
using Vortice.Mathematics;

// A real triangle mesh, not a mocked sampler: sloped asphalt/bridge surface
// under the actual Forza-style guide chevron geometry.
var color = new Color4(1f, 1f, 1f, 1f);
RuntimeObjectVertex V(float x, float z) =>
    new(new Vector3(x, 0.3f + 0.04f * z, z), color, Vector2.Zero);

var road = new RuntimeSplineGeometry(
    [
        V(-3, 10), V(3, 10), V(3, 80),
        V(-3, 10), V(3, 80), V(-3, 80)
    ],
    [], 1, 0);
var sampler = RuntimeSplineSurfaceSampler.Create(road);

void Require(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(message);
}

Require(sampler.TriangleCount == 2, "Road mesh did not register 2 real triangles.");
var point = new RuntimeNavigationGuidancePointInfo(
    0, 0.3 + 0.04 * 35, 35, 0,
    Math.Atan(0.04) * 180 / Math.PI, 25, "route");

var verts = RuntimeNavigationGuidanceGeometry.Build(
    [point], sampler, RuntimeSplineSurfaceSampler.Empty);
Require(verts.Length == 72,
    $"Expected layered chevron of 72 vertices, received {verts.Length}.");

Require(verts.All(v =>
    float.IsFinite(v.Position.X) &&
    float.IsFinite(v.Position.Y) &&
    float.IsFinite(v.Position.Z) &&
    Math.Abs(v.Position.Y - (0.3 + 0.04 * v.Position.Z)) < 0.08),
    "Ground chevrons floated above or clipped through the sloped road surface.");
Require(verts.Any(v => v.Color.B > 0.9f),
    "Ground guide lost the cyan blue Forza-style highlight.");

var noRoad = RuntimeNavigationGuidanceGeometry.Build(
    [point], RuntimeSplineSurfaceSampler.Empty, RuntimeSplineSurfaceSampler.Empty);
Require(noRoad.Length == 0,
    "Ground guidance must not appear without streamed road geometry.");

var overpass = point with { Y = 17.0 };
var wrongLevel = RuntimeNavigationGuidanceGeometry.Build(
    [overpass], sampler, RuntimeSplineSurfaceSampler.Empty);
Require(wrongLevel.Length == 0,
    "Bridge guidance projected onto road of the wrong vertical level.");

var nonFinite = point with { X = double.NaN };
Require(RuntimeNavigationGuidanceGeometry.Build(
    [nonFinite], sampler, RuntimeSplineSurfaceSampler.Empty).Length == 0,
    "Invalid navigation coordinates must be rejected.");

Console.WriteLine(
    $"Forza-style guidance smoke passed; vertices={verts.Length}; roadTriangles={sampler.TriangleCount}");
