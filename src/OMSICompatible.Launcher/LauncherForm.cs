using System.Diagnostics;
using OMSICompatible.Renderer.D3D11;
using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Scripting;
using OmsiCompat.Vehicles;

namespace OMSICompatible.Launcher;

internal sealed class LauncherForm : Form
{
    private sealed record BusSkinSelection(
        OmsiBusInfo Bus,
        OmsiVehicleRepaint? Repaint)
    {
        public string Name =>
            Repaint?.Name ??
            Bus.Skin;

        public string Detail =>
            Repaint is null
                ? $"{Bus.Carroceria} · Skin base: {Bus.Skin}"
                : $"{Bus.Carroceria} · Repaint CTI: {Repaint.Name}";

        public override string ToString() =>
            Name;
    }

    private readonly MapHeroPanel _hero =
        new();

    private readonly TextBox _contentPathBox =
        new();

    private readonly ComboBox _mapBox =
        new();

    private readonly ComboBox _carroceriaBox =
        new();

    private readonly ComboBox _modeloBox =
        new();

    private readonly ComboBox _skinBox =
        new();

    private readonly CheckBox _noBusCheckBox =
        new();

    private readonly Panel _busPreviewHost =
        new();

    private readonly PictureBox _busPreviewFallback =
        new();

    private D3D11RenderWindow? _busPreviewWindow;
    private int _busPreviewGeneration;

    private readonly Dictionary<
        string,
        IReadOnlyList<OmsiVehicleRepaint>>
        _repaintCache =
            new(
                StringComparer.OrdinalIgnoreCase);

    private readonly Label _busPreviewTitle =
        new();

    private readonly Label _busPreviewDetail =
        new();

    private readonly ComboBox _spawnBox =
        new();

    private Button _playButton =
        new();

    private readonly Label _mapCountValue =
        new();

    private readonly Label _runtimeValue =
        new();

    private readonly Label _statusValue =
        new();

    private readonly RichTextBox _logBox =
        new();

    private readonly RuntimeProcessHost _runtime =
        new();

    private LauncherSettings _settings;

    private IReadOnlyList<OmsiMapInfo> _maps =
        Array.Empty<OmsiMapInfo>();

    private IReadOnlyList<OmsiBusInfo> _buses =
        Array.Empty<OmsiBusInfo>();

    private IReadOnlyList<OmsiMapEntryPointGroup> _entryPoints =
        Array.Empty<OmsiMapEntryPointGroup>();

    private bool _updatingBusSelection;

