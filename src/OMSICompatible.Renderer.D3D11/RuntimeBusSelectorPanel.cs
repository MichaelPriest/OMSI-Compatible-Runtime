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
        Width = 520;
        Height = 310;
        BackColor = Color.FromArgb(
            245,
            246,
            248);
        ForeColor = Color.FromArgb(
            32,
            35,
            40);
        BorderStyle =
            BorderStyle.FixedSingle;
        Padding =
            new Padding(
                14);
        TabStop = true;

        var title =
            new Label
            {
                Text = "Selecionar ônibus",
                Font = new Font(
                    "Segoe UI",
                    13.0f,
                    FontStyle.Bold),
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
                BackColor =
                    Color.FromArgb(
                        58,
                        64,
                        74),
                ForeColor =
                    Color.FromArgb(
                        246,
                        180,
                        52),
                Left = 18,
                Top = 52,
                Width = 150,
                Height = 112
            };

        _groupBox =
            new ComboBox
            {
                DropDownStyle =
                    ComboBoxStyle.DropDownList,
                Left = 186,
                Top = 54,
                Width = 306
            };

        _busBox =
            new ComboBox
            {
                DropDownStyle =
                    ComboBoxStyle.DropDownList,
                Left = 186,
                Top = 94,
                Width = 306
            };

        _hofBox =
            new ComboBox
            {
                DropDownStyle =
                    ComboBoxStyle.DropDownList,
                Left = 186,
                Top = 134,
                Width = 306
            };

        _details =
            new Label
            {
                Left = 18,
                Top = 178,
                Width = 474,
                Height = 58,
                ForeColor =
                    Color.FromArgb(
                        76,
                        82,
                        92),
                AutoEllipsis = true
            };

        var cancel =
            new Button
            {
                Text = "Cancelar",
                Left = 326,
                Top = 252,
                Width = 80,
                Height = 32,
                FlatStyle =
                    FlatStyle.System
            };

        var ok =
            new Button
            {
                Text = "OK",
                Left = 412,
                Top = 252,
                Width = 80,
                Height = 32,
                FlatStyle =
                    FlatStyle.System
            };

        Controls.Add(
            title);
        Controls.Add(
            preview);
        Controls.Add(
            new Label
            {
                Text = "Grupo / fabricante",
                Left = 186,
                Top = 37,
                AutoSize = true
            });
        Controls.Add(
            _groupBox);
        Controls.Add(
            new Label
            {
                Text = "Modelo",
                Left = 186,
                Top = 77,
                AutoSize = true
            });
        Controls.Add(
            _busBox);
        Controls.Add(
            new Label
            {
                Text = "HOF",
                Left = 186,
                Top = 117,
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
