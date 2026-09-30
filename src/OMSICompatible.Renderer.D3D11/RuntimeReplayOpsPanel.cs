using System.Drawing;
using System.Numerics;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

/// <summary>
/// Read-only operational replay. It records local vehicle telemetry in a
/// circular buffer and never writes state back into OMSI physics/scripts.
/// </summary>
internal sealed class RuntimeReplayOpsPanel : Panel
{
    private sealed record ReplaySample(
        double TimeSeconds,
        RuntimeLocalVehicleState State);

    private sealed record ReplayMarker(
        double TimeSeconds,
        string Text);

    private const double SampleIntervalSeconds =
        0.10;
    private const double HistorySeconds =
        180.0;

    private readonly List<ReplaySample> _samples =
        [];
    private readonly List<ReplayMarker> _markers =
        [];
    private readonly TrackBar _timeline;
    private readonly Label _time;
    private readonly Label _telemetry;
    private readonly ListBox _markerList;
    private readonly Panel _map;
    private readonly Button _play;
    private readonly System.Windows.Forms.Timer _playbackTimer;
    private double _lastRecordedSeconds =
        double.NegativeInfinity;
    private int _lastCollisionCount;
    private bool _followLive =
        true;
    private bool _playing;

    public RuntimeReplayOpsPanel()
    {
        Visible =
            false;
        Width =
            780;
        Height =
            520;
        BackColor =
            Color.FromArgb(
                217,
                221,
                226);
        BorderStyle =
            BorderStyle.FixedSingle;

        var header =
            new Panel
            {
                Dock =
                    DockStyle.Top,
                Height =
                    48,
                BackColor =
                    Color.FromArgb(
                        53,
                        61,
                        71)
            };

        header.Controls.Add(
            new Label
            {
                AutoSize =
                    true,
                Left =
                    14,
                Top =
                    8,
                Text =
                    "REPLAYOPS  |  REVISÃO OPERACIONAL",
                ForeColor =
                    Color.White,
                Font =
                    new Font(
                        "Segoe UI",
                        13.0f,
                        FontStyle.Bold)
            });

        var close =
            OmsiButton(
                "X",
                (_, _) =>
                    HidePanel());

        close.SetBounds(
            Width - 52,
            8,
            38,
            30);
        close.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Right;

        header.Controls.Add(
            close);
        Controls.Add(
            header);

        _map =
            new Panel
            {
                Left =
                    14,
                Top =
                    60,
                Width =
                    500,
                Height =
                    270,
                BackColor =
                    Color.FromArgb(
                        239,
                        241,
                        244),
                BorderStyle =
                    BorderStyle.FixedSingle
            };

        _map.Paint +=
            PaintReplayMap;

        Controls.Add(
            _map);

        var markerGroup =
            new GroupBox
            {
                Left =
                    526,
                Top =
                    60,
                Width =
                    238,
                Height =
                    270,
                Text =
                    "Ocorrências",
                Font =
                    new Font(
                        "Segoe UI",
                        9.0f,
                        FontStyle.Bold)
            };

        _markerList =
            new ListBox
            {
                Left =
                    10,
                Top =
                    24,
                Width =
                    216,
                Height =
                    232,
                IntegralHeight =
                    false,
                BackColor =
                    Color.FromArgb(
                        246,
                        247,
                        249),
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f)
            };

        _markerList.DoubleClick +=
            (_, _) =>
                JumpToSelectedMarker();

        markerGroup.Controls.Add(
            _markerList);
        Controls.Add(
            markerGroup);

        _timeline =
            new TrackBar
            {
                Left =
                    14,
                Top =
                    340,
                Width =
                    750,
                Height =
                    42,
                Minimum =
                    0,
                Maximum =
                    0,
                TickStyle =
                    TickStyle.None
            };

        _timeline.Scroll +=
            (_, _) =>
            {
                _followLive =
                    _timeline.Value >=
                    _timeline.Maximum;
                _playing =
                    false;
                _play.Text =
                    "PLAY";
                UpdateSelectedState();
            };

        Controls.Add(
            _timeline);

        _time =
            new Label
            {
                Left =
                    14,
                Top =
                    383,
                Width =
                    250,
                Height =
                    22,
                Text =
                    "SEM DADOS",
                Font =
                    new Font(
                        "Segoe UI",
                        9.0f,
                        FontStyle.Bold)
            };

        Controls.Add(
            _time);

