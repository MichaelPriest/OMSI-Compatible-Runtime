using System.Diagnostics;
using OmsiCompat.Core;

namespace OMSICompatible.Launcher;

internal sealed class SettingsForm : Form
{
    private sealed record OptionDescriptor(
        string Label,
        string Description,
        Type ValueType,
        Func<object?> Read,
        Action<object?> Write,
        IReadOnlyList<string>? Choices = null);

    private readonly string _contentRoot;
    private OmsiRuntimeOptions _options;
    private readonly TabControl _tabs = new();
    private readonly List<DataGridView> _optionGrids = [];
    private readonly ComboBox _presetBox = new();
    private readonly RichTextBox _keyboardEditor = new();
    private readonly RichTextBox _controllerEditor = new();
    private readonly Label _status = new();

    public SettingsForm(
        string contentRoot)
    {
        _contentRoot =
            contentRoot;

        _options =
            OmsiRuntimeOptions.Load();

        Text =
            "Configurações — OMSI Compatible Runtime";

        StartPosition =
            FormStartPosition.CenterParent;

        MinimumSize =
            new Size(
                980,
                720);

        ClientSize =
            new Size(
                1180,
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
        LoadPresetList();
        LoadInputFiles();
        RebuildOptionTabs();
    }

    private void BuildInterface()
    {
        var root =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding =
                    new Padding(
                        18)
            };

        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        root.Controls.Add(
            BuildHeader(),
            0,
            0);

        _tabs.Dock =
            DockStyle.Fill;
        _tabs.Padding =
            new Point(
                14,
                6);

        root.Controls.Add(
            _tabs,
            0,
            1);

        root.Controls.Add(
            BuildFooter(),
            0,
            2);

        Controls.Add(
            root);
    }

