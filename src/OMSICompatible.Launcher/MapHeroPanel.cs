using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace OMSICompatible.Launcher;

internal sealed class MapHeroPanel : Panel
{
    private Image? _image;
    private string _title =
        "OMSI Compatible Runtime";
    private string _description =
        "Selecione um mapa para iniciar.";
    private string _badge =
        "RUNTIME x64 · D3D11";

    public MapHeroPanel()
    {
        DoubleBuffered = true;
        Height = 300;
        Dock = DockStyle.Top;
        Margin = Padding.Empty;
    }

    public void SetPresentation(
        MapPresentation? presentation)
    {
        var previous = _image;
        _image = null;

        if (presentation is null)
        {
            _title =
                "OMSI Compatible Runtime";
            _description =
                "Selecione um mapa para iniciar.";
        }
        else
        {
            _title = presentation.Title;
            _description =
                string.IsNullOrWhiteSpace(
                    presentation.Description)
                    ? "Mapa OMSI compatível."
                    : presentation.Description;

            if (!string.IsNullOrWhiteSpace(
                    presentation.ImagePath) &&
                File.Exists(
                    presentation.ImagePath))
            {
                try
                {
                    using var source =
                        Image.FromFile(
                            presentation.ImagePath);

                    _image =
                        new Bitmap(source);
                }
                catch
                {
                    _image = null;
                }
            }
        }

        previous?.Dispose();
        Invalidate();
    }

    protected override void OnPaint(
        PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode =
            SmoothingMode.AntiAlias;

        e.Graphics.TextRenderingHint =
            TextRenderingHint.ClearTypeGridFit;

        var bounds = ClientRectangle;

        if (_image is not null)
        {
            DrawCoverImage(
                e.Graphics,
                _image,
                bounds);
        }
        else
        {
            using var background =
                new LinearGradientBrush(
                    bounds,
                    Color.FromArgb(
                        22,
                        31,
                        44),
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

        using var overlay =
            new LinearGradientBrush(
                bounds,
                Color.FromArgb(
                    35,
                    4,
                    8,
                    14),
                Color.FromArgb(
                    235,
                    7,
                    10,
                    15),
                LinearGradientMode.Vertical);

        e.Graphics.FillRectangle(
            overlay,
            bounds);

        using var accent =
            new SolidBrush(
                Color.FromArgb(
                    0,
                    145,
                    234));

        e.Graphics.FillRectangle(
            accent,
            30,
            26,
            6,
            44);

        using var badgeBackground =
            new SolidBrush(
                Color.FromArgb(
                    210,
                    11,
                    15,
                    21));

        var badgeRect =
            new Rectangle(
                50,
                30,
                185,
                30);

        e.Graphics.FillRoundedRectangle(
            badgeBackground,
            badgeRect,
            8);

        using var badgeFont =
            new Font(
                "Segoe UI Semibold",
                9.0f);

        TextRenderer.DrawText(
            e.Graphics,
            _badge,
            badgeFont,
            badgeRect,
            Color.FromArgb(
                120,
                200,
                255),
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter);

        using var titleFont =
            new Font(
                "Segoe UI Semibold",
                27.0f);

        TextRenderer.DrawText(
            e.Graphics,
            _title,
            titleFont,
            new Rectangle(
                30,
                bounds.Height - 120,
                Math.Max(
                    bounds.Width - 60,
                    1),
                50),
            Color.White,
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter);

        using var descriptionFont =
            new Font(
                "Segoe UI",
                10.5f);

        TextRenderer.DrawText(
            e.Graphics,
            _description,
            descriptionFont,
            new Rectangle(
                32,
                bounds.Height - 72,
                Math.Max(
                    bounds.Width - 64,
                    1),
                54),
            Color.FromArgb(
                205,
                215,
                226),
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.WordBreak);
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

        var x =
            destination.X +
            (destination.Width - width) /
            2.0f;
        var y =
            destination.Y +
            (destination.Height - height) /
            2.0f;

        graphics.DrawImage(
            image,
            x,
            y,
            width,
            height);
    }

    private static void DrawFallbackBus(
        Graphics graphics,
        Rectangle bounds)
    {
        var busWidth =
            Math.Min(
                bounds.Width * 0.62f,
                620.0f);

        var busHeight =
            busWidth * 0.28f;

        var x =
            bounds.Width -
            busWidth -
            20.0f;

        var y =
            bounds.Height -
            busHeight -
            24.0f;

        using var body =
            new SolidBrush(
                Color.FromArgb(
                    60,
                    82,
                    110));

        using var window =
            new SolidBrush(
                Color.FromArgb(
                    20,
                    31,
                    46));

        using var wheel =
            new SolidBrush(
                Color.FromArgb(
                    8,
                    10,
                    13));

        graphics.FillRoundedRectangle(
            body,
            new RectangleF(
                x,
                y,
                busWidth,
                busHeight),
            18);

        graphics.FillRectangle(
            window,
            x + busWidth * 0.08f,
            y + busHeight * 0.15f,
            busWidth * 0.78f,
            busHeight * 0.33f);

        var wheelSize =
            busHeight * 0.34f;

        graphics.FillEllipse(
            wheel,
            x + busWidth * 0.15f,
            y + busHeight - wheelSize * 0.62f,
            wheelSize,
            wheelSize);

        graphics.FillEllipse(
            wheel,
            x + busWidth * 0.72f,
            y + busHeight - wheelSize * 0.62f,
            wheelSize,
            wheelSize);
    }

    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            _image?.Dispose();
            _image = null;
        }

        base.Dispose(disposing);
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(
        this Graphics graphics,
        Brush brush,
        Rectangle rectangle,
        int radius)
    {
        graphics.FillRoundedRectangle(
            brush,
            new RectangleF(
                rectangle.X,
                rectangle.Y,
                rectangle.Width,
                rectangle.Height),
            radius);
    }

    public static void FillRoundedRectangle(
        this Graphics graphics,
        Brush brush,
        RectangleF rectangle,
        float radius)
    {
        using var path =
            new GraphicsPath();

        var diameter =
            radius * 2.0f;

        path.AddArc(
            rectangle.X,
            rectangle.Y,
            diameter,
            diameter,
            180,
            90);

        path.AddArc(
            rectangle.Right - diameter,
            rectangle.Y,
            diameter,
            diameter,
            270,
            90);

        path.AddArc(
            rectangle.Right - diameter,
            rectangle.Bottom - diameter,
            diameter,
            diameter,
            0,
            90);

        path.AddArc(
            rectangle.X,
            rectangle.Bottom - diameter,
            diameter,
            diameter,
            90,
            90);

        path.CloseFigure();

        graphics.FillPath(
            brush,
            path);
    }
}