    public LauncherForm(
        string? explicitContentPath)
    {
        _settings =
            LauncherSettings.Load();

        Text =
            "OMSI Compatible Runtime x64";

        StartPosition =
            FormStartPosition.CenterScreen;

        MinimumSize =
            new Size(
                1050,
                760);

        ClientSize =
            new Size(
                1220,
                820);

        BackColor =
            Color.FromArgb(
                13,
                16,
                21);

        ForeColor =
            Color.White;

        Font =
            new Font(
                "Segoe UI",
                10.0f);

        BuildInterface();

        FormClosed +=
            (_, _) =>
                DisposeBusPreview();

        _updatingBusSelection = true;
        _noBusCheckBox.Checked =
            _settings.StartWithoutBus;
        ApplyNoBusMode();
        _updatingBusSelection = false;

        _runtime.OutputReceived +=
            line =>
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(
                        () => AppendRuntimeLog(line));
                }
            };

        _runtime.Exited +=
            exitCode =>
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(
                        () => RuntimeExited(exitCode));
                }
            };

        var initialPath =
            explicitContentPath ??
            _settings.ContentPath;

        if (!string.IsNullOrWhiteSpace(
                initialPath))
        {
            _contentPathBox.Text =
                initialPath;

            RefreshMaps(
                _settings.MapName);
        }

        FormClosed +=
            (_, _) =>
            {
                SaveSettings();
                _runtime.Dispose();
            };
    }

    private void BuildInterface()
    {
        var root =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding =
                    new Padding(
                        28,
                        24,
                        28,
                        24)
            };

        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                300));

        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));

        root.Controls.Add(
            _hero,
            0,
            0);

        var controlsPanel =
            BuildControlsPanel();

        root.Controls.Add(
            controlsPanel,
            0,
            1);

        var statusCards =
            BuildStatusCards();

        root.Controls.Add(
            statusCards,
            0,
            2);

        var diagnostics =
            BuildDiagnostics();

        root.Controls.Add(
            diagnostics,
            0,
            3);

        Controls.Add(root);

        _hero.SetPresentation(null);
    }

    private Control BuildControlsPanel()
    {
        var panel =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                Padding =
                    new Padding(
                        0,
                        18,
                        0,
                        12)
            };

        panel.Controls.Add(
            CreateFieldLabel(
                "PASTA DO CONTEÚDO"),
            0,
            0);

        var contentRow =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                AutoSize = true
            };

        contentRow.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));

        contentRow.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                116));

        contentRow.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                104));

        _contentPathBox.Dock =
            DockStyle.Fill;

        StyleTextBox(
            _contentPathBox);

        var browse =
            CreateButton(
                "Procurar",
                false);

        browse.Click +=
            (_, _) =>
                BrowseForContent();

        var refresh =
            CreateButton(
                "Atualizar",
                false);

        refresh.Click +=
            (_, _) =>
                RefreshMaps(
                    _mapBox.SelectedItem
                        ?.ToString());

        contentRow.Controls.Add(
            _contentPathBox,
            0,
            0);

        contentRow.Controls.Add(
            browse,
            1,
            0);

        contentRow.Controls.Add(
            refresh,
            2,
            0);

        panel.Controls.Add(
            contentRow,
            0,
            1);

        panel.Controls.Add(
            CreateFieldLabel("MAPA"),
            0,
            2);

        var mapPlayRow =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true
            };

        mapPlayRow.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));

        mapPlayRow.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                220));

        _mapBox.Dock =
            DockStyle.Fill;

        _mapBox.DropDownStyle =
            ComboBoxStyle.DropDownList;

        _mapBox.FlatStyle =
            FlatStyle.Flat;

        _mapBox.BackColor =
            Color.FromArgb(
                31,
                36,
                46);

        _mapBox.ForeColor =
            Color.White;

        _mapBox.Font =
            new Font(
                "Segoe UI",
                11.0f);

        _mapBox.SelectedIndexChanged +=
            (_, _) =>
                MapSelectionChanged();

        _playButton =
            CreateButton(
                "▶  JOGAR",
                true);

        _playButton.Enabled = false;

        _playButton.Click +=
            (_, _) =>
                StartRuntime();

        mapPlayRow.Controls.Add(
            _mapBox,
            0,
            0);

        mapPlayRow.Controls.Add(
            _playButton,
            1,
            0);

        panel.Controls.Add(
            mapPlayRow,
            0,
            3);

        var vehicleSpawnRow =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                AutoSize = true,
                Padding = new Padding(0, 10, 0, 0)
            };

        vehicleSpawnRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 40));
        vehicleSpawnRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 42));
        vehicleSpawnRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 18));

        var vehiclePanel =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                Margin = new Padding(0, 0, 10, 0)
            };

        var vehicleHeader =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true
            };

        vehicleHeader.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));
        vehicleHeader.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.AutoSize));

        vehicleHeader.Controls.Add(
            CreateFieldLabel(
                "SELEÇÃO DO ÔNIBUS"),
            0,
            0);

        _noBusCheckBox.Text =
            "Iniciar sem ônibus";
        _noBusCheckBox.AutoSize =
            true;
        _noBusCheckBox.ForeColor =
            Color.FromArgb(
                205,
                216,
                229);
        _noBusCheckBox.Padding =
            new Padding(
                8,
                5,
                0,
                0);
        _noBusCheckBox.CheckedChanged +=
            (_, _) =>
            {
                ApplyNoBusMode();

                if (_updatingBusSelection)
                {
                    return;
                }

                UpdatePlayAvailability();
                SaveSettings();
            };

        vehicleHeader.Controls.Add(
            _noBusCheckBox,
            1,
            0);

        vehiclePanel.Controls.Add(
            vehicleHeader,
            0,
            0);

        StyleComboBox(
            _carroceriaBox);
        StyleComboBox(
            _modeloBox);
        StyleComboBox(
            _skinBox);

        _carroceriaBox.SelectedIndexChanged +=
            (_, _) =>
            {
                if (_updatingBusSelection)
                {
                    return;
                }

                PopulateModels(
                    _carroceriaBox.SelectedItem
                        as string,
                    preferredBus: null);
                UpdatePlayAvailability();
                SaveSettings();
            };

        _modeloBox.SelectedIndexChanged +=
            (_, _) =>
            {
                if (_updatingBusSelection)
                {
                    return;
                }

                PopulateSkins(
                    _carroceriaBox.SelectedItem
                        as string,
                    _modeloBox.SelectedItem
                        as string,
                    preferredBus: null);
                UpdatePlayAvailability();
                SaveSettings();
            };

        _skinBox.SelectedIndexChanged +=
            (_, _) =>
            {
                if (_updatingBusSelection)
                {
                    return;
                }

                UpdateBusPreview();
                UpdatePlayAvailability();
                SaveSettings();
            };

        vehiclePanel.Controls.Add(
            CreateFieldLabel(
                "CARROCERIA"),
            0,
            1);
        vehiclePanel.Controls.Add(
            _carroceriaBox,
            0,
            2);
        vehiclePanel.Controls.Add(
            CreateFieldLabel(
                "MODELO"),
            0,
            3);
        vehiclePanel.Controls.Add(
            _modeloBox,
            0,
            4);
        vehiclePanel.Controls.Add(
            CreateFieldLabel(
                "SKIN"),
            0,
            5);
        vehiclePanel.Controls.Add(
            _skinBox,
            0,
            6);

        var previewPanel =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                Margin =
                    new Padding(
                        0,
                        20,
                        10,
                        0),
                Padding =
                    new Padding(
                        10)
            };

        previewPanel.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                300));
        previewPanel.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        previewPanel.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        _busPreviewHost.Dock =
            DockStyle.Fill;
        _busPreviewHost.BackColor =
            Color.FromArgb(
                10,
                14,
                20);

        _busPreviewFallback.Dock =
            DockStyle.Fill;
        _busPreviewFallback.SizeMode =
            PictureBoxSizeMode.Zoom;
        _busPreviewFallback.BackColor =
            _busPreviewHost.BackColor;

        _busPreviewHost.Controls.Add(
            _busPreviewFallback);

        _busPreviewTitle.AutoSize =
            true;
        _busPreviewTitle.ForeColor =
            Color.White;
        _busPreviewTitle.Font =
            new Font(
                "Segoe UI Semibold",
                11.0f);
        _busPreviewTitle.Text =
            "Nenhum ônibus selecionado";

        _busPreviewDetail.AutoSize =
            true;
        _busPreviewDetail.ForeColor =
            Color.FromArgb(
                126,
                148,
                173);
        _busPreviewDetail.Font =
            new Font(
                "Segoe UI",
                9.0f);
        _busPreviewDetail.Text =
            "Escolha carroceria, modelo e skin";

        previewPanel.Controls.Add(
            _busPreviewHost,
            0,
            0);
        previewPanel.Controls.Add(
            _busPreviewTitle,
            0,
            1);
        previewPanel.Controls.Add(
            _busPreviewDetail,
            0,
            2);

        var spawnPanel =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true
            };

        spawnPanel.Controls.Add(
            CreateFieldLabel("PONTO INICIAL"),
            0,
            0);

        StyleComboBox(_spawnBox);
        _spawnBox.SelectedIndexChanged +=
            (_, _) =>
            {
                UpdatePlayAvailability();
                SaveSettings();
            };

        spawnPanel.Controls.Add(
            _spawnBox,
            0,
            1);

        vehicleSpawnRow.Controls.Add(
            vehiclePanel,
            0,
            0);

        vehicleSpawnRow.Controls.Add(
            previewPanel,
            1,
            0);

        vehicleSpawnRow.Controls.Add(
            spawnPanel,
            2,
            0);

        panel.Controls.Add(
            vehicleSpawnRow,
            0,
            4);

        var shortcuts =
            new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Padding =
                    new Padding(
                        0,
                        12,
                        0,
                        0)
            };

        AddShortcut(
            shortcuts,
            "Configurações",
            OpenSettings);

        AddShortcut(
            shortcuts,
            "Pasta OMSI",
            () =>
                OpenFolder(
                    _contentPathBox.Text));

        AddShortcut(
            shortcuts,
            "Mapas",
            () =>
                OpenFolder(
                    Path.Combine(
                        _contentPathBox.Text,
                        "maps")));

        AddShortcut(
            shortcuts,
            "Veículos",
            () =>
                OpenFolder(
                    Path.Combine(
                        _contentPathBox.Text,
                        "Vehicles")));

        AddShortcut(
            shortcuts,
            "Logs",
            () =>
                OpenFolder(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Logs")));

        AddShortcut(
            shortcuts,
            "Criar atalho no Desktop",
            CreateDesktopShortcut);

        panel.Controls.Add(
            shortcuts,
            0,
            5);

        return panel;
    }

    private Control BuildStatusCards()
    {
        var row =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                AutoSize = true,
                Padding =
                    new Padding(
                        0,
                        4,
                        0,
                        14)
            };

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                33.33f));

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                33.33f));

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                33.34f));

        row.Controls.Add(
            CreateStatusCard(
                "MAPAS",
                _mapCountValue),
            0,
            0);

        row.Controls.Add(
            CreateStatusCard(
                "RUNTIME",
                _runtimeValue),
            1,
            0);

        row.Controls.Add(
            CreateStatusCard(
                "STATUS",
                _statusValue),
            2,
            0);

        _mapCountValue.Text = "—";
        _runtimeValue.Text = "x64 · D3D11";
        _statusValue.Text = "Aguardando";

        return row;
    }

    private Control BuildDiagnostics()
    {
        var panel =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };

        panel.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        panel.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));

        panel.Controls.Add(
            CreateFieldLabel(
                "STATUS / DIAGNÓSTICO"),
            0,
            0);

        _logBox.Dock =
            DockStyle.Fill;

        _logBox.ReadOnly = true;

        _logBox.BorderStyle =
            BorderStyle.None;

        _logBox.BackColor =
            Color.FromArgb(
                8,
                11,
                15);

        _logBox.ForeColor =
            Color.FromArgb(
                190,
                203,
                219);

        _logBox.Font =
            new Font(
                "Cascadia Mono",
                9.0f);

        panel.Controls.Add(
            _logBox,
            0,
            1);

        return panel;
    }

    private static Control CreateStatusCard(
        string caption,
        Label value)
    {
        var panel =
            new Panel
            {
                Dock = DockStyle.Fill,
                Height = 72,
                BackColor =
                    Color.FromArgb(
                        24,
                        29,
                        37),
                Margin =
                    new Padding(
                        0,
                        0,
                        10,
                        0)
            };

        var captionLabel =
            new Label
            {
                Text = caption,
                AutoSize = true,
                ForeColor =
                    Color.FromArgb(
                        112,
                        132,
                        154),
                Location =
                    new Point(
                        14,
                        11),
                Font =
                    new Font(
                        "Segoe UI Semibold",
                        8.0f)
            };

        value.AutoSize = true;
        value.ForeColor = Color.White;
        value.Location =
            new Point(
                14,
                34);
        value.Font =
            new Font(
                "Segoe UI Semibold",
                12.0f);

        panel.Controls.Add(
            captionLabel);

        panel.Controls.Add(
            value);

        return panel;
    }

    private static Label CreateFieldLabel(
        string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            ForeColor =
                Color.FromArgb(
                    110,
                    133,
                    159),
            Font =
                new Font(
                    "Segoe UI Semibold",
                    8.5f),
            Margin =
                new Padding(
                    0,
                    8,
                    0,
                    6)
        };

    private static void StyleComboBox(
        ComboBox box)
    {
        box.Dock = DockStyle.Fill;
        box.DropDownStyle =
            ComboBoxStyle.DropDownList;
        box.FlatStyle =
            FlatStyle.Flat;
        box.BackColor =
            Color.FromArgb(
                31,
                36,
                46);
        box.ForeColor =
            Color.White;
        box.Font =
            new Font(
                "Segoe UI",
                10.5f);
        box.MinimumSize =
            new Size(
                100,
                38);
    }

    private static void StyleTextBox(
        TextBox box)
    {
        box.BorderStyle =
            BorderStyle.FixedSingle;

        box.BackColor =
            Color.FromArgb(
                31,
                36,
                46);

        box.ForeColor =
            Color.White;

        box.Font =
            new Font(
                "Segoe UI",
                10.5f);

        box.MinimumSize =
            new Size(
                100,
                38);
    }

    private static Button CreateButton(
        string text,
        bool primary)
    {
        var button =
            new Button
            {
                Text = text,
                Height = primary
                    ? 48
                    : 38,
                Dock = DockStyle.Fill,
                FlatStyle =
                    FlatStyle.Flat,
                ForeColor =
                    Color.White,
                BackColor =
                    primary
                        ? Color.FromArgb(
                            0,
                            126,
                            214)
                        : Color.FromArgb(
                            39,
                            46,
                            58),
                Cursor =
                    Cursors.Hand,
                Font =
                    new Font(
                        "Segoe UI Semibold",
                        primary
                            ? 12.0f
                            : 9.5f),
                Margin =
                    new Padding(
                        8,
                        0,
                        0,
                        0)
            };

        button.FlatAppearance.BorderSize =
            0;

        return button;
    }

    private void AddShortcut(
        Control parent,
        string text,
        Action action)
    {
        var button =
            CreateButton(
                text,
                false);

        button.AutoSize = true;

        button.Padding =
            new Padding(
                10,
                2,
                10,
                2);

        button.Margin =
            new Padding(
                0,
                0,
                8,
                8);

        button.Click +=
            (_, _) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    ShowError(ex.Message);
                }
            };

        parent.Controls.Add(button);
    }

    private void BrowseForContent()
    {
        using var dialog =
            new FolderBrowserDialog
            {
                Description =
                    "Selecione a pasta raiz do conteúdo OMSI",
                ShowNewFolderButton = false
            };

        if (Directory.Exists(
                _contentPathBox.Text))
        {
            dialog.SelectedPath =
                _contentPathBox.Text;
        }

        if (dialog.ShowDialog(this) !=
            DialogResult.OK)
        {
            return;
        }

        _contentPathBox.Text =
            dialog.SelectedPath;

        RefreshMaps(null);
    }

    private void RefreshMaps(
        string? selectMap)
    {
        _mapBox.Items.Clear();
        _carroceriaBox.Items.Clear();
        _modeloBox.Items.Clear();
        _skinBox.Items.Clear();
        _spawnBox.Items.Clear();

        _maps =
            Array.Empty<OmsiMapInfo>();
        _buses =
            Array.Empty<OmsiBusInfo>();
        _entryPoints =
            Array.Empty<OmsiMapEntryPointGroup>();

        if (!OmsiContentRoot.TryCreate(
                _contentPathBox.Text,
                out var contentRoot,
                out var error) ||
            contentRoot is null)
        {
            _hero.SetPresentation(null);
            _mapCountValue.Text = "0";
            _statusValue.Text =
                "Pasta inválida";
            _playButton.Enabled = false;

            AppendRuntimeLog(
                error);

            return;
        }

        _maps =
            MapDiscovery.Discover(
                contentRoot);

        _buses =
            BusDiscovery.DiscoverPlayerSelectable(
                contentRoot);

        var selectedBus =
            _buses.FirstOrDefault(
                bus =>
                    string.Equals(
                        bus.RelativePath,
                        _settings.BusRelativePath,
                        StringComparison.OrdinalIgnoreCase))
            ?? _buses.FirstOrDefault();

        SelectBus(
            selectedBus);

        ApplyNoBusMode();

        foreach (var map in _maps)
        {
            _mapBox.Items.Add(
                map.FolderName);
        }

        _mapCountValue.Text =
            _maps.Count.ToString("N0");

        _statusValue.Text =
            _maps.Count > 0
                ? "Pronto"
                : "Sem mapas";

        if (_maps.Count == 0)
        {
            _hero.SetPresentation(null);
            _playButton.Enabled = false;
            return;
        }

        var target =
            _maps.FirstOrDefault(
                map =>
                    string.Equals(
                        map.FolderName,
                        selectMap,
                        StringComparison.OrdinalIgnoreCase));

        _mapBox.SelectedItem =
            target?.FolderName ??
            _maps[0].FolderName;

        AppendRuntimeLog(
            $"{_maps.Count:N0} mapa(s) · {_buses.Count:N0} ônibus encontrado(s).");
    }

    private void MapSelectionChanged()
    {
        var map =
            SelectedMap();

        _spawnBox.Items.Clear();
        _entryPoints =
            map is null
                ? Array.Empty<OmsiMapEntryPointGroup>()
                : MapEntryPointDiscovery.Discover(map);

        foreach (var entryPoint in _entryPoints)
        {
            _spawnBox.Items.Add(entryPoint);
        }

        if (_entryPoints.Count > 0)
        {
            var selectedSpawn =
                _entryPoints.FirstOrDefault(
                    point =>
                        string.Equals(
                            point.Name,
                            _settings.EntryPointName,
                            StringComparison.OrdinalIgnoreCase));

            _spawnBox.SelectedItem =
                selectedSpawn ??
                _entryPoints[0];
        }

        _hero.SetPresentation(
            map is null
                ? null
                : MapPresentationReader.Read(
                    map));

        UpdatePlayAvailability();
        SaveSettings();
    }

    private OmsiMapInfo? SelectedMap()
    {
        var name =
            _mapBox.SelectedItem
                ?.ToString();

        return _maps.FirstOrDefault(
            map =>
                string.Equals(
                    map.FolderName,
                    name,
                    StringComparison.OrdinalIgnoreCase));
    }


    private void SelectBus(
        OmsiBusInfo? bus)
    {
        _updatingBusSelection = true;

        try
        {
            _carroceriaBox.Items.Clear();

            foreach (var carroceria in
                     _buses
                         .Select(
                             static item =>
                                 item.Carroceria)
                         .Distinct(
                             StringComparer.OrdinalIgnoreCase)
                         .OrderBy(
                             static value =>
                                 value,
                             StringComparer.OrdinalIgnoreCase))
            {
                _carroceriaBox.Items.Add(
                    carroceria);
            }

            var targetCarroceria =
                bus?.Carroceria ??
                _carroceriaBox.Items
                    .Cast<object>()
                    .FirstOrDefault()
                    ?.ToString();

            if (!string.IsNullOrWhiteSpace(
                    targetCarroceria))
            {
                _carroceriaBox.SelectedItem =
                    _carroceriaBox.Items
                        .Cast<object>()
                        .FirstOrDefault(
                            value =>
                                string.Equals(
                                    value?.ToString(),
                                    targetCarroceria,
                                    StringComparison.OrdinalIgnoreCase));
            }

            PopulateModels(
                targetCarroceria,
                bus);
        }
        finally
        {
            _updatingBusSelection = false;
        }

        UpdateBusPreview();
    }

    private void PopulateModels(
        string? carroceria,
        OmsiBusInfo? preferredBus)
    {
        var previous =
            _updatingBusSelection;

        _updatingBusSelection = true;

        try
        {
            _modeloBox.Items.Clear();

            foreach (var modelo in
                     _buses
                         .Where(
                             bus =>
                                 string.Equals(
                                     bus.Carroceria,
                                     carroceria,
                                     StringComparison.OrdinalIgnoreCase))
                         .Select(
                             static bus =>
                                 bus.Modelo)
                         .Distinct(
                             StringComparer.OrdinalIgnoreCase)
                         .OrderBy(
                             static value =>
                                 value,
                             StringComparer.OrdinalIgnoreCase))
            {
                _modeloBox.Items.Add(
                    modelo);
            }

            var targetModelo =
                preferredBus?.Modelo ??
                _modeloBox.Items
                    .Cast<object>()
                    .FirstOrDefault()
                    ?.ToString();

            if (!string.IsNullOrWhiteSpace(
                    targetModelo))
            {
                _modeloBox.SelectedItem =
                    _modeloBox.Items
                        .Cast<object>()
                        .FirstOrDefault(
                            value =>
                                string.Equals(
                                    value?.ToString(),
                                    targetModelo,
                                    StringComparison.OrdinalIgnoreCase));
            }

            PopulateSkins(
                carroceria,
                targetModelo,
                preferredBus);
        }
        finally
        {
            _updatingBusSelection = previous;
        }

        UpdateBusPreview();
    }

    private void PopulateSkins(
        string? carroceria,
        string? modelo,
        OmsiBusInfo? preferredBus)
    {
        var previous =
            _updatingBusSelection;

        _updatingBusSelection = true;

        try
        {
            _skinBox.Items.Clear();

            var buses =
                _buses
                    .Where(
                        bus =>
                            string.Equals(
                                bus.Carroceria,
                                carroceria,
                                StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(
                                bus.Modelo,
                                modelo,
                                StringComparison.OrdinalIgnoreCase))
                    .OrderBy(
                        static bus =>
                            bus.Skin,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        static bus =>
                            bus.RelativePath,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var selections =
                new List<
                    BusSkinSelection>();

            foreach (var bus in
                     buses)
            {
                selections.Add(
                    new BusSkinSelection(
                        bus,
                        null));

                foreach (var repaint in
                         GetRepaints(
                             bus))
                {
                    selections.Add(
                        new BusSkinSelection(
                            bus,
                            repaint));
                }
            }

            foreach (var selection in
                     selections
                         .GroupBy(
                             static item =>
                                 item.Bus.RelativePath +
                                 "\n" +
                                 item.Name +
                                 "\n" +
                                 (item.Repaint?.RelativeCtiPath ?? ""),
                             StringComparer.OrdinalIgnoreCase)
                         .Select(
                             static group =>
                                 group.First())
                         .OrderBy(
                             static item =>
                                 item.Name,
                             StringComparer.OrdinalIgnoreCase))
            {
                _skinBox.Items.Add(
                    selection);
            }

            BusSkinSelection? target =
                null;

            if (preferredBus is not null)
            {
                target =
                    selections.FirstOrDefault(
                        item =>
                            string.Equals(
                                item.Bus.RelativePath,
                                preferredBus.RelativePath,
                                StringComparison.OrdinalIgnoreCase) &&
                            RepaintMatchesSavedSelection(
                                item.Repaint));
            }

            target ??=
                selections.FirstOrDefault(
                    item =>
                        preferredBus is not null &&
                        string.Equals(
                            item.Bus.RelativePath,
                            preferredBus.RelativePath,
                            StringComparison.OrdinalIgnoreCase));

            target ??=
                selections.FirstOrDefault();

            _skinBox.SelectedItem =
                target;
        }
        finally
        {
            _updatingBusSelection =
                previous;
        }

        UpdateBusPreview();
    }

    private IReadOnlyList<OmsiVehicleRepaint> GetRepaints(
        OmsiBusInfo bus)
    {
        if (_repaintCache.TryGetValue(
                bus.RelativePath,
                out var cached))
        {
            return cached;
        }

        var discovered =
            OmsiVehicleRepaintCatalog.Discover(
                bus);

        _repaintCache[
            bus.RelativePath] =
            discovered;

        return discovered;
    }

    private bool RepaintMatchesSavedSelection(
        OmsiVehicleRepaint? repaint)
    {
        if (string.IsNullOrWhiteSpace(
                _settings.RepaintName) &&
            string.IsNullOrWhiteSpace(
                _settings.RepaintCtiRelativePath))
        {
            return repaint is null;
        }

        if (repaint is null)
        {
            return false;
        }

        var nameMatches =
            string.IsNullOrWhiteSpace(
                _settings.RepaintName) ||
            string.Equals(
                repaint.Name,
                _settings.RepaintName,
                StringComparison.OrdinalIgnoreCase);

        var ctiMatches =
            string.IsNullOrWhiteSpace(
                _settings.RepaintCtiRelativePath) ||
            string.Equals(
                repaint.RelativeCtiPath,
                _settings.RepaintCtiRelativePath,
                StringComparison.OrdinalIgnoreCase);

        return
            nameMatches &&
            ctiMatches;
    }

    private void ApplyNoBusMode()
    {
        var enabled =
            !_noBusCheckBox.Checked;

        _carroceriaBox.Enabled =
            enabled;
        _modeloBox.Enabled =
            enabled;
        _skinBox.Enabled =
            enabled;

        UpdateBusPreview();
    }

    private async void UpdateBusPreview()
    {
        var generation =
            ++_busPreviewGeneration;

        DisposeBusPreviewWindow();

        _busPreviewFallback.Image?.Dispose();
        _busPreviewFallback.Image =
            null;

        _busPreviewHost.Controls.Clear();
        _busPreviewHost.Controls.Add(
            _busPreviewFallback);

        if (_noBusCheckBox.Checked)
        {
            _busPreviewTitle.Text =
                "Iniciar sem ônibus";
            _busPreviewDetail.Text =
                "Mapa em câmera livre · nenhum veículo carregado";
            return;
        }

        var selection =
            SelectedSkin();

        var bus =
            selection?.Bus;

        _busPreviewTitle.Text =
            bus?.Modelo ??
            "Nenhum ônibus selecionado";

        _busPreviewDetail.Text =
            selection is null
                ? "Escolha carroceria, modelo e skin"
                : selection.Detail +
                  " · carregando prévia 3D...";

        if (selection is null ||
            bus is null)
        {
            return;
        }

        LoadStaticPreviewFallback(
            bus);

        if (!OmsiContentRoot.TryCreate(
                _contentPathBox.Text,
                out var contentRoot,
                out _) ||
            contentRoot is null)
        {
            _busPreviewDetail.Text =
                selection.Detail +
                " · prévia 3D indisponível";
            return;
        }

        await Task.Delay(
            120);

        if (generation !=
                _busPreviewGeneration ||
            IsDisposed ||
            !IsHandleCreated)
        {
            return;
        }

        try
        {
            var repaint =
                selection.Repaint;

            var asset =
                await Task.Run(
                    () =>
                        OmsiArticulatedVehicleAssetLoader.Load(
                            contentRoot,
                            bus,
                            progress:
                                null,
                            repaint:
                                repaint));

            if (generation !=
                    _busPreviewGeneration ||
                IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            if (asset.RenderableMeshCount <= 0)
            {
                _busPreviewDetail.Text =
                    selection.Detail +
                    " · modelo sem mesh 3D renderizável";
                return;
            }

            var runtimeInfo =
                VehiclePreviewFactory.Create(
                    contentRoot.RootPath,
                    asset);

            var scriptCatalog =
                OmsiScriptCatalogLoader.Load(
                    contentRoot,
                    bus.ScriptManifest);

            var previewScriptRuntime =
                new OmsiScriptRuntime(
                    scriptCatalog);

            var preview =
                new D3D11RenderWindow(
                    runtimeInfo,
                    scriptRuntime:
                        previewScriptRuntime,
                    targetFps:
                        60,
                    vsync:
                        true,
                    vehiclePreviewMode:
                        true,
                    initialVehicleVariables:
                        repaint?.SetVariables)
                {
                    TopLevel =
                        false,
                    FormBorderStyle =
                        FormBorderStyle.None,
                    Dock =
                        DockStyle.Fill
                };

            _busPreviewHost.Controls.Clear();
            _busPreviewHost.Controls.Add(
                preview);

            _busPreviewWindow =
                preview;

            preview.Show();

            _busPreviewDetail.Text =
                selection.Detail +
                " · 3D: arraste para girar · roda do mouse para zoom";
        }
        catch (Exception ex)
        {
            if (generation !=
                _busPreviewGeneration)
            {
                return;
            }

            _busPreviewDetail.Text =
                selection.Detail +
                $" · prévia 3D indisponível ({ex.GetType().Name})";

            AppendRuntimeLog(
                $"Prévia 3D: {ex.Message}");
        }
    }

    private void LoadStaticPreviewFallback(
        OmsiBusInfo bus)
    {
        var preview =
            bus.PreviewImagePath;

        if (string.IsNullOrWhiteSpace(
                preview) ||
            !File.Exists(
                preview))
        {
            return;
        }

        try
        {
            using var stream =
                File.OpenRead(
                    preview);

            using var image =
                Image.FromStream(
                    stream);

            _busPreviewFallback.Image =
                new Bitmap(
                    image);
        }
        catch
        {
            _busPreviewFallback.Image =
                null;
        }
    }

    private void DisposeBusPreviewWindow()
    {
        var preview =
            _busPreviewWindow;

        _busPreviewWindow =
            null;

        if (preview is null)
        {
            return;
        }

        try
        {
            preview.Close();
            preview.Dispose();
        }
        catch
        {
        }
    }

    private void DisposeBusPreview()
    {
        _busPreviewGeneration++;
        DisposeBusPreviewWindow();

        _busPreviewFallback.Image?.Dispose();
        _busPreviewFallback.Image =
            null;
    }

    private BusSkinSelection? SelectedSkin() =>
        _skinBox.SelectedItem as
            BusSkinSelection;

    private OmsiBusInfo? SelectedBus() =>
        SelectedSkin()?.Bus;

    private OmsiVehicleRepaint? SelectedRepaint() =>
        SelectedSkin()?.Repaint;

    private OmsiMapEntryPointGroup? SelectedEntryPoint() =>
        _spawnBox.SelectedItem as OmsiMapEntryPointGroup;

    private void UpdatePlayAvailability()
    {
        _playButton.Enabled =
            SelectedMap() is not null &&
            SelectedEntryPoint() is not null &&
            (_noBusCheckBox.Checked ||
             SelectedBus() is not null) &&
            !_runtime.IsRunning;
    }

    private void StartRuntime()
    {
        if (_runtime.IsRunning)
        {
            return;
        }

        var map =
            SelectedMap();

        if (map is null)
        {
            ShowError(
                "Selecione um mapa.");
            return;
        }

        var noBus =
            _noBusCheckBox.Checked;

        var bus =
            SelectedBus();

        if (!noBus &&
            bus is null)
        {
            ShowError(
                "Selecione um ônibus.");
            return;
        }

        var entryPoint =
            SelectedEntryPoint();

        if (entryPoint is null)
        {
            ShowError(
                "Selecione um ponto inicial do mapa.");
            return;
        }

        if (!Directory.Exists(
                _contentPathBox.Text))
        {
            ShowError(
                "A pasta de conteúdo não existe.");
            return;
        }

        try
        {
            SaveSettings();

            _statusValue.Text =
                "Iniciando...";

            _playButton.Enabled = false;

            var skinSelection =
                SelectedSkin();

            AppendRuntimeLog(
                $"Iniciando {map.FolderName} · {(noBus ? "sem ônibus" : $"{bus!.Carroceria} — {bus.Modelo} — {skinSelection?.Name ?? bus.Skin}")} · {entryPoint.Name}...");

            var repaint =
                noBus
                    ? null
                    : SelectedRepaint();

            if (!_runtime.Start(
                    _contentPathBox.Text,
                    map.FolderName,
                    noBus
                        ? null
                        : bus?.RelativePath,
                    entryPoint.Name,
                    repaint?.Name,
                    repaint?.RelativeCtiPath))
            {
                throw new InvalidOperationException(
                    "O runtime não pôde ser iniciado.");
            }

            _settings =
                _settings with
                {
                    LastSessionMapName =
                        map.FolderName,
                    LastSessionBusRelativePath =
                        noBus
                            ? null
                            : bus?.RelativePath,
                    LastSessionSkin =
                        noBus
                            ? null
                            : bus?.Skin,
                    LastSessionRepaintName =
                        repaint?.Name,
                    LastSessionRepaintCtiRelativePath =
                        repaint?.RelativeCtiPath,
                    LastSessionEntryPointName =
                        entryPoint.Name,
                    LastSessionWithoutBus =
                        noBus,
                    LastSessionStartedAt =
                        DateTimeOffset.Now
                };

            SaveSettings();

            _statusValue.Text =
                "Em execução";
        }
        catch (Exception ex)
        {
            _statusValue.Text =
                "Erro";
            _playButton.Enabled = true;
            ShowError(ex.Message);
        }
    }

    private void RuntimeExited(
        int exitCode)
    {
        _statusValue.Text =
            exitCode == 0
                ? "Encerrado"
                : $"Erro {exitCode}";

        UpdatePlayAvailability();

        AppendRuntimeLog(
            $"Runtime encerrado com código {exitCode}.");
    }

    private void OpenSettings()
    {
        if (!Directory.Exists(
                _contentPathBox.Text))
        {
            ShowError(
                "Selecione primeiro a pasta raiz do conteúdo OMSI.");
            return;
        }

        using var settings =
            new SettingsForm(
                _contentPathBox.Text);

        if (settings.ShowDialog(this) ==
            DialogResult.OK)
        {
            AppendRuntimeLog(
                "Configurações atualizadas.");
        }
    }

    private void CreateDesktopShortcut()
    {
        var path =
            DesktopShortcutCreator.Create();

        AppendRuntimeLog(
            $"Atalho criado: {path}");

        MessageBox.Show(
            this,
            "Atalho criado na Área de Trabalho.",
            "OMSI Compatible Runtime",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OpenFolder(
        string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"Pasta não encontrada: {path}");
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
    }

    private void AppendRuntimeLog(
        string line)
    {
        if (_logBox.TextLength > 0)
        {
            _logBox.AppendText(
                Environment.NewLine);
        }

        _logBox.AppendText(
            $"[{DateTime.Now:HH:mm:ss}] {line}");

        _logBox.SelectionStart =
            _logBox.TextLength;

        _logBox.ScrollToCaret();
    }

    private void SaveSettings()
    {
        var repaint =
            SelectedRepaint();

        _settings =
            _settings with
            {
                ContentPath =
                    _contentPathBox.Text,
                MapName =
                    _mapBox.SelectedItem
                        ?.ToString(),
                BusRelativePath =
                    SelectedBus()?.RelativePath,
                EntryPointName =
                    SelectedEntryPoint()?.Name,
                StartWithoutBus =
                    _noBusCheckBox.Checked,
                RepaintName =
                    repaint?.Name,
                RepaintCtiRelativePath =
                    repaint?.RelativeCtiPath
            };

        _settings.Save();
    }

    private void ShowError(
        string message)
    {
        MessageBox.Show(
            this,
            message,
            "OMSI Compatible Runtime",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
