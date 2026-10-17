using System.Drawing;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimeBusSelectionInfo(
    string RelativePath,
    string DisplayName,
    string GroupName,
    string DirectoryPath,
    IReadOnlyList<string> HofFiles);

internal sealed class RuntimeBusSelectorPanel : Panel
{
    private readonly ComboBox _groupBox;
    private readonly ComboBox _busBox;
    private readonly ComboBox _hofBox;
    private readonly Label _details;
    private readonly IReadOnlyList<RuntimeBusSelectionInfo> _buses;

    public RuntimeBusSelectorPanel(
        string contentRoot)
    {
        _buses =
            DiscoverBuses(
                contentRoot);

        Visible = false;
        Width = 560;
        Height = 340;
        BackColor = Color.FromArgb(13, 20, 30);
        ForeColor = Color.FromArgb(232, 240, 248);
        BorderStyle =
            BorderStyle.FixedSingle;
        Padding =
            new Padding(
                14);
        TabStop = true;

        var title =
            new Label
            {
                Text = "GARAGEM  /  SELECIONAR VEÍCULO",
                ForeColor = Color.FromArgb(125, 211, 252),
                Font = new Font("Segoe UI Semibold", 13.0f, FontStyle.Bold),
                AutoSize = true,
                Left = 18,
                Top = 14
            };

        var preview =
            new Label
            {
                Text = "\uE7C5",
                Font = new Font(
                    "Segoe MDL2 Assets",
                    46.0f,
                    FontStyle.Regular),
                TextAlign =
                    ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(24, 39, 55),
                ForeColor = Color.FromArgb(56, 189, 248),
                Left = 18,
                Top = 52,
                Width = 160,
                Height = 128
            };

        _groupBox =
            new ComboBox
            {
                DropDownStyle =
                    ComboBoxStyle.DropDownList,
                Left = 198,
                Top = 58,
                Width = 336,
                Height = 30,
                BackColor = Color.FromArgb(24, 35, 49),
                ForeColor = Color.FromArgb(238, 244, 250)
            };

        _busBox =
            new ComboBox
            {
                DropDownStyle =
                    ComboBoxStyle.DropDownList,
                Left = 198,
                Top = 105,
                Width = 336,
                Height = 30,
                BackColor = Color.FromArgb(24, 35, 49),
                ForeColor = Color.FromArgb(238, 244, 250)
            };

        _hofBox =
            new ComboBox
            {
                DropDownStyle =
                    ComboBoxStyle.DropDownList,
                Left = 198,
                Top = 152,
                Width = 336,
                Height = 30,
                BackColor = Color.FromArgb(24, 35, 49),
                ForeColor = Color.FromArgb(238, 244, 250)
            };

        _details =
            new Label
            {
                Left = 18,
                Top = 198,
                Width = 516,
                Height = 45,
                ForeColor = Color.FromArgb(153, 170, 188),
                AutoEllipsis = true
            };

        var cancel =
            new Button
            {
                Text = "VOLTAR",
                Left = 340,
                Top = 286,
                Width = 88,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 47, 64),
                ForeColor = Color.FromArgb(226, 232, 240),
                Cursor = Cursors.Hand
            };

        var ok =
            new Button
            {
                Text = "CONFIRMAR",
                Left = 436,
                Top = 286,
                Width = 98,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(8, 126, 164),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };

        cancel.FlatAppearance.BorderColor = Color.FromArgb(57, 72, 90);
        cancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(51, 65, 82);
        ok.FlatAppearance.BorderSize = 0;

        Controls.Add(
            title);
        Controls.Add(
            preview);
        Controls.Add(
            new Label
            {
                Text = "GRUPO / FABRICANTE",
                ForeColor = Color.FromArgb(145, 165, 186),
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                Left = 198,
                Top = 39,
                AutoSize = true
            });
        Controls.Add(
            _groupBox);
        Controls.Add(
            new Label
            {
                Text = "MODELO",
                ForeColor = Color.FromArgb(145, 165, 186),
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                Left = 198,
                Top = 86,
                AutoSize = true
            });
        Controls.Add(
            _busBox);
        Controls.Add(
            new Label
            {
                Text = "ARQUIVO HOF",
                ForeColor = Color.FromArgb(145, 165, 186),
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                Left = 198,
                Top = 133,
                AutoSize = true
            });
        Controls.Add(
            _hofBox);
        Controls.Add(
            _details);
        Controls.Add(
            cancel);
        Controls.Add(
            ok);

        _groupBox.SelectedIndexChanged +=
            (_, _) =>
                PopulateBuses();