        var back =
            OmsiButton(
                "-10 s",
                (_, _) =>
                    MoveSeconds(
                        -10.0));
        back.SetBounds(
            275,
            379,
            80,
            30);

        _play =
            OmsiButton(
                "PLAY",
                (_, _) =>
                    TogglePlayback());
        _play.SetBounds(
            362,
            379,
            80,
            30);

        var forward =
            OmsiButton(
                "+10 s",
                (_, _) =>
                    MoveSeconds(
                        10.0));
        forward.SetBounds(
            449,
            379,
            80,
            30);

        var live =
            OmsiButton(
                "LIVE",
                (_, _) =>
                    GoLive());
        live.SetBounds(
            536,
            379,
            80,
            30);

        Controls.Add(
            back);
        Controls.Add(
            _play);
        Controls.Add(
            forward);
        Controls.Add(
            live);

        _telemetry =
            new Label
            {
                Left =
                    14,
                Top =
                    420,
                Width =
                    750,
                Height =
                    78,
                BackColor =
                    Color.FromArgb(
                        239,
                        241,
                        244),
                BorderStyle =
                    BorderStyle.FixedSingle,
                Padding =
                    new Padding(
                        8,
                        5,
                        8,
                        4),
                Font =
                    new Font(
                        "Consolas",
                        9.0f,
                        FontStyle.Bold),
                Text =
                    "Aguardando telemetria do ônibus."
            };

        Controls.Add(
            _telemetry);

        _playbackTimer =
            new System.Windows.Forms.Timer
            {
                Interval =
                    100
            };

