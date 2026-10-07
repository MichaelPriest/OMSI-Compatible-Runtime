using System.Drawing;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimePerformanceSnapshot(
    double Fps,
    double OnePercentLowFps,
    double FrameMilliseconds,
    double WorstFrameMilliseconds,
    double SimulationMilliseconds,
    double TrafficMilliseconds,
    double TextureUploadMilliseconds,
    double MainRenderMilliseconds,
    double MirrorMilliseconds,
    double MirrorUpdatesPerFrame,
    double VisibleMirrorsPerFrame,
    double SceneryDrawsPerFrame,
    double SplineDrawsPerFrame,
    double TerrainDrawsPerFrame,
    double TrafficDrawsPerFrame,
    double VehicleDrawsPerFrame,
    double LightDrawsPerFrame,
    double ReflectionDrawsPerFrame,
    int TrafficAgents,
    int SceneryBatches,
    double GpuTextureMegabytes,
    double ManagedMegabytes,
    int PendingTextureLoads,
    int StreamingUploadLimit,
    double StreamingBudgetMilliseconds,
    double StreamingCpuMilliseconds,
    double StreamingGpuMilliseconds,
    double StreamingSwapMilliseconds,
    int MsaaSamples,
    double SharpenStrength);

/// <summary>
/// In-game OMSI-style performance diagnostics panel. It displays the
/// renderer's real counters and timings; no synthetic benchmark data is used.
/// </summary>
internal sealed class RuntimePerformanceCenterPanel : Panel
{
    private readonly Label _frame;
    private readonly Label _cpu;
    private readonly Label _draws;
    private readonly Label _scene;
    private readonly Label _memory;
    private readonly Label _streaming;
    private readonly Label _graphics;

    public RuntimePerformanceCenterPanel()
    {
        Visible =
            false;
        Width =
            720;
        Height =
            470;
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
                    "PERFORMANCE CENTER  |  DIAGNÓSTICO DO RUNTIME",
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

        var hint =
            new Label
            {
                Left =
                    14,
                Top =
                    58,
                Width =
                    690,
                Height =
                    34,
                Text =
                    "Métricas medidas no frame real. A coleta detalhada fica ativa apenas enquanto este painel estiver aberto ou com OMSI_PROFILE=1.",
                ForeColor =
                    Color.FromArgb(
                        70,
                        76,
                        84)
            };

        Controls.Add(
            hint);

        _frame =
            Section(
                "FRAME",
                14,
                100,
                332,
                82);
        _cpu =
            Section(
                "CPU / FRAME",
                358,
                100,
                346,
                82);
        _draws =
            Section(
                "DRAW CALLS / FRAME",
                14,
                190,
                690,
                72);
        _scene =
            Section(
                "CENA",
                14,
                270,
                220,
                72);
        _memory =
            Section(
                "MEMÓRIA",
                246,
                270,
                220,
                72);
        _graphics =
            Section(
                "GRÁFICOS",
                478,
                270,
                226,
                72);
        _streaming =
            Section(
                "STREAMING",
                14,
                350,
                690,
                92);

        Controls.Add(
            _frame);
        Controls.Add(
            _cpu);
        Controls.Add(
            _draws);
        Controls.Add(
            _scene);
        Controls.Add(
            _memory);
        Controls.Add(
            _graphics);
        Controls.Add(
            _streaming);

        UpdateSnapshot(
            new RuntimePerformanceSnapshot(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0));
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
    }

    public void HidePanel() =>
        Visible =
            false;

    public void UpdateSnapshot(
        RuntimePerformanceSnapshot snapshot)
    {
        _frame.Text =
            $"FRAME\nFPS {snapshot.Fps:0.0}  |  1% {snapshot.OnePercentLowFps:0.0}\n" +
            $"{snapshot.FrameMilliseconds:0.0} ms  |  pior {snapshot.WorstFrameMilliseconds:0.0} ms";

        _cpu.Text =
            $"CPU / FRAME\nSim {snapshot.SimulationMilliseconds:0.00} ms · IA {snapshot.TrafficMilliseconds:0.00} ms\n" +
            $"Render {snapshot.MainRenderMilliseconds:0.00} · Espelhos {snapshot.MirrorMilliseconds:0.00} · Tex {snapshot.TextureUploadMilliseconds:0.00} ms";

        _draws.Text =
            $"DRAW CALLS / FRAME\n" +
            $"Cenário {snapshot.SceneryDrawsPerFrame:0.0} · Splines {snapshot.SplineDrawsPerFrame:0.0} · Terreno {snapshot.TerrainDrawsPerFrame:0.0} · " +
            $"Tráfego {snapshot.TrafficDrawsPerFrame:0.0} · Ônibus {snapshot.VehicleDrawsPerFrame:0.0} · Luzes {snapshot.LightDrawsPerFrame:0.0} · Reflexos {snapshot.ReflectionDrawsPerFrame:0.0}";

        _scene.Text =
            $"CENA\nIA {snapshot.TrafficAgents:N0} · Batches {snapshot.SceneryBatches:N0}\n" +
            $"Espelhos {snapshot.VisibleMirrorsPerFrame:0.0} vis. · {snapshot.MirrorUpdatesPerFrame:0.0} upd/f";

        _memory.Text =
            $"MEMÓRIA\nTexturas GPU {snapshot.GpuTextureMegabytes:0.0} MB\nGerenciada {snapshot.ManagedMegabytes:0.0} MB";

        _graphics.Text =
            $"GRÁFICOS\nMSAA {(snapshot.MsaaSamples >= 2 ? snapshot.MsaaSamples + "x" : "off")}\nSharp {(snapshot.SharpenStrength > 0.0001 ? snapshot.SharpenStrength.ToString("0.00") : "off")}";

        _streaming.Text =
            $"STREAMING\nFila {snapshot.PendingTextureLoads:N0} tex · {snapshot.StreamingUploadLimit}/frame · orçamento {snapshot.StreamingBudgetMilliseconds:0.0} ms\n" +
            $"Preparação CPU {snapshot.StreamingCpuMilliseconds:0.0} ms · GPU {snapshot.StreamingGpuMilliseconds:0.0} ms · Swap {snapshot.StreamingSwapMilliseconds:0.0} ms";
    }

    private static Label Section(
        string title,
        int left,
        int top,
        int width,
        int height) =>
        new()
        {
            Left =
                left,
            Top =
                top,
            Width =
                width,
            Height =
                height,
            Text =
                title,
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
            BorderStyle =
                BorderStyle.FixedSingle,
            Font =
                new Font(
                    "Consolas",
                    9.0f,
                    FontStyle.Bold),
            Padding =
                new Padding(
                    8,
                    6,
                    8,
                    4)
        };

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
}