        _busBox.SelectedIndexChanged +=
            (_, _) =>
                PopulateHofs();

        cancel.Click +=
            (_, _) =>
                HideSelector();

        ok.Click +=
            (_, _) =>
                ConfirmSelection();

        PopulateGroups();
    }

    public event Action<string, string?>?
        SelectionConfirmed;

    public void ShowSelector()
    {
        PopulateGroups();

        Visible = true;
        BringToFront();
        Focus();
    }

    public void HideSelector()
    {
        Visible = false;
    }

    private void PopulateGroups()
    {
        var previous =
            _groupBox.SelectedItem
                as string;

        var groups =
            _buses
                .Select(
                    static bus =>
                        bus.GroupName)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    static value =>
                        value,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        _groupBox.Items.Clear();
        _groupBox.Items.AddRange(
            groups.Cast<object>()
                .ToArray());

        var index =
            Array.FindIndex(
                groups,
                value =>
                    string.Equals(
                        value,
                        previous,
                        StringComparison.OrdinalIgnoreCase));

        _groupBox.SelectedIndex =
            index >= 0
                ? index
                : groups.Length > 0
                    ? 0
                    : -1;

        PopulateBuses();
    }

    private void PopulateBuses()
    {
        var group =
            _groupBox.SelectedItem
                as string;

        var buses =
            _buses
                .Where(
                    bus =>
                        string.Equals(
                            bus.GroupName,
                            group,
                            StringComparison.OrdinalIgnoreCase))
                .OrderBy(
                    static bus =>
                        bus.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        _busBox.DataSource = null;
        _busBox.DisplayMember =
            nameof(
                RuntimeBusSelectionInfo.DisplayName);
        _busBox.DataSource =
            buses;

        PopulateHofs();
    }

    private void PopulateHofs()
    {
        var bus =
            _busBox.SelectedItem
                as RuntimeBusSelectionInfo;

        _hofBox.DataSource = null;

        if (bus is null)
        {
            _details.Text =
                _buses.Count == 0
                    ? "Nenhum arquivo .bus encontrado em Vehicles."
                    : string.Empty;
            return;
        }

        _hofBox.DataSource =
            bus.HofFiles
                .Select(
                    Path.GetFileNameWithoutExtension)
                .ToArray();

        _details.Text =
            $"Veículo: {bus.RelativePath}\r\n" +
            $"HOF disponíveis: {bus.HofFiles.Count:N0}";
    }

    private void ConfirmSelection()
    {
        if (_busBox.SelectedItem is not
            RuntimeBusSelectionInfo bus)
        {
            return;
        }

        string? hofPath =
            null;

        if (_hofBox.SelectedIndex >= 0 &&
            _hofBox.SelectedIndex <
                bus.HofFiles.Count)
        {
            hofPath =
                bus.HofFiles[
                    _hofBox.SelectedIndex];
        }

        SelectionConfirmed?.Invoke(
            bus.RelativePath,
            hofPath);
    }

    private static IReadOnlyList<RuntimeBusSelectionInfo>
        DiscoverBuses(
            string contentRoot)
    {
        var vehicles =
            Path.Combine(
                contentRoot,
                "Vehicles");

        if (!Directory.Exists(
                vehicles))
        {
            return Array.Empty<
                RuntimeBusSelectionInfo>();
        }

        try
        {
            return Directory
                .EnumerateFiles(
                    vehicles,
                    "*.bus",
                    SearchOption.AllDirectories)
                .Select(
                    path =>
                    {
                        var directory =
                            Path.GetDirectoryName(
                                path) ??
                            vehicles;

                        var relative =
                            Path.GetRelativePath(
                                contentRoot,
                                path);

                        var relativeDirectory =
                            Path.GetRelativePath(
                                vehicles,
                                directory);

                        var group =
                            relativeDirectory
                                .Split(
                                    Path.DirectorySeparatorChar,
                                    Path.AltDirectorySeparatorChar)
                                .FirstOrDefault()
                            ?? "Vehicles";

                        var hofFiles =
                            Directory
                                .EnumerateFiles(
                                    directory,
                                    "*.hof",
                                    SearchOption.TopDirectoryOnly)
                                .OrderBy(
                                    static item =>
                                        item,
                                    StringComparer.OrdinalIgnoreCase)
                                .ToArray();

                        return new RuntimeBusSelectionInfo(
                            relative,
                            Path.GetFileNameWithoutExtension(
                                path),
                            group,
                            directory,
                            hofFiles);
                    })
                .OrderBy(
                    static bus =>
                        bus.GroupName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static bus =>
                        bus.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<
                RuntimeBusSelectionInfo>();
        }
    }
}
