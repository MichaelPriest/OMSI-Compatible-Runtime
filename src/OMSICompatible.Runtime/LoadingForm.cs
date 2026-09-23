using System.Drawing.Drawing2D;
using OMSICompatible.World;

namespace OMSICompatible.Runtime;

internal sealed class LoadingForm : Form
{
    private readonly Label _mapLabel = new();
    private readonly Label _stageLabel = new();
    private readonly Label _detailLabel = new();
    private readonly Label _percentLabel = new();
    private readonly Panel _progressTrack = new();
    private readonly Panel _progressFill = new();

    private Image? _backgroundImage;
    private int _percent;

    public LoadingForm(
        string mapName,
        string? imagePath)
    {
        Text = "OMSI Compatible Runtime";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(960, 540);
        MinimumSize = new Size(800, 450);
        BackColor = Color.FromArgb(8, 11, 16);
        ForeColor = Color.White;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
        UpdateStyles();

        TryLoadImage(imagePath);
        BuildInterface(mapName);
    }

    public void UpdateProgress(
        WorldLoadProgress progress)
    {
        _percent = Math.Clamp(
            progress.Percent,
            0,
            100);

        _stageLabel.Text =
            progress.Stage;

        _detailLabel.Text =
            progress.Detail;

        _percentLabel.Text =
            $"{_percent}%";

        UpdateProgressFill();

        // Labels and progress panels invalidate their own regions.
        // Repainting the whole form on every progress event caused the
        // background to flash black on slower loads.
    }

    public void SetStage(
        int percent,
        string stage,
        string detail)
    {
        UpdateProgress(
            new WorldLoadProgress(
                percent,
                stage,
                detail));
    }

    public void ShowFailure(
        string message)
    {
        _stageLabel.Text =
            "Não foi possível iniciar";

        _detailLabel.Text =
            message;

        _percentLabel.Text =
            "ERRO";

        _progressFill.Width = 0;

        var close =
            new Button
            {
                Text = "FECHAR",
                Width = 130,
                Height = 40,
                Anchor =
                    AnchorStyles.Right |
                    AnchorStyles.Bottom,
                Left =
                    ClientSize.Width - 162,
                Top =
                    ClientSize.Height - 64,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        180,
                        45,
                        45),
                ForeColor =
                    Color.White
            };

        close.FlatAppearance.BorderSize = 0;
        close.Click +=
            (_, _) => Close();