    private Control BuildHeader()
    {
        var panel =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 6,
                Padding =
                    new Padding(
                        0,
                        0,
                        0,
                        12)
            };

        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                175));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                150));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                160));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                120));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                120));

        var title =
            new Label
            {
                AutoSize = true,
                Text =
                    "CONFIGURAÇÕES",
                ForeColor =
                    Color.FromArgb(
                        112,
                        199,
                        255),
                Font =
                    new Font(
                        "Segoe UI Semibold",
                        11.0f),
                Padding =
                    new Padding(
                        0,
                        8,
                        0,
                        0)
            };

        _presetBox.Dock =
            DockStyle.Fill;
        _presetBox.DropDownStyle =
            ComboBoxStyle.DropDownList;
        _presetBox.BackColor =
            Color.FromArgb(
                31,
                36,
                46);
        _presetBox.ForeColor =
            Color.White;

        var importCurrent =
            CreateButton(
                "Importar options.cfg");

        importCurrent.Click +=
            (_, _) =>
                ImportCurrentOmsiOptions();

        var loadPreset =
            CreateButton(
                "Carregar perfil");

        loadPreset.Click +=
            (_, _) =>
                ImportSelectedPreset();

        var openOmsi =
            CreateButton(
                "Abrir OMSI");

        openOmsi.Click +=
            (_, _) =>
                OpenOmsiFolder();

        var defaults =
            CreateButton(
                "Padrões");

        defaults.Click +=
            (_, _) =>
            {
                _options =
                    new OmsiRuntimeOptions();

                RebuildOptionTabs();

                SetStatus(
                    "Valores padrão do runtime carregados.");
            };

        panel.Controls.Add(
            title,
            0,
            0);
        panel.Controls.Add(
            _presetBox,
            1,
            0);
        panel.Controls.Add(
            importCurrent,
            2,
            0);
        panel.Controls.Add(
            loadPreset,
            3,
            0);
        panel.Controls.Add(
            openOmsi,
            4,
            0);
        panel.Controls.Add(
            defaults,
            5,
            0);

        return panel;
    }

    private Control BuildFooter()
    {
        var panel =
            new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ColumnCount = 4,
                Padding =
                    new Padding(
                        0,
                        12,
                        0,
                        0)
            };

        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                150));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                120));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                120));

        _status.Dock =
            DockStyle.Fill;
        _status.AutoEllipsis =
            true;
        _status.ForeColor =
            Color.FromArgb(
                152,
                169,
                189);
        _status.Padding =
            new Padding(
                0,
                9,
                0,
                0);
        _status.Text =
            "As configurações OMSI são importadas sem alterar a instalação original.";

        var saveInputs =
            CreateButton(
                "Salvar Inputs");

        saveInputs.Click +=
            (_, _) =>
                SaveInputFiles();

        var save =
            CreateButton(
                "Salvar");

        save.Click +=
            (_, _) =>
            {
                if (!CommitGridValues())
                {
                    return;
                }

                _options.Save();

                SetStatus(
                    $"Configurações salvas em {OmsiRuntimeOptions.SettingsPath}");

                DialogResult =
                    DialogResult.OK;

                Close();
            };

        var cancel =
            CreateButton(
                "Cancelar");

        cancel.Click +=
            (_, _) =>
            {
                DialogResult =
                    DialogResult.Cancel;

                Close();
            };

        panel.Controls.Add(
            _status,
            0,
            0);
        panel.Controls.Add(
            saveInputs,
            1,
            0);
        panel.Controls.Add(
            save,
            2,
            0);
        panel.Controls.Add(
            cancel,
            3,
            0);

        return panel;
    }

    private void RebuildOptionTabs()
    {
        var selected =
            _tabs.SelectedIndex;

        _tabs.TabPages.Clear();
        _optionGrids.Clear();

        AddOptionsTab(
            "Geral",
            GeneralOptions());

        AddOptionsTab(
            "Avançado",
            AdvancedOptions());

        AddOptionsTab(
            "Gráficos",
            GraphicsOptions());

        AddOptionsTab(
            "Gráficos avançados",
            AdvancedGraphicsOptions());

        AddOptionsTab(
            "Som",
            SoundOptions());

        AddOptionsTab(
            "Tráfego IA",
            TrafficOptions());

        AddTextEditorTab(
            "Teclado",
            _keyboardEditor,
            Path.Combine(
                _contentRoot,
                "Inputs",
                "keyboard.cfg"),
            includeResetButton: true);

        AddTextEditorTab(
            "Game Controller",
            _controllerEditor,
            Path.Combine(
                _contentRoot,
                "Inputs",
                "gamectrler.cfg"),
            includeResetButton: false);

        AddOptionsTab(
            "Runtime x64",
            RuntimeOptions());

        if (_tabs.TabPages.Count > 0)
        {
            _tabs.SelectedIndex =
                Math.Clamp(
                    selected,
                    0,
                    _tabs.TabPages.Count - 1);
        }
    }

    private void AddOptionsTab(
        string title,
        IReadOnlyList<OptionDescriptor> options)
    {
        var tab =
            new TabPage(
                title)
            {
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                ForeColor =
                    Color.White,
                Padding =
                    new Padding(
                        10)
            };

        var grid =
            CreateOptionsGrid();

        foreach (var option in
                 options)
        {
            var row =
                new DataGridViewRow
                {
                    Tag =
                        option
                };

            row.CreateCells(
                grid);

            row.Cells[0].Value =
                option.Label;

            DataGridViewCell valueCell;

            if (option.ValueType ==
                typeof(bool))
            {
                valueCell =
                    new DataGridViewCheckBoxCell
                    {
                        Value =
                            Convert.ToBoolean(
                                option.Read())
                    };
            }
            else if (option.Choices is
                     { Count: > 0 })
            {
                var combo =
                    new DataGridViewComboBoxCell
                    {
                        FlatStyle =
                            FlatStyle.Flat
                    };

                combo.Items.AddRange(
                    option.Choices
                        .Cast<object>()
                        .ToArray());

                combo.Value =
                    option.Read()
                        ?.ToString() ??
                    option.Choices[0];

                valueCell =
                    combo;
            }
            else
            {
                valueCell =
                    new DataGridViewTextBoxCell
                    {
                        Value =
                            FormatValue(
                                option.Read())
                    };
            }

            row.Cells[1] =
                valueCell;

            row.Cells[2].Value =
                option.Description;

            grid.Rows.Add(
                row);
        }

        _optionGrids.Add(
            grid);

        tab.Controls.Add(
            grid);

        _tabs.TabPages.Add(
            tab);
    }

    private void AddTextEditorTab(
        string title,
        RichTextBox editor,
        string path,
        bool includeResetButton)
    {
        var tab =
            new TabPage(
                title)
            {
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                ForeColor =
                    Color.White,
                Padding =
                    new Padding(
                        10)
            };

        var layout =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };

        layout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        var info =
            new Label
            {
                AutoSize = true,
                MaximumSize =
                    new Size(
                        1050,
                        0),
                Text =
                    title == "Teclado"
                        ? "Mapeamento compatível com Inputs\\keyboard.cfg. Cada [entry] representa um evento do OMSI/veículo e sua tecla."
                        : "Mapeamento compatível com Inputs\\gamectrler.cfg, incluindo controladores, eixos, botões e force feedback.",
                ForeColor =
                    Color.FromArgb(
                        190,
                        203,
                        218),
                Padding =
                    new Padding(
                        0,
                        0,
                        0,
                        8)
            };

        editor.Dock =
            DockStyle.Fill;
        editor.BorderStyle =
            BorderStyle.FixedSingle;
        editor.BackColor =
            Color.FromArgb(
                10,
                14,
                20);
        editor.ForeColor =
            Color.FromArgb(
                224,
                231,
                239);
        editor.Font =
            new Font(
                "Cascadia Mono",
                9.5f);
        editor.WordWrap =
            false;
        editor.AcceptsTab =
            true;

        var buttons =
            new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection =
                    FlowDirection.LeftToRight,
                Padding =
                    new Padding(
                        0,
                        8,
                        0,
                        0)
            };

        var reload =
            CreateButton(
                "Recarregar arquivo");

        reload.Click +=
            (_, _) =>
                LoadEditor(
                    editor,
                    path);

        buttons.Controls.Add(
            reload);

        if (includeResetButton)
        {
            var reset =
                CreateButton(
                    "Restaurar keyboard_reset.cfg");

            reset.Click +=
                (_, _) =>
                    LoadEditor(
                        editor,
                        Path.Combine(
                            _contentRoot,
                            "Inputs",
                            "keyboard_reset.cfg"));

            buttons.Controls.Add(
                reset);
        }

        var openInputs =
            CreateButton(
                "Abrir pasta Inputs");

        openInputs.Click +=
            (_, _) =>
                OpenPath(
                    Path.Combine(
                        _contentRoot,
                        "Inputs"));

        buttons.Controls.Add(
            openInputs);

        layout.Controls.Add(
            info,
            0,
            0);
        layout.Controls.Add(
            editor,
            0,
            1);
        layout.Controls.Add(
            buttons,
            0,
            2);

        tab.Controls.Add(
            layout);

        _tabs.TabPages.Add(
            tab);
    }

    private DataGridView CreateOptionsGrid()
    {
        var grid =
            new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoGenerateColumns = false,
                SelectionMode =
                    DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                BackgroundColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                BorderStyle =
                    BorderStyle.None,
                GridColor =
                    Color.FromArgb(
                        45,
                        56,
                        70),
                EnableHeadersVisualStyles =
                    false
            };

        grid.ColumnHeadersDefaultCellStyle.BackColor =
            Color.FromArgb(
                27,
                34,
                44);
        grid.ColumnHeadersDefaultCellStyle.ForeColor =
            Color.White;
        grid.DefaultCellStyle.BackColor =
            Color.FromArgb(
                20,
                27,
                37);
        grid.DefaultCellStyle.ForeColor =
            Color.White;
        grid.DefaultCellStyle.SelectionBackColor =
            Color.FromArgb(
                45,
                77,
                105);
        grid.DefaultCellStyle.SelectionForeColor =
            Color.White;

        grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Configuração",
                ReadOnly = true,
                Width = 285
            });

        grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Valor",
                Width = 210
            });

        grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Descrição",
                ReadOnly = true,
                AutoSizeMode =
                    DataGridViewAutoSizeColumnMode.Fill
            });

        return grid;
    }

    private bool CommitGridValues()
    {
        foreach (var grid in
                 _optionGrids)
        {
            grid.EndEdit();

            foreach (DataGridViewRow row in
                     grid.Rows)
            {
                if (row.Tag is not
                    OptionDescriptor option)
                {
                    continue;
                }

                try
                {
                    var raw =
                        row.Cells[1].Value;

                    object? value;

                    if (option.ValueType ==
                        typeof(bool))
                    {
                        value =
                            Convert.ToBoolean(
                                raw ?? false);
                    }
                    else if (option.ValueType ==
                             typeof(int))
                    {
                        value =
                            int.Parse(
                                raw?.ToString() ??
                                "0",
                                System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else if (option.ValueType ==
                             typeof(double))
                    {
                        value =
                            double.Parse(
                                (raw?.ToString() ??
                                 "0")
                                    .Replace(
                                        ',',
                                        '.'),
                                System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        value =
                            raw?.ToString() ??
                            "";
                    }

                    option.Write(
                        value);
                }
                catch
                {
                    MessageBox.Show(
                        this,
                        $"Valor inválido em: {option.Label}",
                        "Configurações",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return false;
                }
            }
        }

        return true;
    }

    private IReadOnlyList<OptionDescriptor> GeneralOptions() =>
    [
        Choice("Idioma", "Idioma dos diálogos, avisos e interface.", () => _options.Language, v => _options.Language = v, DiscoverLanguages()),
        Choice("Venda de passagens", "0=desativada, 1=simples, 2=avançada.", () => _options.TicketSalesMode.ToString(), v => _options.TicketSalesMode = int.Parse(v), ["0", "1", "2"]),
        Text("Rádio via internet", "URL usada por veículos equipados com rádio.", () => _options.InternetRadioUrl, v => _options.InternetRadioUrl = v),
        Bool("Transição suave da câmera", "Transições suaves entre câmeras de motorista/passageiro.", () => _options.SmoothDriverViewTransitions, v => _options.SmoothDriverViewTransitions = v),
        Bool("Movimento da cabeça do motorista", "Movimento relativo da cabeça durante aceleração e frenagem.", () => _options.DriverHeadMovement, v => _options.DriverHeadMovement = v),
        Bool("Colisão veículo/paisagem", "Ativa a detecção de colisão com o cenário.", () => _options.VehicleLandscapeCollisions, v => _options.VehicleLandscapeCollisions = v),
        Bool("Colisão com terreno", "Ativa colisão física com o terreno.", () => _options.TerrainCollisions, v => _options.TerrainCollisions = v),
        Bool("Colisão veículo/veículo", "Ativa colisão entre veículos.", () => _options.VehicleToVehicleCollisions, v => _options.VehicleToVehicleCollisions = v),
        Bool("Colisão veículo/pedestres", "Ativa colisão do veículo do jogador com pedestres.", () => _options.UserVehiclePedestrianCollisions, v => _options.UserVehiclePedestrianCollisions = v),
        Bool("Centralização automática da direção", "Compatibilidade com a opção autoCenter do OMSI.", () => _options.AutomaticSteeringCenter, v => _options.AutomaticSteeringCenter = v),
        Bool("Motorista visível no próprio ônibus", "Mostra o motorista em vistas externas.", () => _options.SeeOwnDriver, v => _options.SeeOwnDriver = v),
        Bool("Informações de passageiros/passagens", "Mostra diálogos e avisos de passageiros.", () => _options.ShowTicketPassengerInfo, v => _options.ShowTicketPassengerInfo = v),
        Bool("Usar hora atual", "Usa a hora atual do sistema ao iniciar.", () => _options.UseCurrentTime, v => _options.UseCurrentTime = v),
        Bool("Usar data atual", "Usa a data atual do sistema ao iniciar.", () => _options.UseCurrentDate, v => _options.UseCurrentDate = v),
        Bool("Usar ano atual", "Usa o ano atual do sistema ao iniciar.", () => _options.UseCurrentYear, v => _options.UseCurrentYear = v),
        Integer("Vida útil/desgaste", "Nível de desgaste/lifespan compatível com options.cfg.", () => _options.WearLifespan, v => _options.WearLifespan = v),
        Bool("Embreagem automática", "Permite gerenciamento automático da embreagem.", () => _options.AutomaticClutch, v => _options.AutomaticClutch = v),
        Bool("Análise automática de horário", "Exibe análise de horário conforme o comportamento OMSI.", () => _options.ScheduleAnalysisPopup, v => _options.ScheduleAnalysisPopup = v),
        Bool("Vista alternativa", "Compatibilidade com a opção altView.", () => _options.AlternativeView, v => _options.AlternativeView = v),
        Bool("Velocidade de direção reduzida", "Compatibilidade com redSteerSpd.", () => _options.ReducedSteeringSpeed, v => _options.ReducedSteeringSpeed = v),
        Bool("Prévia de veículo", "Mostra a prévia do veículo quando disponível.", () => _options.ShowVehiclePreview, v => _options.ShowVehiclePreview = v),
        Text("Fonte typewriter", "Fonte usada por elementos do OMSI que dependem de font_typewriter.", () => _options.TypewriterFont, v => _options.TypewriterFont = v),
        Bool("Game Controller ativo", "Ativa o uso dos controladores configurados em Inputs\\gamectrler.cfg.", () => _options.GameControllerEnabled, v => _options.GameControllerEnabled = v)
    ];

    private IReadOnlyList<OptionDescriptor> AdvancedOptions() =>
    [
        Bool("Multithreading reduzido", "Compatibilidade com o modo de multithreading reduzido do OMSI 2.", () => _options.ReducedMultithreading, v => _options.ReducedMultithreading = v),
        Bool("Carregar mapa inteiro ao iniciar", "Equivale à opção loadAllTiles.", () => _options.LoadWholeMapAtStart, v => _options.LoadWholeMapAtStart = v),
        Bool("Autosave", "Salva automaticamente o estado quando suportado.", () => _options.AutoSave, v => _options.AutoSave = v),
        Bool("Mostrar mensagens de erro", "Mostra mensagens de diagnóstico durante a execução.", () => _options.ShowErrorMessages, v => _options.ShowErrorMessages = v)
    ];

    private IReadOnlyList<OptionDescriptor> GraphicsOptions() =>
    [
        Integer("FPS alvo", "Limite de atualização/renderização.", () => _options.TargetFps, v => _options.TargetFps = Math.Clamp(v, 10, 240)),
        Integer("Tiles vizinhos", "Quantidade de tiles ativos em cada direção.", () => _options.NeighborTiles, v => _options.NeighborTiles = Math.Clamp(v, 0, 8)),
        Number("Visibilidade máxima (m)", "Distância máxima de objetos.", () => _options.MaximumObjectVisibilityMeters, v => _options.MaximumObjectVisibilityMeters = Math.Max(50, v)),
        Number("Tamanho mínimo do objeto", "Fração mínima do tamanho da tela.", () => _options.MinimumObjectSize, v => _options.MinimumObjectSize = Math.Max(0, v)),
        Number("Tamanho mínimo em reflexos", "Fração mínima para objetos renderizados nos reflexos.", () => _options.MinimumObjectSizeReflections, v => _options.MinimumObjectSizeReflections = Math.Max(0, v)),
        Choice("Reflexos em tempo real", "none, economy ou full.", () => _options.RealTimeReflections, v => _options.RealTimeReflections = v, ["none", "economy", "full"]),
        Bool("Sistemas de partículas", "Chuva, neve, fumaça e partículas do veículo.", () => _options.ParticleSystems, v => _options.ParticleSystems = v),
        Integer("Máx. partículas por emissor", "Limite de partículas por sistema.", () => _options.MaximumParticlesPerEmitter, v => _options.MaximumParticlesPerEmitter = Math.Max(0, v)),
        Bool("Partículas somente no veículo do jogador", "Reduz partículas dos demais veículos.", () => _options.ParticleOnlyOwnVehicle, v => _options.ParticleOnlyOwnVehicle = v),
        Bool("Partículas em reflexos", "Renderiza partículas nos espelhos/reflexos.", () => _options.ParticleSystemsInReflections, v => _options.ParticleSystemsInReflections = v),
        Bool("Efeito de brilho do sol", "Equivale ao Sun Glow do OMSI.", () => _options.SunGlow, v => _options.SunGlow = v),
        Integer("Complexidade máxima de objetos", "Nível de detalhe/complexidade dos objetos.", () => _options.MaximumObjectComplexity, v => _options.MaximumObjectComplexity = Math.Clamp(v, 0, 3)),
        Integer("Complexidade máxima do mapa", "Nível de detalhe do mapa.", () => _options.MaximumMapComplexity, v => _options.MaximumMapComplexity = Math.Clamp(v, 0, 3)),
        Bool("Stencil Buffer Effects", "Base para sombras e efeitos de chuva/água no OMSI.", () => _options.StencilBufferEffects, v => _options.StencilBufferEffects = v),
        Bool("Sombras", "Ativa sombras.", () => _options.Shadows, v => _options.Shadows = v),
        Bool("Reflexos de chuva", "Ativa reflexos em superfícies molhadas.", () => _options.RainReflections, v => _options.RainReflections = v),
        Bool("Pessoas nos reflexos de chuva", "Renderiza pessoas nas superfícies refletivas molhadas.", () => _options.HumansInRainReflections, v => _options.HumansInRainReflections = v)
    ];

    private IReadOnlyList<OptionDescriptor> AdvancedGraphicsOptions() =>
    [
        Number("Reflexo econômico abaixo de FPS", "Troca reflexos Full para Economy abaixo deste FPS.", () => _options.ReflectionEconomyBelowFps, v => _options.ReflectionEconomyBelowFps = v),
        Number("Reflexo Full acima de FPS", "Volta a reflexos Full acima deste FPS.", () => _options.ReflectionFullAboveFps, v => _options.ReflectionFullAboveFps = v),
        Number("Reduzir tiles abaixo de FPS", "Reduz tiles ativos abaixo deste FPS.", () => _options.ReduceTilesBelowFps, v => _options.ReduceTilesBelowFps = v),
        Number("Aumentar tiles acima de FPS", "Restaura tiles acima deste FPS.", () => _options.IncreaseTilesAboveFps, v => _options.IncreaseTilesAboveFps = v),
        Bool("Aspect ratio manual", "Usa proporção de tela definida manualmente.", () => _options.ManualAspectRatioEnabled, v => _options.ManualAspectRatioEnabled = v),
        Number("Aspect ratio", "Ex.: 1.778 para 16:9.", () => _options.ManualAspectRatio, v => _options.ManualAspectRatio = Math.Max(0.5, v)),
        Bool("Apenas texturas Low-Res", "Prioriza as versões de baixa resolução.", () => _options.OnlyLowResolutionTextures, v => _options.OnlyLowResolutionTextures = v),
        Bool("Limitar texturas a 256 px", "Exceto o próprio ônibus.", () => _options.LimitTexturesTo256, v => _options.LimitTexturesTo256 = v),
        Bool("Low-Res em objetos distantes", "Usa texturas reduzidas conforme a distância.", () => _options.LowResolutionTexturesAtDistance, v => _options.LowResolutionTexturesAtDistance = v),
        Number("Memória para texturas High-Res (MB)", "Limite para carregamento de texturas de alta resolução.", () => _options.HighResolutionTextureMemoryMb, v => _options.HighResolutionTextureMemoryMb = Math.Max(64, v)),
        Integer("Tamanho textura de reflexo", "Resolução das texturas de espelho/reflexo.", () => _options.RealTimeReflectionTextureSize, v => _options.RealTimeReflectionTextureSize = Math.Clamp(v, 64, 4096)),
        Bool("Canal Night Map", "Canal de material noturno.", () => _options.MaterialNightMap, v => _options.MaterialNightMap = v),
        Bool("Canal Light Map", "Canal de iluminação.", () => _options.MaterialLightMap, v => _options.MaterialLightMap = v),
        Bool("Canal Light Map do terreno", "Canal de iluminação do terreno.", () => _options.MaterialTerrainLightMap, v => _options.MaterialTerrainLightMap = v),
        Bool("Canal Reflection Map", "Canal de mapa de reflexão.", () => _options.MaterialReflectionMap, v => _options.MaterialReflectionMap = v),
        Bool("Canal Bump Map", "Canal de relevo/bump.", () => _options.MaterialBumpMap, v => _options.MaterialBumpMap = v),
        Integer("Filtro de textura", "Valor de compatibilidade texFilter.", () => _options.TextureFilterMode, v => _options.TextureFilterMode = v),
        Integer("Filtragem anisotrópica", "Nível de filtragem anisotrópica.", () => _options.AnisotropicFiltering, v => _options.AnisotropicFiltering = Math.Clamp(v, 1, 16))
    ];

    private IReadOnlyList<OptionDescriptor> SoundOptions() =>
    [
        Integer("Volume geral (%)", "Volume mestre de todos os efeitos sonoros.", () => _options.MasterVolumePercent, v => _options.MasterVolumePercent = Math.Clamp(v, 0, 100)),
        Integer("Efeito estéreo", "Separação estéreo compatível com o OMSI.", () => _options.StereoEffect, v => _options.StereoEffect = Math.Clamp(v, 0, 100)),
        Integer("Máximo de sons", "Quantidade máxima de sons simultâneos.", () => _options.MaximumSoundCount, v => _options.MaximumSoundCount = Math.Max(0, v)),
        Bool("Efeito Doppler", "Ativa alteração de frequência por movimento relativo.", () => _options.DopplerEffect, v => _options.DopplerEffect = v),
        Bool("Sons de veículos IA", "Ativa sons dos demais veículos.", () => _options.AiVehicleSounds, v => _options.AiVehicleSounds = v),
        Bool("Sons do cenário", "Ativa sons ambientais e de objetos do mapa.", () => _options.ScenerySounds, v => _options.ScenerySounds = v),
        Bool("Reverb", "Ativa efeitos de reverberação.", () => _options.ReverbEffects, v => _options.ReverbEffects = v)
    ];

    private IReadOnlyList<OptionDescriptor> TrafficOptions() =>
    [
        Integer("Veículos não programados", "Máximo de carros/caminhões IA sem horário.", () => _options.MaximumUnscheduledTraffic, v => _options.MaximumUnscheduledTraffic = Math.Max(0, v)),
        Integer("Fator de tráfego rodoviário (%)", "Multiplicador de tráfego não programado.", () => _options.RoadTrafficFactorPercent, v => _options.RoadTrafficFactorPercent = Math.Clamp(v, 0, 200)),
        Integer("Carros estacionados (%)", "Percentual de vagas ocupadas.", () => _options.ParkedCarsPercent, v => _options.ParkedCarsPercent = Math.Clamp(v, 0, 100)),
        Integer("Máximo de pessoas", "Limite total de pedestres e passageiros.", () => _options.MaximumPeople, v => _options.MaximumPeople = Math.Max(0, v)),
        Integer("Passageiros nas paradas (%)", "Multiplicador da quantidade de passageiros.", () => _options.PassengerFactorPercent, v => _options.PassengerFactorPercent = Math.Clamp(v, 0, 300)),
        Integer("Veículos programados", "Máximo de ônibus/trens/trams IA com horários.", () => _options.MaximumScheduledTraffic, v => _options.MaximumScheduledTraffic = Math.Max(0, v)),
        Integer("Prioridade do tráfego programado", "1 a 4, como no OMSI.", () => _options.ScheduledTrafficPriority, v => _options.ScheduledTrafficPriority = Math.Clamp(v, 1, 4)),
        Bool("Usar lista IA reduzida", "Limita a variedade de veículos IA.", () => _options.UseReducedAiList, v => _options.UseReducedAiList = v)
    ];

    private IReadOnlyList<OptionDescriptor> RuntimeOptions() =>
    [
        Bool("VSync", "Sincronização vertical do renderer x64.", () => _options.RuntimeVSync, v => _options.RuntimeVSync = v),
        Bool("Tela cheia sem bordas", "Abre o runtime em borderless fullscreen.", () => _options.RuntimeBorderlessFullscreen, v => _options.RuntimeBorderlessFullscreen = v),
        Integer("Raio de streaming", "Raio de tiles carregados pelo runtime x64.", () => _options.RuntimeStreamingRadius, v => _options.RuntimeStreamingRadius = Math.Clamp(v, 0, 8)),
        Bool("Preferir GPU de hardware", "Tenta Direct3D em hardware antes do fallback WARP.", () => _options.RuntimePreferHardwareGpu, v => _options.RuntimePreferHardwareGpu = v),
        Bool("Mostrar FPS", "Mostra contador de FPS na janela do runtime.", () => _options.RuntimeShowFps, v => _options.RuntimeShowFps = v),
        Bool("Diagnóstico detalhado", "Mantém logs detalhados de carregamento e renderização.", () => _options.RuntimeDiagnostics, v => _options.RuntimeDiagnostics = v)
    ];

    private OptionDescriptor Bool(
        string label,
        string description,
        Func<bool> read,
        Action<bool> write) =>
        new(
            label,
            description,
            typeof(bool),
            () => read(),
            value =>
                write(
                    Convert.ToBoolean(
                        value)));

    private OptionDescriptor Integer(
        string label,
        string description,
        Func<int> read,
        Action<int> write) =>
        new(
            label,
            description,
            typeof(int),
            () => read(),
            value =>
                write(
                    Convert.ToInt32(
                        value)));

    private OptionDescriptor Number(
        string label,
        string description,
        Func<double> read,
        Action<double> write) =>
        new(
            label,
            description,
            typeof(double),
            () => read(),
            value =>
                write(
                    Convert.ToDouble(
                        value)));

    private OptionDescriptor Text(
        string label,
        string description,
        Func<string> read,
        Action<string> write) =>
        new(
            label,
            description,
            typeof(string),
            () => read(),
            value =>
                write(
                    value?.ToString() ??
                    ""));

    private OptionDescriptor Choice(
        string label,
        string description,
        Func<string> read,
        Action<string> write,
        IReadOnlyList<string> choices) =>
        new(
            label,
            description,
            typeof(string),
            () => read(),
            value =>
                write(
                    value?.ToString() ??
                    ""),
            choices);

    private string[] DiscoverLanguages()
    {
        var inputs =
            Path.Combine(
                _contentRoot,
                "Inputs");

        if (!Directory.Exists(inputs))
        {
            return
            [
                "PTBR",
                "ENG",
                "DEU"
            ];
        }

        var values =
            Directory
                .EnumerateFiles(
                    inputs,
                    "*.kyb",
                    SearchOption.TopDirectoryOnly)
                .Select(
                    Path.GetFileNameWithoutExtension)
                .Where(
                    static value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                .Select(
                    static value =>
                        value!)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    static value =>
                        value,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (!values.Contains(
                _options.Language,
                StringComparer.OrdinalIgnoreCase))
        {
            values.Insert(
                0,
                _options.Language);
        }

        return values.ToArray();
    }

    private void LoadPresetList()
    {
        _presetBox.Items.Clear();

        var directory =
            Path.Combine(
                _contentRoot,
                "option_presets");

        if (!Directory.Exists(
                directory))
        {
            _presetBox.Items.Add(
                "(nenhum perfil encontrado)");
            _presetBox.SelectedIndex =
                0;
            _presetBox.Enabled =
                false;
            return;
        }

        foreach (var path in
                 Directory
                     .EnumerateFiles(
                         directory,
                         "*.oop",
                         SearchOption.TopDirectoryOnly)
                     .OrderBy(
                         Path.GetFileName,
                         StringComparer.OrdinalIgnoreCase))
        {
            _presetBox.Items.Add(
                new FileInfo(path));
        }

        _presetBox.DisplayMember =
            nameof(
                FileInfo.Name);

        if (_presetBox.Items.Count > 0)
        {
            _presetBox.SelectedIndex =
                0;
        }
    }

    private void ImportCurrentOmsiOptions()
    {
        var path =
            Path.Combine(
                _contentRoot,
                "options.cfg");

        if (!File.Exists(path))
        {
            MessageBox.Show(
                this,
                "options.cfg não foi encontrado na pasta selecionada.",
                "Configurações",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        _options =
            OmsiRuntimeOptions.ImportFromFile(
                path,
                _options);

        RebuildOptionTabs();

        SetStatus(
            "options.cfg importado para o perfil do Runtime x64.");
    }

    private void ImportSelectedPreset()
    {
        if (_presetBox.SelectedItem is not
            FileInfo file)
        {
            return;
        }

        _options =
            OmsiRuntimeOptions.ImportFromFile(
                file.FullName,
                _options);

        RebuildOptionTabs();

        SetStatus(
            $"Perfil importado: {file.Name}");
    }

    private void LoadInputFiles()
    {
        LoadEditor(
            _keyboardEditor,
            Path.Combine(
                _contentRoot,
                "Inputs",
                "keyboard.cfg"));

        LoadEditor(
            _controllerEditor,
            Path.Combine(
                _contentRoot,
                "Inputs",
                "gamectrler.cfg"));
    }

    private void LoadEditor(
        RichTextBox editor,
        string path)
    {
        try
        {
            editor.Text =
                File.Exists(path)
                    ? File.ReadAllText(path)
                    : "";

            SetStatus(
                File.Exists(path)
                    ? $"Carregado: {path}"
                    : $"Arquivo não encontrado: {path}");
        }
        catch (Exception ex)
        {
            SetStatus(
                ex.Message);
        }
    }

    private void SaveInputFiles()
    {
        var inputs =
            Path.Combine(
                _contentRoot,
                "Inputs");

        if (!Directory.Exists(inputs))
        {
            MessageBox.Show(
                this,
                "A pasta Inputs não foi encontrada.",
                "Configurações",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        var result =
            MessageBox.Show(
                this,
                "Salvar os editores de Teclado e Game Controller diretamente na pasta Inputs do OMSI?\n\nUma cópia .runtime-backup será criada antes da gravação.",
                "Salvar Inputs",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

        if (result !=
            DialogResult.Yes)
        {
            return;
        }

        SaveWithBackup(
            Path.Combine(
                inputs,
                "keyboard.cfg"),
            _keyboardEditor.Text);

        SaveWithBackup(
            Path.Combine(
                inputs,
                "gamectrler.cfg"),
            _controllerEditor.Text);

        SetStatus(
            "Inputs salvos com backup.");
    }

    private static void SaveWithBackup(
        string path,
        string text)
    {
        if (File.Exists(path))
        {
            File.Copy(
                path,
                path + ".runtime-backup",
                overwrite: true);
        }

        File.WriteAllText(
            path,
            text);
    }

    private void OpenOmsiFolder()
    {
        OpenPath(
            _contentRoot);
    }

    private static void OpenPath(
        string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
    }

    private Button CreateButton(
        string text)
    {
        var button =
            new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        31,
                        39,
                        50),
                ForeColor =
                    Color.White,
                Height = 38,
                Margin =
                    new Padding(
                        4)
            };

        button.FlatAppearance.BorderColor =
            Color.FromArgb(
                57,
                72,
                90);

        return button;
    }

    private static string FormatValue(
        object? value) =>
        value switch
        {
            double number =>
                number.ToString(
                    "0.###",
                    System.Globalization.CultureInfo.InvariantCulture),
            _ =>
                value?.ToString() ??
                ""
        };

    private void SetStatus(
        string text)
    {
        _status.Text =
            text;
    }
}
