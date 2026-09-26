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
    private readonly RuntimeProcessHost _runtime =
        new();

    private readonly LauncherSettings _settings =
        LauncherSettings.Load();

    private IReadOnlyList<OmsiMapInfo> _maps =
        Array.Empty<OmsiMapInfo>();

    private IReadOnlyList<OmsiBusInfo> _buses =
        Array.Empty<OmsiBusInfo>();

    private IReadOnlyList<OmsiMapEntryPointGroup> _entryPoints =
        Array.Empty<OmsiMapEntryPointGroup>();

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
    }

    private void HomeNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        HomeView.Visibility =
            Visibility.Visible;

        SessionView.Visibility =
            Visibility.Collapsed;

        UpdateHomeSummary();
    }

    private void NewSessionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        HomeView.Visibility =
            Visibility.Collapsed;

        SessionView.Visibility =
            Visibility.Visible;
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
        var skins =
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

        SkinBox.ItemsSource =
            skins;

        SkinBox.SelectedItem =
            preferredBus is not null &&
            skins.Contains(
                preferredBus)
                ? preferredBus
                : skins.FirstOrDefault();

        UpdateBusPreview();
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

        var bus =
            SelectedBus();

        BusPreviewTitle.Text =
            bus?.Modelo ??
            "Nenhum ônibus selecionado";

        BusPreviewSubtitle.Text =
            bus?.Carroceria ??
            "Escolha carroceria e modelo";

        BusPreviewSkin.Text =
            bus is null
                ? string.Empty
                : $"Skin: {bus.Skin}";

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
                    spawn.Name))
            {
                throw new InvalidOperationException(
                    "O runtime não pôde ser iniciado.");
            }
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

        var spawn =
            SelectedSpawn();

        LastMapText.Text =
            map?.FolderName ??
            "Nenhum mapa selecionado";

        LastBusText.Text =
            NoBusCheckBox.IsChecked ==
                    true
                ? "Sem ônibus"
                : bus?.Modelo ??
                  "Nenhum veículo selecionado";

        LastSpawnText.Text =
            spawn?.Name ??
            "Nenhum ponto inicial";

        LastTimeText.Text =
            DateTime.Now.ToString(
                "HH:mm");

        LastSessionDateText.Text =
            DateTime.Now.ToString(
                "dd MMM yyyy · HH:mm");

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

        ApplyImage(
            LastSessionImage,
            mapImage);

        CompatibilityStatusText.Text =
            _maps.Count ==
                    0 &&
                _buses.Count ==
                    0
                ? "Aguardando conteúdo"
                : "Boa";
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

    private OmsiBusInfo? SelectedBus() =>
        SkinBox.SelectedItem
            as OmsiBusInfo;

    private OmsiMapEntryPointGroup? SelectedSpawn() =>
        SpawnBox.SelectedItem
            as OmsiMapEntryPointGroup;

    private void ClearSelections()
    {
        _maps =
            Array.Empty<OmsiMapInfo>();

        _buses =
            Array.Empty<OmsiBusInfo>();

        _entryPoints =
            Array.Empty<OmsiMapEntryPointGroup>();

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

        UpdateHero(null);
    }

    private void SaveSettings()
    {
        new LauncherSettings(
            ContentPathBox.Text,
            SelectedMap()?.FolderName,
            SelectedBus()?.RelativePath,
            SelectedSpawn()?.Name,
            NoBusCheckBox.IsChecked ==
                true)
            .Save();
    }

    private void SetStatus(
        string text)
    {
        AppendLog(
            text);
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