        Controls.Add(close);
        close.BringToFront();
    }

    private void BuildInterface(
        string mapName)
    {
        var footer =
            new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 178,
                BackColor =
                    Color.FromArgb(
                        215,
                        8,
                        11,
                        16),
                Padding =
                    new Padding(
                        34,
                        22,
                        34,
                        22)
            };

        var runtimeBadge =
            new Label
            {
                Text =
                    "OMSI COMPATIBLE RUNTIME  ·  x64",
                AutoSize = true,
                ForeColor =
                    Color.FromArgb(
                        105,
                        198,
                        255),
                Font =
                    new Font(
                        "Segoe UI Semibold",
                        9.0f),
                Location =
                    new Point(
                        34,
                        20)
            };

        _mapLabel.Text = mapName;
        _mapLabel.AutoEllipsis = true;
        _mapLabel.ForeColor = Color.White;
        _mapLabel.Font =
            new Font(
                "Segoe UI Semibold",
                21.0f);
        _mapLabel.Location =
            new Point(
                34,
                44);
        _mapLabel.Size =
            new Size(
                700,
                42);
        _mapLabel.Anchor =
            AnchorStyles.Left |
            AnchorStyles.Right |
            AnchorStyles.Top;

        _stageLabel.Text =
            "Preparando...";
        _stageLabel.AutoEllipsis = true;
        _stageLabel.ForeColor =
            Color.FromArgb(
                224,
                230,
                238);
        _stageLabel.Font =
            new Font(
                "Segoe UI Semibold",
                11.0f);
        _stageLabel.Location =
            new Point(
                36,
                92);
        _stageLabel.Size =
            new Size(
                520,
                26);

        _detailLabel.Text =
            "Inicializando runtime x64...";
        _detailLabel.AutoEllipsis = true;
        _detailLabel.ForeColor =
            Color.FromArgb(
                145,
                158,
                175);
        _detailLabel.Font =
            new Font(
                "Segoe UI",
                9.5f);
        _detailLabel.Location =
            new Point(
                36,
                119);
        _detailLabel.Size =
            new Size(
                690,
                24);

        _percentLabel.Text = "0%";
        _percentLabel.TextAlign =
            ContentAlignment.MiddleRight;
        _percentLabel.ForeColor =
            Color.White;
        _percentLabel.Font =
            new Font(
                "Segoe UI Semibold",
                12.0f);
        _percentLabel.Size =
            new Size(
                90,
                28);
        _percentLabel.Location =
            new Point(
                820,
                94);
        _percentLabel.Anchor =
            AnchorStyles.Right |
            AnchorStyles.Top;

        _progressTrack.Height = 5;
        _progressTrack.BackColor =
            Color.FromArgb(
                50,
                59,
                72);
        _progressTrack.Location =
            new Point(
                36,
                151);
        _progressTrack.Size =
            new Size(
                888,
                5);
        _progressTrack.Anchor =
            AnchorStyles.Left |
            AnchorStyles.Right |
            AnchorStyles.Bottom;

        _progressFill.Dock =
            DockStyle.Left;
        _progressFill.BackColor =
            Color.FromArgb(
                0,
                145,
                234);
        _progressFill.Width = 0;

        _progressTrack.Controls.Add(
            _progressFill);

        footer.Controls.Add(
            runtimeBadge);
        footer.Controls.Add(
            _mapLabel);
        footer.Controls.Add(
            _stageLabel);
        footer.Controls.Add(
            _detailLabel);
        footer.Controls.Add(
            _percentLabel);
        footer.Controls.Add(
            _progressTrack);

        Controls.Add(footer);

        footer.Resize +=
            (_, _) =>
            {
                _mapLabel.Width =
                    Math.Max(
                        footer.ClientSize.Width - 220,
                        100);

                _detailLabel.Width =
                    Math.Max(
                        footer.ClientSize.Width - 250,
                        100);

                _percentLabel.Left =
                    footer.ClientSize.Width -
                    _percentLabel.Width -
                    34;

                _progressTrack.Width =
                    Math.Max(
                        footer.ClientSize.Width - 72,
                        40);

                UpdateProgressFill();
            };
    }

    private void UpdateProgressFill()
    {
        var width =
            (int)Math.Round(
                _progressTrack.ClientSize.Width *
                (_percent / 100.0));

        _progressFill.Width =
            Math.Clamp(
                width,
                0,
                Math.Max(
                    _progressTrack.ClientSize.Width,
                    0));
    }

    private void TryLoadImage(
        string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(
                imagePath) ||
            !File.Exists(imagePath))
        {
            return;
        }

        try
        {
            using var source =
                Image.FromFile(
                    imagePath);

            _backgroundImage =
                new Bitmap(source);
        }
        catch
        {
            _backgroundImage = null;
        }
    }

    protected override void OnPaintBackground(
        PaintEventArgs e)
    {
        var bounds = ClientRectangle;

        if (_backgroundImage is not null)
        {
            DrawCoverImage(
                e.Graphics,
                _backgroundImage,
                bounds);
        }
        else
        {
            using var background =
                new LinearGradientBrush(
                    bounds,
                    Color.FromArgb(
                        23,
                        34,
                        48),
                    Color.FromArgb(
                        6,
                        9,
                        14),
                    18.0f);

            e.Graphics.FillRectangle(
                background,
                bounds);

            DrawFallbackBus(
                e.Graphics,
                bounds);
        }

        using var shade =
            new LinearGradientBrush(
                bounds,
                Color.FromArgb(
                    28,
                    0,
                    0,
                    0),
                Color.FromArgb(
                    180,
                    0,
                    0,
                    0),
                LinearGradientMode.Vertical);

        e.Graphics.FillRectangle(
            shade,
            bounds);
    }

    private static void DrawCoverImage(
        Graphics graphics,
        Image image,
        Rectangle destination)
    {
        var scale =
            Math.Max(
                destination.Width /
                (float)image.Width,
                destination.Height /
                (float)image.Height);

        var width =
            image.Width * scale;
        var height =
            image.Height * scale;

        graphics.DrawImage(
            image,
            destination.X +
            (destination.Width - width) /
            2.0f,
            destination.Y +
            (destination.Height - height) /
            2.0f,
            width,
            height);
    }

    private static void DrawFallbackBus(
        Graphics graphics,
        Rectangle bounds)
    {
        var body =
            new RectangleF(
                bounds.Width * 0.20f,
                bounds.Height * 0.30f,
                bounds.Width * 0.64f,
                bounds.Height * 0.25f);

        using var bodyBrush =
            new SolidBrush(
                Color.FromArgb(
                    55,
                    80,
                    110));

        using var glassBrush =
            new SolidBrush(
                Color.FromArgb(
                    16,
                    25,
                    39));

        using var wheelBrush =
            new SolidBrush(
                Color.FromArgb(
                    7,
                    9,
                    12));

        graphics.FillRectangle(
            bodyBrush,
            body);

        graphics.FillRectangle(
            glassBrush,
            body.X +
            body.Width * 0.08f,
            body.Y +
            body.Height * 0.12f,
            body.Width * 0.78f,
            body.Height * 0.34f);

        var wheel =
            body.Height * 0.28f;

        graphics.FillEllipse(
            wheelBrush,
            body.X +
            body.Width * 0.16f,
            body.Bottom -
            wheel * 0.55f,
            wheel,
            wheel);

        graphics.FillEllipse(
            wheelBrush,
            body.X +
            body.Width * 0.72f,
            body.Bottom -
            wheel * 0.55f,
            wheel,
            wheel);
    }

    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            _backgroundImage?.Dispose();
            _backgroundImage = null;
        }

        base.Dispose(disposing);
    }
}
