using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

public readonly record struct RuntimeFuelTrackState(
    double? Percent,
    double? ContentLiters,
    string Kind,
    bool ReturnToGarageRecommended)
{
    public static RuntimeFuelTrackState Unknown { get; } =
        new(
            null,
            null,
            "COMBUSTÍVEL",
            false);
}

internal sealed class RuntimeNavPulsePanel : Panel
{
    private readonly record struct RoadSegment(
        Vector2 A,
        Vector2 B,
        Vector2 Midpoint);

    private RoadSegment[] _roads;
    private readonly Button _modeButton;
    private readonly Button _expandButton;
    private readonly Button _guidanceButton;
    private readonly Button _followButton;
    private readonly Button _northButton;
    private readonly Button _trafficButton;
    private readonly Button _zoomInButton;
    private readonly Button _zoomOutButton;
    private Vector3 _position;
    private float _headingRadians;
    private readonly record struct MapVehicle(
        Vector2 Position,
        float HeadingRadians,
        bool IsPeer);

    private MapVehicle[] _mapVehicles = [];
    private Vector2[] _route =
        Array.Empty<Vector2>();
    private RuntimeFuelTrackState _fuel =
        RuntimeFuelTrackState.Unknown;
    private bool _circleMode;
    private bool _expanded;
    private bool _guidanceEnabled;
    private bool _following = true;
    private bool _northUp;
    private bool _showTraffic = true;
    private bool _hasFreeCenter;
    private Vector2 _freeCenter;
    private float _fullMapRangeMeters = 1350.0f;
    private PointF? _panAnchor;
    private Vector2 _panStartCenter;
    private float _rangeMeters =
        450.0f;

    public bool Expanded =>
        _expanded;

    public bool GuidanceEnabled => _guidanceEnabled;
    public bool FollowingVehicle => _following;
    public bool NorthUp => _northUp;
    public bool ShowTraffic => _showTraffic;
    public float FullMapRangeMeters => _fullMapRangeMeters;

    public event EventHandler? MapPreferencesChanged;
    public event EventHandler?
        DisplayModeChanged;

    public event EventHandler?
        GuidanceVisibilityChanged;

    public RuntimeNavPulsePanel(
        IReadOnlyList<RuntimeSplineInfo> splines)
    {
        DoubleBuffered = true;
        Visible = false;
        BackColor =
            Color.FromArgb(
                35,
                42,
                48);
        ForeColor = Color.White;
        BorderStyle =
            BorderStyle.FixedSingle;

        _roads =
            BuildRoadSegments(
                splines);

        _modeButton =
            new Button
            {
                Text = "○",
                Width = 30,
                Height = 24,
                Top = 4,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        205,
                        210,
                        216),
                ForeColor =
                    Color.FromArgb(
                        30,
                        35,
                        40),
                Font =
                    new Font(
                        "Segoe UI",
                        9.0f,
                        FontStyle.Bold),
                TabStop = false
            };

        _modeButton.FlatAppearance.BorderColor =
            Color.FromArgb(
                130,
                138,
                146);
        _modeButton.Click +=
            (_, _) =>
            {
                _circleMode =
                    !_circleMode;
                _modeButton.Text =
                    _circleMode
                        ? "□"
                        : "○";
                Invalidate();
            };

