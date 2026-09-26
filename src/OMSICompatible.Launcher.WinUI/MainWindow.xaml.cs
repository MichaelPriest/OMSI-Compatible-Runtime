using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Vehicles;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OMSICompatible.Launcher.WinUI;

public sealed partial class MainWindow :
    Window
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
    }

    private sealed record MapLibraryCard(
        OmsiMapInfo Map,
        string Title,
        string Detail,
        BitmapImage? Preview);

    private sealed record VehicleLibraryCard(
        OmsiBusInfo Bus,
        string Title,
        string Subtitle,
        string Detail,
        BitmapImage? Preview);

    private readonly RuntimeProcessHost _runtime =
        new();

    private LauncherSettings _settings =
        LauncherSettings.Load();

    private IReadOnlyList<OmsiMapInfo> _maps =
        Array.Empty<OmsiMapInfo>();

    private IReadOnlyList<OmsiBusInfo> _buses =
        Array.Empty<OmsiBusInfo>();

    private IReadOnlyList<MapLibraryCard> _mapLibraryCards =
        Array.Empty<MapLibraryCard>();

    private IReadOnlyList<VehicleLibraryCard> _vehicleLibraryCards =
        Array.Empty<VehicleLibraryCard>();

    private readonly Dictionary<
        string,
        IReadOnlyList<OmsiVehicleRepaint>>
        _repaintCache =
            new(
                StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<OmsiMapEntryPointGroup> _entryPoints =
        Array.Empty<OmsiMapEntryPointGroup>();

    private int _sceneryObjectCount;

    private bool _refreshing;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.Resize(
            new SizeInt32(
                1600,
                900));

        _runtime.OutputReceived +=
            RuntimeOutputReceived;

        _runtime.Exited +=
            RuntimeExited;

        Closed +=
            (_, _) =>
                _runtime.Dispose();

        Activated +=
            OnFirstActivated;
    }

    private async void OnFirstActivated(
        object sender,
        WindowActivatedEventArgs args)
    {
        Activated -=
            OnFirstActivated;

        _refreshing = true;
        NoBusCheckBox.IsChecked =
            _settings.StartWithoutBus;
        _refreshing = false;

        var initial =
            _settings.ContentPath;

        if (!string.IsNullOrWhiteSpace(initial))
        {
            ContentPathBox.Text =
                initial;

            await RefreshContentAsync(
                _settings.MapName);
        }

        UpdateRuntimeStatusCards();
        UpdateHomeSummary();
    }

    private void HomeNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowView(
            HomeView,
            HomeNavButton);

        UpdateHomeSummary();
    }

    private void NewSessionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowView(
            SessionView,
            PlayNavButton);
    }

    private void MapsNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        UpdateLibraryViews();

        ShowView(
            MapsView,
            MapsNavButton);
    }

    private void VehiclesNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        UpdateLibraryViews();

        ShowView(
            VehiclesView,
            VehiclesNavButton);
    }

    private void CompatibilityNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        UpdateCompatibilityAndDiagnostics();

        ShowView(
            CompatibilityView,
            CompatibilityNavButton);
    }

    private void DiagnosticsNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        UpdateCompatibilityAndDiagnostics();

        ShowView(
            DiagnosticsView,
            DiagnosticsNavButton);
    }

    private void ShowView(
        FrameworkElement visibleView,
        Button selectedButton)
    {
        FrameworkElement[] views =
        [
            HomeView,
            SessionView,
            MapsView,
            VehiclesView,
            CompatibilityView,
            DiagnosticsView
        ];

        foreach (var view in views)
        {
            view.Visibility =
                ReferenceEquals(
                    view,
                    visibleView)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        SetNavigationSelection(
            selectedButton);
    }

    private void SetNavigationSelection(
        Button selectedButton)
    {
        Button[] primaryButtons =
        [
            HomeNavButton,
            PlayNavButton,
            MapsNavButton,
            VehiclesNavButton,
            CompatibilityNavButton,
            DiagnosticsNavButton
        ];

        foreach (var button in primaryButtons)
        {
            var selected =
                ReferenceEquals(
                    button,
                    selectedButton);

            button.Background =
                new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(
                        selected
                            ? (byte)255
                            : (byte)0,
                        23,
                        71,
                        153));

            button.BorderBrush =
                new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(
                        selected
                            ? (byte)255
                            : (byte)0,
                        36,
                        87,
                        175));

            button.BorderThickness =
                selected
                    ? new Thickness(1)
                    : new Thickness(0);

            button.CornerRadius =
                new CornerRadius(10);
        }
    }

    private void ContinueButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (PlayButton.IsEnabled)
        {
            PlayButton_Click(
                sender,
                e);

            return;
        }

        NewSessionButton_Click(
            sender,
            e);
    }

    private async void BrowseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var picker =
            new FolderPicker();

        picker.FileTypeFilter.Add(
            "*");

        InitializeWithWindow.Initialize(
            picker,
            WindowNative.GetWindowHandle(
                this));

        var folder =
            await picker.PickSingleFolderAsync();

        if (folder is null)
        {
            return;
        }

        ContentPathBox.Text =
            folder.Path;

        await RefreshContentAsync(
            null);
    }

    private async void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await RefreshContentAsync(
            SelectedMap()?.FolderName);
    }

    private async Task RefreshContentAsync(
        string? selectMap)
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        PlayButton.IsEnabled = false;

        try
        {
            if (!OmsiContentRoot.TryCreate(
                    ContentPathBox.Text,
                    out var contentRoot,
                    out var error) ||
                contentRoot is null)
            {
                SetStatus(
                    error);

                ClearSelections();
                return;
            }

            _repaintCache.Clear();

            SetStatus(
                "Descobrindo mapas e ônibus...");

            var discovery =
                await Task.Run(
                    () =>
                    {
                        var maps =
                            MapDiscovery.Discover(
                                contentRoot);

                        var buses =
                            BusDiscovery.Discover(
                                contentRoot);

                        var objectCount =
                            CountSceneryObjects(
                                contentRoot);

                        return (
                            Maps: maps,
                            Buses: buses,
                            ObjectCount:
                                objectCount);
                    });

            _maps =
                discovery.Maps;

            _buses =
                discovery.Buses;

            _sceneryObjectCount =
                discovery.ObjectCount;

            RebuildLibraryCards();
            UpdateLibraryViews();

            MapBox.ItemsSource =
                _maps;

            MapCountText.Text =
                _maps.Count.ToString(
                    "N0");

            BusCountText.Text =
                _buses.Count.ToString(
                    "N0");

            ObjectCountText.Text =
                discovery.ObjectCount.ToString(
                    "N0");

            FooterMapCountText.Text =
                _maps.Count.ToString(
                    "N0");

            FooterBusCountText.Text =
                _buses.Count.ToString(
                    "N0");

            FooterObjectCountText.Text =
                discovery.ObjectCount.ToString(
                    "N0");

            var map =
                _maps.FirstOrDefault(
                    item =>
                        string.Equals(
                            item.FolderName,
                            selectMap,
                            StringComparison.OrdinalIgnoreCase))
                ?? _maps.FirstOrDefault();

            MapBox.SelectedItem =
                map;

            var bus =
                _buses.FirstOrDefault(
                    item =>
                        string.Equals(
                            item.RelativePath,
                            _settings.BusRelativePath,
                            StringComparison.OrdinalIgnoreCase))
                ?? _buses.FirstOrDefault();

            SelectBus(
                bus);

            ApplyNoBusMode();

            if (map is not null)
            {
                await LoadEntryPointsAsync(
                    map);
            }

            UpdatePlayAvailability();
            UpdateHomeSummary();
            UpdateCompatibilityAndDiagnostics();

            SetStatus(
                $"{_maps.Count:N0} mapa(s) · {_buses.Count:N0} ônibus.");
        }
        catch (Exception ex)
        {
            SetStatus(
                ex.Message);
        }
        finally
        {
            _refreshing = false;
            SaveSettings();
        }
    }

    private async void MapBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var map =
            SelectedMap();

        UpdateHero(
            map);

        if (_refreshing ||
            map is null)
        {
            UpdatePlayAvailability();
            return;
        }

        await LoadEntryPointsAsync(
            map);

        UpdatePlayAvailability();
        SaveSettings();
    }

    private void SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_refreshing)
        {
            return;
        }

        UpdatePlayAvailability();
        UpdateHomeSummary();
        SaveSettings();
    }

    private void CarroceriaBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_refreshing)
        {
            return;
        }

        PopulateModels(
            CarroceriaBox.SelectedItem
                as string,
            preferredBus: null);
    }

    private void ModeloBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_refreshing)
        {
            return;
        }

        PopulateSkins(
            CarroceriaBox.SelectedItem
                as string,
            ModeloBox.SelectedItem
                as string,
            preferredBus: null);
    }

    private void SkinBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateBusPreview();

        if (_refreshing)
        {
            return;
        }

        UpdatePlayAvailability();
        UpdateHomeSummary();
        SaveSettings();
    }

    private void NoBusModeChanged(
        object sender,
        RoutedEventArgs e)
    {
        ApplyNoBusMode();

        if (_refreshing)
        {
            return;
        }

        UpdatePlayAvailability();
        UpdateHomeSummary();
        SaveSettings();
    }

    private void SelectBus(
        OmsiBusInfo? bus)
    {
        var previousRefreshing =
            _refreshing;

        _refreshing = true;

        try
        {
            var carrocerias =
                _buses
                    .Select(
                        static item =>
                            item.Carroceria)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(
                        static value =>
                            value,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            CarroceriaBox.ItemsSource =
                carrocerias;

            var carroceria =
                bus?.Carroceria ??
                carrocerias.FirstOrDefault();

            CarroceriaBox.SelectedItem =
                carrocerias.FirstOrDefault(
                    value =>
                        string.Equals(
                            value,
                            carroceria,
                            StringComparison.OrdinalIgnoreCase));

            PopulateModels(
                carroceria,
                bus);
        }
        finally
        {
            _refreshing =
                previousRefreshing;
        }

        UpdateBusPreview();
    }

    private void PopulateModels(
        string? carroceria,
        OmsiBusInfo? preferredBus)
    {
        var modelos =
            string.IsNullOrWhiteSpace(
                carroceria)
                ? Array.Empty<string>()
                : _buses
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
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

        ModeloBox.ItemsSource =
            modelos;

        var model =
            preferredBus?.Modelo ??
            modelos.FirstOrDefault();

        ModeloBox.SelectedItem =
            modelos.FirstOrDefault(
                value =>
                    string.Equals(
                        value,
                        model,
                        StringComparison.OrdinalIgnoreCase));

        PopulateSkins(
            carroceria,
            model,
            preferredBus);
    }

    private void PopulateSkins(
        string? carroceria,
        string? modelo,
        OmsiBusInfo? preferredBus)
    {
        var buses =
            string.IsNullOrWhiteSpace(
                carroceria) ||
            string.IsNullOrWhiteSpace(
                modelo)
                ? Array.Empty<OmsiBusInfo>()
                : _buses
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
            new List<BusSkinSelection>();

        foreach (var bus in buses)
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

        var visibleSelections =
            selections
                .GroupBy(
                    static item =>
                        item.Bus.RelativePath +
                        "\n" +
                        item.Name +
                        "\n" +
                        (item.Repaint?.RelativeCtiPath ??
                         string.Empty),
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    static group =>
                        group.First())
                .OrderBy(
                    static item =>
                        item.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        SkinBox.ItemsSource =
            visibleSelections;

        BusSkinSelection? target =
            null;

        if (preferredBus is not null)
        {
            target =
                visibleSelections.FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Bus.RelativePath,
                            preferredBus.RelativePath,
                            StringComparison.OrdinalIgnoreCase) &&
                        RepaintMatchesSavedSelection(
                            item.Repaint));
        }

        target ??=
            visibleSelections.FirstOrDefault(
                item =>
                    preferredBus is not null &&
                    string.Equals(
                        item.Bus.RelativePath,
                        preferredBus.RelativePath,
                        StringComparison.OrdinalIgnoreCase) &&
                    item.Repaint is null);

        target ??=
            visibleSelections.FirstOrDefault();

        SkinBox.SelectedItem =
            target;

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
        var noBus =
            NoBusCheckBox.IsChecked ==
            true;

        CarroceriaBox.IsEnabled =
            !noBus;

        ModeloBox.IsEnabled =
            !noBus;

        SkinBox.IsEnabled =
            !noBus;

        UpdateBusPreview();
    }

    private void UpdateBusPreview()
    {
        if (NoBusCheckBox.IsChecked ==
            true)
        {
            BusPreviewTitle.Text =
                "Iniciar sem ônibus";
            BusPreviewSubtitle.Text =
                "O mapa será aberto em câmera livre";
            BusPreviewSkin.Text =
                "Nenhum veículo será carregado";
            BusPreviewImage.Source =
                null;
            BusPreviewEmptyText.Text =
                "Modo mapa";
            BusPreviewEmptyText.Visibility =
                Visibility.Visible;

            UpdateHomeSummary();
            return;
        }

        var selection =
            SelectedSkinSelection();

        var bus =
            selection?.Bus;

        BusPreviewTitle.Text =
            bus?.Modelo ??
            "Nenhum ônibus selecionado";

        BusPreviewSubtitle.Text =
            selection?.Detail ??
            "Escolha carroceria, modelo e skin/repaint";

        BusPreviewSkin.Text =
            selection is null
                ? string.Empty
                : selection.Repaint is null
                    ? $"Skin base: {bus!.Skin}"
                    : $"Repaint CTI: {selection.Repaint.Name}";

        ApplyImage(
            BusPreviewImage,
            bus?.PreviewImagePath);

        BusPreviewEmptyText.Text =
            "Sem imagem de prévia";

        BusPreviewEmptyText.Visibility =
            string.IsNullOrWhiteSpace(
                bus?.PreviewImagePath)
                ? Visibility.Visible
                : Visibility.Collapsed;

        UpdateHomeSummary();
    }

    private async Task LoadEntryPointsAsync(
        OmsiMapInfo map)
    {
        SpawnBox.ItemsSource = null;
        _entryPoints =
            Array.Empty<OmsiMapEntryPointGroup>();

        SetStatus(
            "Lendo pontos iniciais do mapa...");

        _entryPoints =
            await Task.Run(
                () =>
                    MapEntryPointDiscovery.Discover(
                        map));

        SpawnBox.ItemsSource =
            _entryPoints;

        var spawn =
            _entryPoints.FirstOrDefault(
                item =>
                    string.Equals(
                        item.Name,
                        _settings.EntryPointName,
                        StringComparison.OrdinalIgnoreCase))
            ?? _entryPoints.FirstOrDefault();

        SpawnBox.SelectedItem =
            spawn;

        UpdateHomeSummary();
    }

    private void PlayButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_runtime.IsRunning)
        {
            return;
        }

        var map =
            SelectedMap();

        var noBus =
            NoBusCheckBox.IsChecked ==
            true;

        var bus =
            SelectedBus();

        var repaint =
            noBus
                ? null
                : SelectedRepaint();

        var spawn =
            SelectedSpawn();

        if (map is null ||
            spawn is null ||
            (!noBus && bus is null))
        {
            SetStatus(
                noBus
                    ? "Selecione mapa e ponto inicial."
                    : "Selecione mapa, ônibus e ponto inicial.");
            return;
        }

        try
        {
            SaveSettings();

            ShowLoading(
                map,
                noBus
                    ? null
                    : bus);

            SetRuntimeProgress(
                new RuntimeProgress(
                    1,
                    "Inicializando",
                    "Abrindo o runtime x64..."));

            if (!_runtime.Start(
                    ContentPathBox.Text,
                    map.FolderName,
                    noBus
                        ? null
                        : bus?.RelativePath,
                    spawn.Name,
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
                        spawn.Name,
                    LastSessionWithoutBus =
                        noBus,
                    LastSessionStartedAt =
                        DateTimeOffset.Now
                };

            SaveSettings();
            UpdateHomeSummary();
            UpdateRuntimeStatusCards();
        }
        catch (Exception ex)
        {
            HideLoading();
            SetStatus(
                ex.Message);
        }
    }

    private void RuntimeOutputReceived(
        string line)
    {
        DispatcherQueue.TryEnqueue(
            () =>
            {
                if (string.Equals(
                        line,
                        "[runtime-select-bus]",
                        StringComparison.Ordinal))
                {
                    NoBusCheckBox.IsChecked =
                        false;

                    Activate();

                    CarroceriaBox.Focus(
                        FocusState.Programmatic);

                    SetStatus(
                        "Selecione o próximo ônibus e pressione JOGAR.");
                    return;
                }

                if (RuntimeProgress.TryParse(
                        line,
                        out var progress) &&
                    progress is not null)
                {
                    SetRuntimeProgress(
                        progress);
                    return;
                }

                if (string.Equals(
                        line,
                        "[runtime-ready]",
                        StringComparison.Ordinal))
                {
                    SetRuntimeProgress(
                        new RuntimeProgress(
                            100,
                            "Pronto",
                            "Entrando no mundo..."));

                    HideLoading();
                    SetStatus(
                        "Em execução");
                    UpdateRuntimeStatusCards();
                    return;
                }

                AppendLog(
                    line);
            });
    }

    private void RuntimeExited(
        int exitCode)
    {
        DispatcherQueue.TryEnqueue(
            () =>
            {
                HideLoading();

                SetStatus(
                    exitCode == 0
                        ? "Runtime encerrado."
                        : $"Runtime encerrado com erro {exitCode}.");

                UpdatePlayAvailability();
                UpdateRuntimeStatusCards();
            });
    }

    private void ShowLoading(
        OmsiMapInfo map,
        OmsiBusInfo? bus)
    {
        LoadingMapText.Text =
            map.FolderName;

        LoadingBusText.Text =
            bus?.SelectionLabel ??
            "Sem ônibus · modo mapa";

        var image =
            ResolveMapImage(
                map.DirectoryPath);

        ApplyImage(
            LoadingImage,
            image);

        LoadingOverlay.Visibility =
            Visibility.Visible;

        PlayButton.IsEnabled =
            false;
    }

    private void HideLoading()
    {
        LoadingOverlay.Visibility =
            Visibility.Collapsed;

        UpdatePlayAvailability();
    }

    private void SetRuntimeProgress(
        RuntimeProgress progress)
    {
        LoadingProgress.Value =
            progress.Percent;

        LoadingPercentText.Text =
            $"{progress.Percent}%";

        LoadingStageText.Text =
            progress.Stage;

        LoadingDetailText.Text =
            progress.Detail;
    }

    private void UpdateHero(
        OmsiMapInfo? map)
    {
        UpdateHomeSummary();
    }

    private void UpdateHomeSummary()
    {
        var map =
            SelectedMap();

        var bus =
            NoBusCheckBox.IsChecked ==
                    true
                ? null
                : SelectedBus();

        var mapImage =
            map is null
                ? null
                : ResolveMapImage(
                    map.DirectoryPath);

        var heroImage =
            !string.IsNullOrWhiteSpace(
                bus?.PreviewImagePath)
                ? bus!.PreviewImagePath
                : mapImage;

        ApplyImage(
            HeroImage,
            heroImage);

        var lastMap =
            _maps.FirstOrDefault(
                item =>
                    string.Equals(
                        item.FolderName,
                        _settings.LastSessionMapName,
                        StringComparison.OrdinalIgnoreCase));

        var lastBus =
            _buses.FirstOrDefault(
                item =>
                    string.Equals(
                        item.RelativePath,
                        _settings.LastSessionBusRelativePath,
                        StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(
                         _settings.LastSessionSkin) ||
                     string.Equals(
                         item.Skin,
                         _settings.LastSessionSkin,
                         StringComparison.OrdinalIgnoreCase)));

        var hasLastSession =
            _settings.LastSessionStartedAt
                is not null;

        LastMapText.Text =
            hasLastSession
                ? lastMap?.FolderName ??
                  _settings.LastSessionMapName ??
                  "Mapa indisponível"
                : "Nenhuma sessão iniciada";

        LastBusText.Text =
            !hasLastSession
                ? "—"
                : _settings.LastSessionWithoutBus
                    ? "Sem ônibus"
                    : lastBus is null
                        ? "Veículo indisponível"
                        : string.IsNullOrWhiteSpace(
                              _settings.LastSessionRepaintName)
                            ? lastBus.Modelo
                            : $"{lastBus.Modelo} · {_settings.LastSessionRepaintName}";

        LastSpawnText.Text =
            hasLastSession
                ? _settings.LastSessionEntryPointName ??
                  "Ponto não registrado"
                : "—";

        var localStartedAt =
            _settings.LastSessionStartedAt
                ?.ToLocalTime();

        LastTimeText.Text =
            localStartedAt?.ToString(
                "HH:mm") ??
            "—";

        LastSessionDateText.Text =
            localStartedAt?.ToString(
                "dd MMM yyyy · HH:mm") ??
            "Nenhuma sessão";

        var lastMapImage =
            lastMap is null
                ? null
                : ResolveMapImage(
                    lastMap.DirectoryPath);

        ApplyImage(
            LastSessionImage,
            lastMapImage);

        CompatibilityStatusText.Text =
            _maps.Count ==
                    0 &&
                _buses.Count ==
                    0
                ? "Aguardando conteúdo"
                : "Conteúdo carregado";

        UpdateRuntimeStatusCards();
    }

    private static int CountSceneryObjects(
        OmsiContentRoot contentRoot)
    {
        try
        {
            return Directory.Exists(
                    contentRoot.SceneryObjectsPath)
                ? Directory
                    .EnumerateFiles(
                        contentRoot.SceneryObjectsPath,
                        "*.sco",
                        SearchOption.AllDirectories)
                    .Count()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string? ResolveMapImage(
        string mapDirectory)
    {
        string[] candidates =
        [
            "picture.jpg",
            "picture.jpeg",
            "picture.png",
            "preview.jpg",
            "preview.png",
            "picture.bmp"
        ];

        return candidates
            .Select(
                name =>
                    Path.Combine(
                        mapDirectory,
                        name))
            .FirstOrDefault(
                File.Exists);
    }

    private static void ApplyImage(
        Image target,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path) ||
            !File.Exists(path))
        {
            target.Source = null;
            return;
        }

        try
        {
            target.Source =
                new BitmapImage(
                    new Uri(
                        path));
        }
        catch
        {
            target.Source = null;
        }
    }

    private void UpdatePlayAvailability()
    {
        var noBus =
            NoBusCheckBox.IsChecked ==
            true;

        var canPlay =
            !_runtime.IsRunning &&
            SelectedMap() is not null &&
            SelectedSpawn() is not null &&
            (noBus ||
             SelectedBus() is not null);

        PlayButton.IsEnabled =
            canPlay;

        ContinueButton.IsEnabled =
            canPlay;
    }

    private OmsiMapInfo? SelectedMap() =>
        MapBox.SelectedItem
            as OmsiMapInfo;

    private BusSkinSelection? SelectedSkinSelection() =>
        SkinBox.SelectedItem
            as BusSkinSelection;

    private OmsiBusInfo? SelectedBus() =>
        SelectedSkinSelection()?.Bus;

    private OmsiVehicleRepaint? SelectedRepaint() =>
        SelectedSkinSelection()?.Repaint;

    private OmsiMapEntryPointGroup? SelectedSpawn() =>
        SpawnBox.SelectedItem
            as OmsiMapEntryPointGroup;

    private void ClearSelections()
    {
        _maps =
            Array.Empty<OmsiMapInfo>();

        _buses =
            Array.Empty<OmsiBusInfo>();

        _mapLibraryCards =
            Array.Empty<MapLibraryCard>();

        _vehicleLibraryCards =
            Array.Empty<VehicleLibraryCard>();

        _entryPoints =
            Array.Empty<OmsiMapEntryPointGroup>();

        _sceneryObjectCount = 0;

        _repaintCache.Clear();

        MapBox.ItemsSource = null;
        CarroceriaBox.ItemsSource = null;
        ModeloBox.ItemsSource = null;
        SkinBox.ItemsSource = null;
        SpawnBox.ItemsSource = null;

        UpdateBusPreview();

        MapCountText.Text = "0";
        BusCountText.Text = "0";
        ObjectCountText.Text = "0";

        FooterMapCountText.Text = "0";
        FooterBusCountText.Text = "0";
        FooterObjectCountText.Text = "0";

        UpdateLibraryViews();
        UpdateHero(null);
        UpdateCompatibilityAndDiagnostics();
    }

    private void RebuildLibraryCards()
    {
        _mapLibraryCards =
            _maps
                .Select(
                    map =>
                        new MapLibraryCard(
                            map,
                            map.FolderName,
                            $"global.cfg · {FormatBytes(map.GlobalConfigBytes)}",
                            CreateBitmapImage(
                                ResolveMapImage(
                                    map.DirectoryPath))))
                .ToArray();

        _vehicleLibraryCards =
            _buses
                .Select(
                    bus =>
                    {
                        var couplingDetail =
                            bus.CoupledBack is null
                                ? string.Empty
                                : " · acoplamento traseiro";

                        var scriptDetail =
                            $"{bus.ScriptManifest.RegisteredFileCount:N0} arquivo(s) de script · " +
                            $"{bus.ScriptManifest.MissingFileCount:N0} ausente(s)";

                        return new VehicleLibraryCard(
                            bus,
                            bus.Modelo,
                            $"{bus.Carroceria} · {bus.Skin}",
                            $"{bus.Physics.Axles.Count:N0} eixo(s) · {scriptDetail}{couplingDetail}",
                            CreateBitmapImage(
                                bus.PreviewImagePath));
                    })
                .ToArray();
    }

    private void UpdateLibraryViews()
    {
        var mapQuery =
            MapsSearchBox.Text
                ?.Trim();

        var maps =
            _mapLibraryCards
                .Where(
                    item =>
                        string.IsNullOrWhiteSpace(
                            mapQuery) ||
                        item.Title.Contains(
                            mapQuery,
                            StringComparison.OrdinalIgnoreCase) ||
                        item.Map.DirectoryPath.Contains(
                            mapQuery,
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        MapsGrid.ItemsSource =
            maps;

        MapsResultCountText.Text =
            $"{maps.Length:N0} " +
            (maps.Length == 1
                ? "mapa"
                : "mapas");

        var vehicleQuery =
            VehiclesSearchBox.Text
                ?.Trim();

        var vehicles =
            _vehicleLibraryCards
                .Where(
                    item =>
                        string.IsNullOrWhiteSpace(
                            vehicleQuery) ||
                        item.Title.Contains(
                            vehicleQuery,
                            StringComparison.OrdinalIgnoreCase) ||
                        item.Subtitle.Contains(
                            vehicleQuery,
                            StringComparison.OrdinalIgnoreCase) ||
                        item.Bus.RelativePath.Contains(
                            vehicleQuery,
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        VehiclesGrid.ItemsSource =
            vehicles;

        VehiclesResultCountText.Text =
            $"{vehicles.Length:N0} " +
            (vehicles.Length == 1
                ? "veículo"
                : "veículos");
    }

    private void MapsSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdateLibraryViews();
    }

    private void VehiclesSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdateLibraryViews();
    }

    private void MapLibraryItem_Click(
        object sender,
        ItemClickEventArgs e)
    {
        if (e.ClickedItem is not
            MapLibraryCard card)
        {
            return;
        }

        MapBox.SelectedItem =
            card.Map;

        ShowView(
            SessionView,
            PlayNavButton);

        SetStatus(
            $"Mapa selecionado: {card.Map.FolderName}");
    }

    private void VehicleLibraryItem_Click(
        object sender,
        ItemClickEventArgs e)
    {
        if (e.ClickedItem is not
            VehicleLibraryCard card)
        {
            return;
        }

        NoBusCheckBox.IsChecked =
            false;

        SelectBus(
            card.Bus);

        ApplyNoBusMode();
        UpdatePlayAvailability();
        UpdateHomeSummary();
        SaveSettings();

        ShowView(
            SessionView,
            PlayNavButton);

        SetStatus(
            $"Veículo selecionado: {card.Bus.SelectionLabel}");
    }

    private static string FormatBytes(
        long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes:N0} B";
        }

        if (bytes < 1024L * 1024L)
        {
            return $"{bytes / 1024d:N1} KB";
        }

        return $"{bytes / (1024d * 1024d):N1} MB";
    }

    private static BitmapImage? CreateBitmapImage(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path) ||
            !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new BitmapImage(
                new Uri(
                    path));
        }
        catch
        {
            return null;
        }
    }

    private void UpdateCompatibilityAndDiagnostics()
    {
        var runtimePath =
            _runtime.ResolveRuntimePath();

        var runtimeExists =
            File.Exists(
                runtimePath);

        var runtimeDirectory =
            Path.GetDirectoryName(
                runtimePath) ??
            AppContext.BaseDirectory;

        var odeAvailable =
            ContainsRuntimeFile(
                runtimeDirectory,
                "ode_single.dll");

        var rendererAvailable =
            ContainsRuntimeFile(
                runtimeDirectory,
                "OMSICompatible.Renderer.D3D11.dll");

        var missingScripts =
            _buses.Sum(
                static bus =>
                    bus.ScriptManifest.MissingFileCount);

        var missingModels =
            _buses.Count(
                static bus =>
                    string.IsNullOrWhiteSpace(
                        bus.ModelConfigPath) ||
                    !File.Exists(
                        bus.ModelConfigPath));

        CompatibilityMapCountText.Text =
            _maps.Count.ToString(
                "N0");

        CompatibilityVehicleCountText.Text =
            _buses.Count.ToString(
                "N0");

        CompatibilityObjectCountText.Text =
            _sceneryObjectCount.ToString(
                "N0");

        CompatibilityMissingScriptsText.Text =
            missingScripts.ToString(
                "N0");

        var contentPath =
            ContentPathBox.Text
                ?.Trim();

        var contentExists =
            !string.IsNullOrWhiteSpace(
                contentPath) &&
            Directory.Exists(
                contentPath);

        CompatibilityContentRootText.Text =
            contentExists
                ? $"Biblioteca: {contentPath}"
                : "Biblioteca: não configurada ou indisponível";

        CompatibilityRuntimeText.Text =
            runtimeExists
                ? $"Runtime: disponível · {runtimePath}"
                : $"Runtime: não encontrado · {runtimePath}";

        CompatibilityOdeText.Text =
            odeAvailable
                ? "ODE: ode_single.dll disponível"
                : "ODE: ode_single.dll não encontrado";

        CompatibilityRendererText.Text =
            rendererAvailable
                ? "D3D11: renderer disponível"
                : "D3D11: renderer não encontrado";

        CompatibilityModelText.Text =
            _buses.Count == 0
                ? "model.cfg: aguardando biblioteca de veículos"
                : missingModels == 0
                    ? $"model.cfg: {_buses.Count:N0}/{_buses.Count:N0} veículos com modelo"
                    : $"model.cfg: {missingModels:N0} veículo(s) sem modelo renderizável";

        DiagnosticsRuntimeText.Text =
            _runtime.IsRunning
                ? "Runtime em execução"
                : runtimeExists
                    ? "Runtime pronto"
                    : "Runtime não encontrado";

        DiagnosticsContentText.Text =
            contentExists
                ? contentPath!
                : "Não configurada";

        DiagnosticsTextBox.Text =
            BuildDiagnosticsText(
                runtimePath,
                runtimeExists,
                odeAvailable,
                rendererAvailable,
                missingScripts,
                missingModels,
                contentPath,
                contentExists);
    }

    private string BuildDiagnosticsText(
        string runtimePath,
        bool runtimeExists,
        bool odeAvailable,
        bool rendererAvailable,
        int missingScripts,
        int missingModels,
        string? contentPath,
        bool contentExists)
    {
        var lines =
            new List<string>
            {
                "OMSI Compatible Runtime - Diagnóstico",
                $"Gerado: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
                $"Launcher: WinUI 3 x64",
                $"Runtime em execução: {_runtime.IsRunning}",
                $"Runtime path: {runtimePath}",
                $"Runtime disponível: {runtimeExists}",
                $"ODE x64 single precision disponível: {odeAvailable}",
                $"Renderer D3D11 disponível: {rendererAvailable}",
                $"Biblioteca OMSI: {(contentExists ? contentPath : "indisponível")}",
                $"Mapas detectados: {_maps.Count:N0}",
                $"Veículos .bus detectados: {_buses.Count:N0}",
                $"Objetos .sco detectados: {_sceneryObjectCount:N0}",
                $"Referências de script ausentes: {missingScripts:N0}",
                $"Veículos sem model.cfg renderizável: {missingModels:N0}",
                $"PR de desenvolvimento: #2 / fix/runtime-visual-controls-pass"
            };

        if (_buses.Count > 0)
        {
            lines.Add(
                string.Empty);

            lines.Add(
                "Veículos com referências de script ausentes:");

            foreach (var bus in
                     _buses
                         .Where(
                             static item =>
                                 item.ScriptManifest.MissingFileCount >
                                 0)
                         .OrderByDescending(
                             static item =>
                                 item.ScriptManifest.MissingFileCount)
                         .ThenBy(
                             static item =>
                                 item.SelectionLabel,
                             StringComparer.OrdinalIgnoreCase)
                         .Take(100))
            {
                lines.Add(
                    $"- {bus.SelectionLabel}: {bus.ScriptManifest.MissingFileCount:N0} ausente(s)");
            }
        }

        return string.Join(
            Environment.NewLine,
            lines);
    }

    private void CopyDiagnosticsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        UpdateCompatibilityAndDiagnostics();

        var package =
            new Windows.ApplicationModel.DataTransfer.DataPackage();

        package.SetText(
            DiagnosticsTextBox.Text);

        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(
            package);

        SetStatus(
            "Diagnóstico copiado para a área de transferência.");
    }

    private void GenerateDiagnosticsReportButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        UpdateCompatibilityAndDiagnostics();

        try
        {
            var directory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "OMSI-Compatible-Runtime",
                    "Reports");

            Directory.CreateDirectory(
                directory);

            var path =
                Path.Combine(
                    directory,
                    $"diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            File.WriteAllText(
                path,
                DiagnosticsTextBox.Text);

            DiagnosticsReportPathText.Text =
                path;

            SetStatus(
                $"Relatório gerado: {path}");
        }
        catch (Exception ex)
        {
            SetStatus(
                $"Falha ao gerar relatório: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        _settings =
            _settings with
            {
                ContentPath =
                    ContentPathBox.Text,
                MapName =
                    SelectedMap()?.FolderName,
                BusRelativePath =
                    SelectedBus()?.RelativePath,
                RepaintName =
                    SelectedRepaint()?.Name,
                RepaintCtiRelativePath =
                    SelectedRepaint()?.RelativeCtiPath,
                EntryPointName =
                    SelectedSpawn()?.Name,
                StartWithoutBus =
                    NoBusCheckBox.IsChecked ==
                    true
            };

        _settings.Save();
    }

    private void UpdateRuntimeStatusCards()
    {
        var runtimePath =
            _runtime.ResolveRuntimePath();

        var runtimeExists =
            File.Exists(
                runtimePath);

        RuntimeStatusText.Text =
            _runtime.IsRunning
                ? "Em execução"
                : runtimeExists
                    ? "Pronto"
                    : "Não encontrado";

        var runtimeDirectory =
            Path.GetDirectoryName(
                runtimePath) ??
            AppContext.BaseDirectory;

        OdeStatusText.Text =
            ContainsRuntimeFile(
                runtimeDirectory,
                "ode_single.dll")
                ? "Disponível"
                : "Não encontrado";

        D3D11StatusText.Text =
            ContainsRuntimeFile(
                runtimeDirectory,
                "OMSICompatible.Renderer.D3D11.dll")
                ? "Disponível"
                : "Não encontrado";

        UpdateStatusText.Text =
            "Canal alpha";

        if (DiagnosticsView is not null &&
            CompatibilityView is not null)
        {
            UpdateCompatibilityAndDiagnostics();
        }
    }

    private static bool ContainsRuntimeFile(
        string root,
        string fileName)
    {
        try
        {
            return Directory.Exists(root) &&
                   Directory
                       .EnumerateFiles(
                           root,
                           fileName,
                           SearchOption.AllDirectories)
                       .Any();
        }
        catch
        {
            return false;
        }
    }

    private void SetStatus(
        string text)
    {
        FooterStatusTitle.Text =
            _runtime.IsRunning
                ? "Runtime"
                : "Launcher";

        FooterStatusDetail.Text =
            text;

        AppendLog(
            text);

        UpdateRuntimeStatusCards();
    }

    private void AppendLog(
        string text)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return;
        }

        if (LogBox.Text.Length > 0)
        {
            LogBox.Text +=
                Environment.NewLine;
        }

        LogBox.Text +=
            $"[{DateTime.Now:HH:mm:ss}] {text}";
    }
}
