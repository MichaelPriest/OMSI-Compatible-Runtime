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


// openOMSI-style city-map interactions: cursor-point invariant under
// heading-up/north-up projection, drag and zoom.
foreach (var heading in new[] { 0.0f, 0.71f, -2.2f })
{
    var origin = new Vector2(1234.5f, -455.0f);
    var point = new Vector2(1377.0f, -280.25f);
    var screenCenter = new System.Drawing.PointF(350, 260);
    var screen = RuntimeNavMapProjection.ToScreen(
        point, origin, screenCenter, 0.43f, heading);
    var inverse = RuntimeNavMapProjection.ToWorld(
        screen, origin, screenCenter, 0.43f, heading);
    Require(Vector2.Distance(point, inverse) < 0.001f,
        "Map projection did not round-trip in heading-up/north-up mode.");

    var movedCenter = RuntimeNavMapProjection.Pan(
        origin, screen, new System.Drawing.PointF(
            screen.X + 65, screen.Y - 45), 0.43f, heading);
    var movedScreen = RuntimeNavMapProjection.ToScreen(
        point, movedCenter, screenCenter, 0.43f, heading);
    Require(Math.Abs(movedScreen.X - screen.X - 65.0f) < 0.01f &&
            Math.Abs(movedScreen.Y - screen.Y + 45.0f) < 0.01f,
        "Dragging the full map did not move the scene with the cursor.");

    var zoomCenter = RuntimeNavMapProjection.ZoomAtCursor(
        origin, screen, screenCenter, 0.43f, 0.86f, heading);
    var zoomedScreen = RuntimeNavMapProjection.ToScreen(
        point, zoomCenter, screenCenter, 0.86f, heading);
    Require(Math.Abs(zoomedScreen.X - screen.X) < 0.01f &&
            Math.Abs(zoomedScreen.Y - screen.Y) < 0.01f,
        "Wheel zoom moved the world point under the mouse cursor.");
}
Console.WriteLine("Navigation city-map pan/zoom/follow projection tests passed.");
