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
    var mapPoint = new Vector2(1377.0f, -280.25f);
    var screenCenter = new System.Drawing.PointF(350, 260);
    var screen = RuntimeNavMapProjection.ToScreen(
        mapPoint, origin, screenCenter, 0.43f, heading);
    var inverse = RuntimeNavMapProjection.ToWorld(
        screen, origin, screenCenter, 0.43f, heading);
    Require(Vector2.Distance(mapPoint, inverse) < 0.001f,
        "Map projection did not round-trip in heading-up/north-up mode.");

    var movedCenter = RuntimeNavMapProjection.Pan(
        origin, screen, new System.Drawing.PointF(
            screen.X + 65, screen.Y - 45), 0.43f, heading);
    var movedScreen = RuntimeNavMapProjection.ToScreen(
        mapPoint, movedCenter, screenCenter, 0.43f, heading);
    Require(Math.Abs(movedScreen.X - screen.X - 65.0f) < 0.01f &&
            Math.Abs(movedScreen.Y - screen.Y + 45.0f) < 0.01f,
        "Dragging the full map did not move the scene with the cursor.");

    var zoomCenter = RuntimeNavMapProjection.ZoomAtCursor(
        origin, screen, screenCenter, 0.43f, 0.86f, heading);
    var zoomedScreen = RuntimeNavMapProjection.ToScreen(
        mapPoint, zoomCenter, screenCenter, 0.86f, heading);
    Require(Math.Abs(zoomedScreen.X - screen.X) < 0.01f &&
            Math.Abs(zoomedScreen.Y - screen.Y) < 0.01f,
        "Wheel zoom moved the world point under the mouse cursor.");
}
Console.WriteLine("Navigation city-map pan/zoom/follow projection tests passed.");


// Real OMSI path types and their indexed road geometry.
var lane = new RuntimeTrafficPathSegmentInfo(
    71, 999, 0, 0, 0, 3.5,
    [new RuntimeTrafficPathPointInfo(0, 0, 0),
     new RuntimeTrafficPathPointInfo(30, 0, 0),
     new RuntimeTrafficPathPointInfo(60, 0, 0)],
    [], [], 40.0);
var pedestrian = lane with { Index = 72, Type = 1 };
var rail = lane with { Index = 73, Type = 2 };
var network = new RuntimeTrafficPathNetworkInfo(
    [lane, pedestrian, rail], 1, 1, 1, 0, 0, 0, 0, 0);
var roadSections = RuntimeNavTrafficMap.Build(network);
Require(roadSections.Length == 2 && roadSections.All(s => s.SegmentIndex == 71),
    "City map included pedestrian/rail paths or missed a real motor lane.");
var roadIndex = new RuntimeNavRoadIndex(roadSections);
Require(roadIndex.Count == 2 &&
        roadIndex.Nearby(new Vector2(20, 0), 40).Count() == 2 &&
        !roadIndex.Nearby(new Vector2(900, 900), 40).Any(),
    "Spatially indexed full-map road lookup returned invalid geometry.");

// A lone AI waiting at a light does not justify a red traffic warning.
var car1 = new RuntimeTrafficAgentInfo(
    11, 71, 10.0, 0.4, "Vehicles/Test/car.bus", 10, 0, 0, 0);
var car2 = car1 with { AgentIndex = 12, X = 25 };
var limits = new Dictionary<int, double> { [71] = 40.0 };
Require(RuntimeNavTrafficMap.EstimateCongestion([car1], limits).Count == 0,
    "A single stopped AI vehicle was incorrectly reported as congestion.");
var congested = RuntimeNavTrafficMap.EstimateCongestion(
    [car1, car2], limits);
Require(congested.TryGetValue(71, out var level) && level > 0.7f,
    "Two slow real AI vehicles did not mark their OMSI lane as congested.");
var fastCar1 = car1 with { SpeedMetersPerSecond = 11.0 };
var fastCar2 = car2 with { SpeedMetersPerSecond = 11.0 };
Require(RuntimeNavTrafficMap.EstimateCongestion(
    [fastCar1, fastCar2], limits).Count == 0,
    "Fast vehicles should not produce a congestion warning.");

// Timetable stop markers belong to authored route geometry and names.
var stopRoute = new[]
{
    new RuntimeTrafficPathPointInfo(0, 0, 0),
    new RuntimeTrafficPathPointInfo(50, 0, 0),
    new RuntimeTrafficPathPointInfo(100, 0, 0)
};
var positioned = RuntimeNavStopProjector.Place(stopRoute,
    [new RuntimeNavigationStopDistanceInfo("Terminal A", 0, true),
     new RuntimeNavigationStopDistanceInfo("Intermediária", 30, true),
     new RuntimeNavigationStopDistanceInfo("Terminal B", 60, true)]);
Require(positioned.Length == 3 &&
        positioned[1].Name == "Intermediária" &&
        Math.Abs(positioned[1].X - 50) < 0.01 &&
        positioned[2].IsTerminus &&
        Math.Abs(positioned[2].X - 100) < 0.01,
    "Timetable stop interpolation diverged from the selected OMSI route.");
Console.WriteLine("OMSI road network / real AI congestion / named stop tests passed.");
