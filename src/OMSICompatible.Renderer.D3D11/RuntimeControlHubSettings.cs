using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

/// <summary>
/// Presentation preferences only. CCO authorization continues to be checked
/// against DriverPass roles; these switches do not grant dispatch privileges.
/// </summary>
internal sealed record RuntimeControlHubSettings
{
    public bool DriveOpsEnabled { get; init; } = true;
    public bool ControlHubEnabled { get; init; } = true;
    public bool SemiTransparent { get; init; } = true;
    public int OpacityPercent { get; init; } = 84;

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "OMSI-Compatible-Runtime",
            "controlhub-display.json");

    public static RuntimeControlHubSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return (JsonSerializer.Deserialize<RuntimeControlHubSettings>(
                    File.ReadAllText(SettingsPath)) ??
                    new RuntimeControlHubSettings()).Normalized();
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine(
                $"[cco-display] Could not read preferences: {ex.Message}");
        }

        return new RuntimeControlHubSettings();
    }

    public RuntimeControlHubSettings Normalized() =>
        this with
        {
            OpacityPercent = Math.Clamp(OpacityPercent, 60, 100)
        };

    public double WindowOpacity =>
        SemiTransparent
            ? Math.Clamp(OpacityPercent, 60, 100) / 100.0
            : 1.0;

    public void Save()
    {
        try
        {
            var path = SettingsPath;
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            try
            {
                File.WriteAllText(
                    temporary,
                    JsonSerializer.Serialize(
                        Normalized(),
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        }));
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"[cco-display] Could not save preferences: {ex.Message}");
        }
    }
}

/// <summary>
/// Built-in screen reachable via OMSI's Options button. Preferences take
/// effect immediately, including on the real top-level DriveOps overlay.
/// </summary>
internal sealed class RuntimeControlHubSettingsForm : Form
{
    public event Action<RuntimeControlHubSettings>? PreferencesChanged;

    public RuntimeControlHubSettingsForm(RuntimeControlHubSettings initial)
    {
        Text = "Telas e CCO | OMSI Runtime";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 370);
        BackColor = Color.FromArgb(18, 29, 44);
        ForeColor = Color.FromArgb(225, 235, 247);
        Font = new Font("Segoe UI", 10.0f);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };

        var heading = new Label
        {
            Text = "CENTRAL OPERACIONAL · CONTROLE DE TELAS",
            ForeColor = Color.FromArgb(146, 190, 244),
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            Location = new Point(22, 20),
            Size = new Size(475, 32)
        };
        var description = new Label
        {
            Text = "Ative os módulos que deseja usar e ajuste a transparência\n" +
                   "do DriveOps sobre o mapa. As permissões CCO não são alteradas.",
            Location = new Point(22, 55),
            Size = new Size(475, 48)
        };

        var driveOps = new CheckBox
        {
            Text = "Permitir abrir a central DriveOps no jogo",
            Checked = initial.DriveOpsEnabled,
            Location = new Point(24, 112),
            AutoSize = true
        };
        var controlHub = new CheckBox
        {
            Text = "Ativar módulo CONTROLHUB / CCO",
            Checked = initial.ControlHubEnabled,
            Location = new Point(24, 152),
            AutoSize = true
        };
        var semiTransparent = new CheckBox
        {
            Text = "Janela semitransparente sobre a simulação",
            Checked = initial.SemiTransparent,
            Location = new Point(24, 192),
            AutoSize = true
        };
        var opacityLabel = new Label
        {
            Text = $"Opacidade: {Math.Clamp(initial.OpacityPercent, 60, 100)}%",
            Location = new Point(24, 235),
            Size = new Size(250, 25)
        };
        var opacity = new TrackBar
        {
            Minimum = 60,
            Maximum = 100,
            TickFrequency = 10,
            SmallChange = 5,
            LargeChange = 10,
            Value = Math.Clamp(initial.OpacityPercent, 60, 100),
            Location = new Point(22, 260),
            Size = new Size(460, 45),
            Enabled = initial.SemiTransparent
        };
        var status = new Label
        {
            Text = "Salvo automaticamente nesta instalação.",
            ForeColor = Color.FromArgb(150, 175, 196),
            Location = new Point(22, 315),
            Size = new Size(400, 30)
        };

        void Apply()
        {
            opacity.Enabled = semiTransparent.Checked;
            opacityLabel.Text = $"Opacidade: {opacity.Value}%";

            var updated = initial with
            {
                DriveOpsEnabled = driveOps.Checked,
                ControlHubEnabled = controlHub.Checked,
                SemiTransparent = semiTransparent.Checked,
                OpacityPercent = opacity.Value
            };

            updated.Save();
            PreferencesChanged?.Invoke(updated);
        }

        driveOps.CheckedChanged += (_, _) => Apply();
        controlHub.CheckedChanged += (_, _) => Apply();
        semiTransparent.CheckedChanged += (_, _) => Apply();
        opacity.ValueChanged += (_, _) => Apply();

        Controls.AddRange(
        [
            heading,
            description,
            driveOps,
            controlHub,
            semiTransparent,
            opacityLabel,
            opacity,
            status
        ]);
    }
}