        _playbackTimer.Tick +=
            (_, _) =>
                AdvancePlayback();
        _playbackTimer.Start();
    }

    public void Record(
        double nowSeconds,
        RuntimeLocalVehicleState state,
        int collisionCount)
    {
        if (!double.IsFinite(
                nowSeconds))
        {
            return;
        }

        if (collisionCount >
            _lastCollisionCount)
        {
            AddMarker(
                "COLISÃO DETECTADA",
                nowSeconds);
        }

        _lastCollisionCount =
            Math.Max(
                _lastCollisionCount,
                collisionCount);

        if (nowSeconds -
                _lastRecordedSeconds <
            SampleIntervalSeconds)
        {
            return;
        }

        _lastRecordedSeconds =
            nowSeconds;

        _samples.Add(
            new ReplaySample(
                nowSeconds,
                state));

        var minimumTime =
            nowSeconds -
            HistorySeconds;

        while (_samples.Count >
                   0 &&
               _samples[0].TimeSeconds <
                   minimumTime)
        {
            _samples.RemoveAt(
                0);

            if (!_followLive &&
                _timeline.Value >
                    0)
            {
                _timeline.Value--;
            }
        }

        _markers.RemoveAll(
            marker =>
                marker.TimeSeconds <
                minimumTime);

        RefreshTimelineBounds();

        if (_followLive)
        {
            _timeline.Value =
                _timeline.Maximum;
        }

        if (Visible)
        {
            UpdateMarkerList();
            UpdateSelectedState();
        }
    }

    public void AddMarker(
        string text,
        double nowSeconds)
    {
        var safe =
            string.IsNullOrWhiteSpace(
                text)
                ? "OCORRÊNCIA"
                : text.Trim();

        if (safe.Length >
            96)
        {
            safe =
                safe[..96];
        }

        _markers.Add(
            new ReplayMarker(
                nowSeconds,
                safe));

        if (_markers.Count >
            64)
        {
            _markers.RemoveAt(
                0);
        }

        if (Visible)
        {
            UpdateMarkerList();
        }
    }

    public void TogglePanel()
    {
        if (Visible)
        {
            HidePanel();
        }
        else
        {
            ShowPanel();
        }
    }

    public void ShowPanel()
    {
        Visible =
            true;
        BringToFront();
        RefreshTimelineBounds();
        UpdateMarkerList();
        UpdateSelectedState();
    }

    public void HidePanel()
    {
        Visible =
            false;
        _playing =
            false;
        _play.Text =
            "PLAY";
    }

    private void RefreshTimelineBounds()
    {
        var maximum =
            Math.Max(
                0,
                _samples.Count - 1);

        _timeline.Maximum =
            maximum;

        if (_timeline.Value >
            maximum)
        {
            _timeline.Value =
                maximum;
        }
    }

    private void UpdateSelectedState()
    {
        if (_samples.Count ==
            0)
        {
            _time.Text =
                "SEM DADOS";
            _telemetry.Text =
                "Aguardando telemetria do ônibus.";
            _map.Invalidate();
            return;
        }

        var index =
            Math.Clamp(
                _timeline.Value,
                0,
                _samples.Count - 1);

        var sample =
            _samples[index];
        var latest =
            _samples[^1];
        var age =
            Math.Max(
                0.0,
                latest.TimeSeconds -
                    sample.TimeSeconds);
        var state =
            sample.State;

        _time.Text =
            _followLive
                ? "LIVE"
                : $"-{age:0.0} s";

        var gear =
            state.Gear switch
            {
                > 1 =>
                    "D",
                < 1 =>
                    "R",
                _ =>
                    "N"
            };

        _telemetry.Text =
            $"Vel {Math.Abs(state.SpeedMetersPerSecond) * 3.6f:0.0} km/h · Direção {state.SteeringAngleRadians * 180.0 / Math.PI:0.0}° · Marcha {gear}\n" +
            $"Acel {state.AcceleratorLevel * 100.0f:0}% · Freio {state.BrakeLevel * 100.0f:0}% · Elétrica {(state.ElectricalSystemEnabled ? "ON" : "OFF")} · Motor {(state.EngineRunning ? "ON" : "OFF")}\n" +
            $"Pos {state.Position.X:0.0}, {state.Position.Y:0.0}, {state.Position.Z:0.0} · Heading {state.HeadingRadians * 180.0 / Math.PI:0.0}°";

        _map.Invalidate();
    }

    private void UpdateMarkerList()
    {
        _markerList.BeginUpdate();
        _markerList.Items.Clear();

        if (_samples.Count >
            0)
        {
            var latest =
                _samples[^1]
                    .TimeSeconds;

            foreach (var marker in
                     _markers
                         .OrderByDescending(
                             static marker =>
                                 marker.TimeSeconds))
            {
                var age =
                    Math.Max(
                        0.0,
                        latest -
                            marker.TimeSeconds);

                _markerList.Items.Add(
                    $"{age,5:0.0}s  {marker.Text}");
            }
        }

        _markerList.EndUpdate();
    }

    private void JumpToSelectedMarker()
    {
        if (_markerList.SelectedIndex <
                0 ||
            _samples.Count ==
                0)
        {
            return;
        }

        var ordered =
            _markers
                .OrderByDescending(
                    static marker =>
                        marker.TimeSeconds)
                .ToArray();

        if (_markerList.SelectedIndex >=
            ordered.Length)
        {
            return;
        }

        var target =
            ordered[
                _markerList.SelectedIndex];

        var nearestIndex =
            0;
        var nearestDistance =
            double.MaxValue;

        for (var index = 0;
             index < _samples.Count;
             index++)
        {
            var distance =
                Math.Abs(
                    _samples[index].TimeSeconds -
                    target.TimeSeconds);

            if (distance <
                nearestDistance)
            {
                nearestDistance =
                    distance;
                nearestIndex =
                    index;
            }
        }

        _followLive =
            false;
        _playing =
            false;
        _play.Text =
            "PLAY";
        _timeline.Value =
            nearestIndex;
        UpdateSelectedState();
    }

    private void MoveSeconds(
        double seconds)
    {
        if (_samples.Count ==
            0)
        {
            return;
        }

        var current =
            _samples[
                Math.Clamp(
                    _timeline.Value,
                    0,
                    _samples.Count - 1)]
                .TimeSeconds;

        var target =
            current +
            seconds;

        var index =
            0;
        var best =
            double.MaxValue;

        for (var sampleIndex = 0;
             sampleIndex < _samples.Count;
             sampleIndex++)
        {
            var distance =
                Math.Abs(
                    _samples[sampleIndex].TimeSeconds -
                    target);

            if (distance <
                best)
            {
                best =
                    distance;
                index =
                    sampleIndex;
            }
        }

        _followLive =
            index ==
            _samples.Count - 1;
        _playing =
            false;
        _play.Text =
            "PLAY";
        _timeline.Value =
            index;
        UpdateSelectedState();
    }

    private void TogglePlayback()
    {
        if (_samples.Count <
            2)
        {
            return;
        }

        if (_followLive)
        {
            _followLive =
                false;
            _timeline.Value =
                Math.Max(
                    0,
                    _samples.Count - 100);
        }

        _playing =
            !_playing;
        _play.Text =
            _playing
                ? "PAUSE"
                : "PLAY";
    }

    private void AdvancePlayback()
    {
        if (!_playing ||
            !Visible ||
            _samples.Count ==
                0)
        {
            return;
        }

        if (_timeline.Value >=
            _timeline.Maximum)
        {
            _playing =
                false;
            _followLive =
                true;
            _play.Text =
                "PLAY";
            UpdateSelectedState();
            return;
        }

        _timeline.Value =
            Math.Min(
                _timeline.Maximum,
                _timeline.Value + 1);

        UpdateSelectedState();
    }

    private void GoLive()
    {
        _followLive =
            true;
        _playing =
            false;
        _play.Text =
            "PLAY";

        if (_samples.Count >
            0)
        {
            _timeline.Value =
                _timeline.Maximum;
        }

        UpdateSelectedState();
    }

    private void PaintReplayMap(
        object? sender,
        PaintEventArgs e)
    {
        e.Graphics.Clear(
            Color.FromArgb(
                239,
                241,
                244));

        if (_samples.Count <
            2)
        {
            DrawCentered(
                e.Graphics,
                "SEM TRAJETÓRIA",
                _map.ClientRectangle);
            return;
        }

        var selectedIndex =
            Math.Clamp(
                _timeline.Value,
                0,
                _samples.Count - 1);
        var startIndex =
            Math.Max(
                0,
                selectedIndex - 600);
        var segment =
            _samples
                .Skip(
                    startIndex)
                .Take(
                    selectedIndex -
                    startIndex +
                    1)
                .ToArray();

        if (segment.Length <
            2)
        {
            return;
        }

        var minX =
            segment.Min(
                static item =>
                    item.State.Position.X);
        var maxX =
            segment.Max(
                static item =>
                    item.State.Position.X);
        var minZ =
            segment.Min(
                static item =>
                    item.State.Position.Z);
        var maxZ =
            segment.Max(
                static item =>
                    item.State.Position.Z);

        var rangeX =
            Math.Max(
                maxX -
                minX,
                5.0f);
        var rangeZ =
            Math.Max(
                maxZ -
                minZ,
                5.0f);

        var scale =
            Math.Min(
                (_map.ClientSize.Width -
                 30.0f) /
                rangeX,
                (_map.ClientSize.Height -
                 30.0f) /
                rangeZ);

        PointF Map(
            Vector3 position) =>
            new(
                15.0f +
                (position.X -
                 minX) *
                scale,
                _map.ClientSize.Height -
                15.0f -
                (position.Z -
                 minZ) *
                scale);

        using var routePen =
            new Pen(
                Color.FromArgb(
                    65,
                    107,
                    143),
                2.0f);

        for (var index = 1;
             index < segment.Length;
             index++)
        {
            e.Graphics.DrawLine(
                routePen,
                Map(
                    segment[index - 1]
                        .State
                        .Position),
                Map(
                    segment[index]
                        .State
                        .Position));
        }

        var selectedPoint =
            Map(
                _samples[selectedIndex]
                    .State
                    .Position);

        using var selectedBrush =
            new SolidBrush(
                Color.FromArgb(
                    180,
                    48,
                    42));

        e.Graphics.FillEllipse(
            selectedBrush,
            selectedPoint.X -
                5.0f,
            selectedPoint.Y -
                5.0f,
            10.0f,
            10.0f);
    }

    private static void DrawCentered(
        Graphics graphics,
        string text,
        Rectangle bounds)
    {
        using var font =
            new Font(
                "Segoe UI",
                9.0f,
                FontStyle.Bold);
        using var brush =
            new SolidBrush(
                Color.FromArgb(
                    105,
                    111,
                    119));

        var size =
            graphics.MeasureString(
                text,
                font);

        graphics.DrawString(
            text,
            font,
            brush,
            bounds.Left +
                (bounds.Width -
                 size.Width) /
                2.0f,
            bounds.Top +
                (bounds.Height -
                 size.Height) /
                2.0f);
    }

    private static Button OmsiButton(
        string text,
        EventHandler handler)
    {
        var button =
            new Button
            {
                Text =
                    text,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        239,
                        241,
                        244),
                ForeColor =
                    Color.FromArgb(
                        31,
                        36,
                        43),
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f,
                        FontStyle.Bold),
                Cursor =
                    Cursors.Hand
            };

        button.FlatAppearance.BorderColor =
            Color.FromArgb(
                145,
                151,
                160);
        button.Click +=
            handler;

        return button;
    }

    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            _playbackTimer.Stop();
            _playbackTimer.Dispose();
        }

        base.Dispose(
            disposing);
    }
}