        _expandButton =
            new Button
            {
                Text = "MAX",
                Width = 40,
                Height = 24,
                Top = 4,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        205,
                        210,
                        216),
                ForeColor =
                    Color.FromArgb(
                        30,
                        35,
                        40),
                Font =
                    new Font(
                        "Segoe UI",
                        7.5f,
                        FontStyle.Bold),
                TabStop = false
            };

        _guidanceButton =
            new Button
            {
                Text = "SETAS",
                Width = 52,
                Height = 24,
                Top = 4,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        205,
                        210,
                        216),
                ForeColor =
                    Color.FromArgb(
                        30,
                        35,
                        40),
                Font =
                    new Font(
                        "Segoe UI",
                        7.0f,
                        FontStyle.Bold),
                TabStop = false
            };

        _guidanceButton.FlatAppearance.BorderColor =
            Color.FromArgb(
                130,
                138,
                146);
        _guidanceButton.Click +=
            (_, _) =>
            {
                SetGuidanceEnabled(!_guidanceEnabled);
            };

        _expandButton.FlatAppearance.BorderColor =
            Color.FromArgb(
                130,
                138,
                146);
        _expandButton.Click +=
            (_, _) =>
            {
                SetExpanded(!_expanded);
            };

        _guidanceButton.Text = "SEM SETA";

        _followButton = MapControl("SEGUIR", 53);
        _northButton = MapControl("GIRO", 50);
        _trafficButton = MapControl("IA ON", 50);
        _zoomInButton = MapControl("+", 27);
        _zoomOutButton = MapControl("−", 27);

        _followButton.Click += (_, _) => SetFollowing(!_following);
        _northButton.Click += (_, _) =>
        {
            _northUp = !_northUp;
            RefreshMapOptions();
        };
        _trafficButton.Click += (_, _) =>
        {
            _showTraffic = !_showTraffic;
            RefreshMapOptions();
        };
        _zoomInButton.Click += (_, _) => ZoomAtCenter(0.75f);
        _zoomOutButton.Click += (_, _) => ZoomAtCenter(1.3333334f);

        Controls.AddRange(
            [_followButton, _northButton, _trafficButton,
             _zoomInButton, _zoomOutButton]);
        Controls.Add(
            _modeButton);
        Controls.Add(
            _expandButton);
        Controls.Add(
            _guidanceButton);

        Resize +=
            (_, _) =>
            {
                _modeButton.Left =
                    Math.Max(
                        4,
                        Width -
                        _modeButton.Width -
                        5);

                _expandButton.Left =
                    Math.Max(
                        4,
                        _modeButton.Left -
                        _expandButton.Width -
                        4);

                _guidanceButton.Left =
                    Math.Max(
                        4,
                        _expandButton.Left -
                        _guidanceButton.Width -
                        4);

                LayoutMapControls();
            };
        UpdateMapButtons();
        LayoutMapControls();
    }

    private static Button MapControl(string text, int width)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 24,
            Top = 4,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 61, 81),
            ForeColor = Color.FromArgb(217, 235, 249),
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            TabStop = false
        };
        button.FlatAppearance.BorderColor =
            Color.FromArgb(90, 126, 152);
        return button;
    }

    private void LayoutMapControls()
    {
        var left = _guidanceButton.Left - 4;
        foreach (var control in new[]
        {
            _trafficButton, _northButton, _followButton,
            _zoomOutButton, _zoomInButton
        })
        {
            control.Visible = _expanded;
            left -= control.Width + 4;
            control.Left = Math.Max(4, left);
        }
    }

    private void UpdateMapButtons()
    {
        _followButton.Text = _following ? "SEGUIR" : "LIVRE";
        _northButton.Text = _northUp ? "NORTE" : "GIRO";
        _trafficButton.Text = _showTraffic ? "IA ON" : "IA OFF";
        _zoomOutButton.Enabled = _fullMapRangeMeters < 11999.0f;
        _zoomInButton.Enabled = _fullMapRangeMeters > 120.01f;
    }

    private void RefreshMapOptions()
    {
        UpdateMapButtons();
        Invalidate();
        MapPreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreMapPreferences(
        bool northUp,
        bool showTraffic,
        bool following,
        float rangeMeters)
    {
        _northUp = northUp;
        _showTraffic = showTraffic;
        _following = following;
        _fullMapRangeMeters = float.IsFinite(rangeMeters)
            ? Math.Clamp(rangeMeters, 120.0f, 12000.0f)
            : 1350.0f;
        UpdateMapButtons();
        Invalidate();
    }

    public void SetRoadNetwork(IReadOnlyList<RuntimeSplineInfo> splines)
    {
        ArgumentNullException.ThrowIfNull(splines);
        // Called on the UI thread when a streaming generation is committed,
        // not during OnPaint or every frame. No stale startup-only roads.
        _roads = BuildRoadSegments(splines);
        if (Visible)
        {
            Invalidate();
        }
    }

    private Vector2 ViewCenter =>
        !_expanded || _following || !_hasFreeCenter
            ? new Vector2(_position.X, _position.Z)
            : _freeCenter;

    private float ViewRange =>
        _expanded ? _fullMapRangeMeters : _rangeMeters;

    private float MapHeading =>
        _expanded && _northUp ? 0.0f : _headingRadians;

    private RectangleF MapArea => new(
        5.0f, 31.0f,
        Math.Max(80.0f, Width - 10.0f),
        Math.Max(80.0f, Height - 31.0f - 45.0f - 5.0f));

    private float MapScale(RectangleF bounds) =>
        Math.Min(bounds.Width, bounds.Height) /
        (ViewRange * 2.0f);

    private void SetFollowing(bool following)
    {
        if (_following == following)
        {
            return;
        }

        if (!following)
        {
            _freeCenter = ViewCenter;
            _hasFreeCenter = true;
        }
        _following = following;
        RefreshMapOptions();
    }

    private void ZoomAtCenter(float factor)
    {
        if (!_expanded || !float.IsFinite(factor))
        {
            return;
        }
        _fullMapRangeMeters = Math.Clamp(
            _fullMapRangeMeters * factor, 120.0f, 12000.0f);
        RefreshMapOptions();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_expanded || e.Button != MouseButtons.Left ||
            !MapArea.Contains(e.Location))
        {
            return;
        }

        if (_following)
        {
            _freeCenter = ViewCenter;
            _hasFreeCenter = true;
            _following = false;
        }

        _panAnchor = e.Location;
        _panStartCenter = ViewCenter;
        Capture = true;
        UpdateMapButtons();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_expanded || _panAnchor is not { } start ||
            (e.Button & MouseButtons.Left) == 0)
        {
            return;
        }

        _freeCenter = RuntimeNavMapProjection.Pan(
            _panStartCenter, start, e.Location,
            MapScale(MapArea), MapHeading);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_expanded || e.Button != MouseButtons.Left ||
            !_panAnchor.HasValue)
        {
            return;
        }
        _panAnchor = null;
        Capture = false;
        RefreshMapOptions();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_expanded || e.Delta == 0)
        {
            return;
        }

        var map = MapArea;
        var factor = MathF.Pow(0.82f, e.Delta / 120.0f);
        var previousRange = _fullMapRangeMeters;
        _fullMapRangeMeters = Math.Clamp(
            previousRange * factor, 120.0f, 12000.0f);

        if (map.Contains(e.Location))
        {
            var center = new PointF(
                map.Left + map.Width / 2.0f,
                map.Top + map.Height / 2.0f);
            _freeCenter = RuntimeNavMapProjection.ZoomAtCursor(
                ViewCenter, e.Location, center,
                Math.Min(map.Width, map.Height) / (previousRange * 2.0f),
                MapScale(map), MapHeading);
            _hasFreeCenter = true;
            _following = false;
        }
        RefreshMapOptions();
    }

    public void TogglePanel()
    {
        Visible = !Visible;
        if (Visible)
        {
            BringToFront();
            Invalidate();
        }
    }

    public void SetGuidanceEnabled(bool enabled)
    {
        if (_guidanceEnabled == enabled)
        {
            return;
        }

        _guidanceEnabled = enabled;
        _guidanceButton.Text = enabled ? "SETAS" : "SEM SETA";
        GuidanceVisibilityChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
        {
            return;
        }

        _expanded = expanded;
        if (expanded)
        {
            _circleMode = false;
            _modeButton.Text = "○";
        }

        _expandButton.Text = expanded ? "MIN" : "MAX";
        if (expanded && !_hasFreeCenter)
        {
            _freeCenter = new Vector2(_position.X, _position.Z);
            _hasFreeCenter = true;
        }
        LayoutMapControls();
        DisplayModeChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SetRoute(
        IReadOnlyList<RuntimeTrafficPathPointInfo> points)
    {
        _route =
            points
                .Select(
                    static point =>
                        new Vector2(
                            (float)point.X,
                            (float)point.Z))
                .ToArray();

        if (Visible)
        {
            Invalidate();
        }
    }

    public void UpdateState(
        Vector3 position,
        float headingRadians,
        float speedMetersPerSecond,
        IReadOnlyList<RuntimeTrafficAgentInfo> traffic,
        RuntimeFuelTrackState fuel)
    {
        _position =
            position;
        _headingRadians =
            headingRadians;
        _fuel =
            fuel;
        _rangeMeters =
            SuggestedRadius(
                Math.Abs(
                    speedMetersPerSecond) *
                3.6f);

        if (!_hasFreeCenter)
        {
            _freeCenter = new Vector2(_position.X, _position.Z);
            _hasFreeCenter = true;
        }

        // openOMSI-style moving vehicle icons: use actual sim/peer agents
        // and their heading, not a peer-only circle list or placeholder AI.
        var effectiveRange = Math.Max(_fullMapRangeMeters, _rangeMeters);
        var maximumDistanceSquared = (effectiveRange + 300.0f) *
                                     (effectiveRange + 300.0f);
        _mapVehicles = traffic
            .Where(agent =>
                double.IsFinite(agent.X) &&
                double.IsFinite(agent.Z) &&
                double.IsFinite(agent.HeadingRadians) &&
                (agent.X - _position.X) * (agent.X - _position.X) +
                (agent.Z - _position.Z) * (agent.Z - _position.Z)
                    <= maximumDistanceSquared)
            .Take(400)
            .Select(agent =>
                new MapVehicle(
                    new Vector2((float)agent.X, (float)agent.Z),
                    (float)agent.HeadingRadians,
                    (unchecked((uint)agent.AgentIndex) & 0x60000000u)
                        == 0x60000000u))
            .ToArray();

        if (Visible)
        {
            Invalidate();
        }
    }

    protected override void OnPaint(
        PaintEventArgs e)
    {
        base.OnPaint(
            e);

        var graphics =
            e.Graphics;

        graphics.SmoothingMode =
            System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var background =
            new SolidBrush(
                Color.FromArgb(
                    38,
                    44,
                    50));

        graphics.FillRectangle(
            background,
            ClientRectangle);

        var mapTop =
            31.0f;
        var footerHeight =
            45.0f;
        var mapHeight =
            Math.Max(
                80.0f,
                Height -
                mapTop -
                footerHeight -
                5.0f);
        var mapWidth =
            Math.Max(
                80.0f,
                Width -
                10.0f);

        var mapRect =
            MapArea;

        var previousClip =
            graphics.Clip.Clone();

        if (_circleMode)
        {
            using var path =
                new System.Drawing.Drawing2D.GraphicsPath();

            var size =
                Math.Min(
                    mapRect.Width,
                    mapRect.Height);

            var circle =
                new RectangleF(
                    mapRect.X +
                    (mapRect.Width - size) /
                    2.0f,
                    mapRect.Y +
                    (mapRect.Height - size) /
                    2.0f,
                    size,
                    size);

            path.AddEllipse(
                circle);
            graphics.SetClip(
                path);
            mapRect =
                circle;
        }
        else
        {
            graphics.SetClip(
                mapRect);
        }

        using var mapBrush =
            new SolidBrush(
                Color.FromArgb(
                    28,
                    33,
                    38));
        graphics.FillRectangle(
            mapBrush,
            mapRect);

        var center =
            new PointF(
                mapRect.Left +
                mapRect.Width /
                2.0f,
                mapRect.Top +
                mapRect.Height /
                2.0f);

        var scale = MapScale(mapRect);
        var viewCenter = ViewCenter;
        var range = ViewRange;

        using var roadPen =
            new Pen(
                Color.FromArgb(
                    112,
                    122,
                    130),
                1.25f);

        foreach (var road in
                 _roads)
        {
            var dx =
                road.Midpoint.X -
                viewCenter.X;
            var dz =
                road.Midpoint.Y -
                viewCenter.Y;

            if (dx * dx +
                dz * dz >
                (range + 80.0f) *
                (range + 80.0f))
            {
                continue;
            }

            var a =
                ToScreen(
                    road.A,
                    center,
                    scale);
            var b =
                ToScreen(
                    road.B,
                    center,
                    scale);

            graphics.DrawLine(
                roadPen,
                a,
                b);
        }

        if (_route.Length >
            1)
        {
            using var routePen =
                new Pen(
                    Color.FromArgb(
                        205,
                        35,
                        190,
                        245),
                    3.0f);

            for (var index = 1;
                 index <
                     _route.Length;
                 index++)
            {
                var aWorld =
                    _route[
                        index -
                        1];
                var bWorld =
                    _route[
                        index];

                var midpoint =
                    (
                        aWorld +
                        bWorld
                    ) /
                    2.0f;

                var delta =
                    midpoint -
                    new Vector2(
                        viewCenter.X,
                        viewCenter.Y);

                if (delta.LengthSquared() >
                    (range + 120.0f) *
                    (range + 120.0f))
                {
                    continue;
                }

                graphics.DrawLine(
                    routePen,
                    ToScreen(
                        aWorld,
                        center,
                        scale),
                    ToScreen(
                        bWorld,
                        center,
                        scale));
            }
        }

        if (_showTraffic)
        {
            foreach (var vehicle in _mapVehicles)
            {
                if (Vector2.DistanceSquared(vehicle.Position, viewCenter) >
                    range * range * 2.0f)
                {
                    continue;
                }

                DrawVehicleMarker(
                    graphics,
                    ToScreen(vehicle.Position, center, scale),
                    vehicle.HeadingRadians - MapHeading,
                    vehicle.IsPeer
                        ? Color.FromArgb(87, 174, 221)
                        : Color.FromArgb(240, 182, 83),
                    5.0f);
            }
        }

        // The bus belongs at its actual world position, not permanently
        // at the map centre when follow mode is off.
        DrawVehicleMarker(
            graphics,
            ToScreen(new Vector2(_position.X, _position.Z), center, scale),
            _headingRadians - MapHeading,
            Color.FromArgb(245, 245, 245),
            9.0f);

        graphics.Clip =
            previousClip;

        using var titleFont =
            new Font(
                "Segoe UI",
                9.0f,
                FontStyle.Bold);
        using var bodyFont =
            new Font(
                "Segoe UI",
                8.0f,
                FontStyle.Bold);
        using var titleBrush =
            new SolidBrush(
                Color.FromArgb(
                    225,
                    229,
                    233));

        graphics.DrawString(
            _expanded
                ? $"MAPA · {(_following ? "SEGUIR" : "LIVRE")} · " +
                  $"{(_northUp ? "NORTE" : "DIREÇÃO")} · {range:0} m"
                : $"NAVPULSE · {_rangeMeters:0} m",
            titleFont,
            titleBrush,
            8.0f,
            7.0f);

        DrawFuelTrack(
            graphics,
            bodyFont,
            mapRect.Bottom + 5.0f);
    }

    private static float SuggestedRadius(
        float speedKph) =>
        speedKph switch
        {
            < 10.0f => 450.0f,
            < 25.0f => 650.0f,
            < 45.0f => 900.0f,
            < 65.0f => 1250.0f,
            < 90.0f => 1750.0f,
            _ => 2400.0f
        };

    private PointF ToScreen(
        Vector2 world,
        PointF center,
        float scale) =>
        RuntimeNavMapProjection.ToScreen(
            world, ViewCenter, center, scale, MapHeading);

    private static void DrawVehicleMarker(
        Graphics graphics,
        PointF center,
        float heading,
        Color color,
        float size)
    {
        var sin = MathF.Sin(heading);
        var cos = MathF.Cos(heading);
        var forward = new PointF(sin * size, -cos * size);
        var right = new PointF(cos * size * 0.58f, sin * size * 0.58f);
        var points = new[]
        {
            new PointF(center.X + forward.X, center.Y + forward.Y),
            new PointF(center.X + right.X - forward.X * 0.65f,
                       center.Y + right.Y - forward.Y * 0.65f),
            new PointF(center.X - right.X - forward.X * 0.65f,
                       center.Y - right.Y - forward.Y * 0.65f)
        };
        using var brush = new SolidBrush(color);
        using var outline = new Pen(Color.FromArgb(22, 28, 36), 1.0f);
        graphics.FillPolygon(brush, points);
        graphics.DrawPolygon(outline, points);
    }

    private void DrawFuelTrack(
        Graphics graphics,
        Font font,
        float y)
    {
        var percent =
            _fuel.Percent;

        var text =
            percent.HasValue
                ? $"{_fuel.Kind} {percent.Value:0}%"
                : _fuel.ContentLiters.HasValue
                    ? $"{_fuel.Kind} {_fuel.ContentLiters.Value:0.0} L"
                    : $"{_fuel.Kind} N/D";

        if (_fuel.ReturnToGarageRecommended)
        {
            text +=
                "  ·  RETORNAR À GARAGEM";
        }

        using var brush =
            new SolidBrush(
                _fuel.ReturnToGarageRecommended
                    ? Color.FromArgb(
                        235,
                        92,
                        80)
                    : Color.FromArgb(
                        212,
                        218,
                        223));

        graphics.DrawString(
            text,
            font,
            brush,
            8.0f,
            y);
    }

    private static RoadSegment[] BuildRoadSegments(
        IReadOnlyList<RuntimeSplineInfo> splines)
    {
        var result =
            new List<RoadSegment>();

        foreach (var spline in
                 splines)
        {
            if (spline.LengthMeters <=
                0.01)
            {
                continue;
            }

            var steps =
                Math.Clamp(
                    (int)Math.Ceiling(
                        spline.LengthMeters /
                        15.0),
                    1,
                    64);

            var previous =
                EvaluateCenter(
                    spline,
                    0.0);

            for (var index = 1;
                 index <= steps;
                 index++)
            {
                var distance =
                    spline.LengthMeters *
                    index /
                    steps;

                var current =
                    EvaluateCenter(
                        spline,
                        distance);

                result.Add(
                    new RoadSegment(
                        previous,
                        current,
                        (
                            previous +
                            current
                        ) /
                        2.0f));

                previous =
                    current;
            }
        }

        return result.ToArray();
    }

    private static Vector2 EvaluateCenter(
        RuntimeSplineInfo spline,
        double distance)
    {
        var clamped =
            Math.Clamp(
                distance,
                0.0,
                spline.LengthMeters);

        var yaw =
            spline.HeadingDegrees *
            Math.PI /
            180.0;

        var curved =
            Math.Abs(
                spline.RadiusMeters) >
            0.001;

        var angle =
            curved
                ? clamped /
                  spline.RadiusMeters
                : 0.0;

        var localX =
            curved
                ? spline.RadiusMeters *
                  (
                      1.0 -
                      Math.Cos(
                          angle)
                  )
                : 0.0;

        var localZ =
            curved
                ? spline.RadiusMeters *
                  Math.Sin(
                      angle)
                : clamped;

        var cosYaw =
            Math.Cos(
                yaw);
        var sinYaw =
            Math.Sin(
                yaw);

        var startX =
            spline.TileX *
            300.0 +
            spline.X;
        var startZ =
            spline.TileY *
            300.0 +
            spline.Z;

        return new Vector2(
            (float)(
                startX +
                localX *
                    cosYaw +
                localZ *
                    sinYaw),
            (float)(
                startZ -
                localX *
                    sinYaw +
                localZ *
                    cosYaw));
    }
}
