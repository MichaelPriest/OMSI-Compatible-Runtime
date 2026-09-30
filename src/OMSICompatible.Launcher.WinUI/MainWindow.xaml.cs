using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Vehicles;
using OMSICompatible.Multiplayer;
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

    private sealed record HofSelection(
        string Name,
        string FullPath)
    {
        public override string ToString() => Name;
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

    private sealed record KeyboardDisplayRow(
        string Trigger,
        string Key,
        string Flags);

    private readonly RuntimeProcessHost _runtime =
        new();

    private LauncherSettings _settings =
        LauncherSettings.Load();

    private OmsiRuntimeOptions _runtimeOptions =
        OmsiRuntimeOptions.Load();

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

    private bool _libraryCardsDirty = true;
    private bool _contentDiscoveryRunning;
    private int _entryPointLoadVersion;
    private bool _refreshing;
    private bool _restartRuntimeAfterInGameBusSelection;
    private CancellationTokenSource? _embeddedPreviewCancellation;
    private nint _embeddedPreviewWindow;
    private string? _embeddedPreviewKey;

    private const int GwlStyle = -16;
    private const nint WsChild = 0x40000000;
    private const nint WsCaption = 0x00C00000;
    private const nint WsThickFrame = 0x00040000;
    private static readonly nint WsPopup = unchecked((nint)0x80000000);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const int SwHide = 0;
    private const int SwShowNa = 8;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(
        nint childWindow,
        nint newParentWindow);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(
        nint window,
        int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(
        nint window,
        int index,
        nint newValue);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(
        nint window,
        int command);

    public MainWindow(
        bool xamlSmokeOnly = false)
    {
        InitializeComponent();

        if (xamlSmokeOnly)
        {
            return;
        }

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
            {
                StopEmbeddedVehiclePreview();
                _runtime.Dispose();
            };

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

        LoadRuntimeOptionsIntoUi();
        LoadMultiplayerOptionsIntoUi();
        UpdateControlsView();
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
        EnsureLibraryCards();
        UpdateLibraryViews();

        ShowView(
            MapsView,
            MapsNavButton);
    }

    private void VehiclesNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        EnsureLibraryCards();
        UpdateLibraryViews();

        ShowView(
            VehiclesView,
            VehiclesNavButton);
    }

    private void MultiplayerNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _runtimeOptions =
            OmsiRuntimeOptions.Load();

        LoadMultiplayerOptionsIntoUi();

        ShowView(
            MultiplayerView,
            MultiplayerNavButton);
    }

    private void MultiplayerPlayButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ApplyMultiplayerOptionsFromUi(
            showStatus:
                false);

        ShowView(
            SessionView,
            PlayNavButton);
    }

    private async void DiscoverMultiplayerButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var port =
            int.TryParse(
                MultiplayerPortBox.Text,
                out var parsedPort) &&
            parsedPort is >= 1 and <=
                ushort.MaxValue
                ? parsedPort
                : OpenOmsiLanProtocol.DefaultPort;

        MultiplayerDiscoveryText.Text =
            "Procurando sessões openOMSI LAN...";

        try
        {
            var found =
                await Task.Run(
                    () =>
                        OpenOmsiLanSession.Discover(
                            TimeSpan.FromMilliseconds(
                                1400),
                            port));

            if (found is null)
            {
                MultiplayerDiscoveryText.Text =
                    $"Nenhuma sessão encontrada nas portas {port}-{Math.Min(port + OpenOmsiLanProtocol.PortRange - 1, ushort.MaxValue)}.";
                return;
            }

            MultiplayerJoinRadio.IsChecked =
                true;

            MultiplayerTargetBox.Text =
                $"{found.Endpoint.Address}:{found.Endpoint.Port}";

            MultiplayerDiscoveryText.Text =
                $"{found.HostName} · mapa {found.Map} · {found.Players} jogador(es) · sessão {OpenOmsiLanProtocol.SessionHex(found.Session)} · {found.Endpoint.Address}:{found.Endpoint.Port}";

            ApplyMultiplayerOptionsFromUi(
                showStatus:
                    false);

            SetStatus(
                $"Sessão LAN encontrada: {found.HostName}.");
        }
        catch (Exception exception)
        {
            MultiplayerDiscoveryText.Text =
                $"Falha ao descobrir LAN: {exception.Message}";
        }
    }

    private void SaveMultiplayerButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ApplyMultiplayerOptionsFromUi(
            showStatus:
                true);
    }

    private void ControlsNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        UpdateControlsView();

        ShowView(
            ControlsView,
            ControlsNavButton);
    }

    private void SettingsNavButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _runtimeOptions =
            OmsiRuntimeOptions.Load();

        LoadRuntimeOptionsIntoUi();

        ShowView(
            SettingsView,
            SettingsNavButton);
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
            MultiplayerView,
            ControlsView,
            SettingsView,
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

        if (ReferenceEquals(
                visibleView,
                SessionView))
        {
            QueueEmbeddedVehiclePreview();
        }
        else
        {
            StopEmbeddedVehiclePreview();
        }
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
            MultiplayerNavButton,
            ControlsNavButton,
            SettingsNavButton,
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
        if (_contentDiscoveryRunning)
        {
            return;
        }

        _contentDiscoveryRunning = true;
        _refreshing = true;
        PlayButton.IsEnabled = false;
        ContentDiscoveryProgress.Visibility =
            Visibility.Visible;

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
            _libraryCardsDirty = true;

            SetStatus(
                "Descobrindo mapas...");

            _maps =
                await Task.Run(
                    () =>
                        MapDiscovery.Discover(
                            contentRoot));

            MapBox.ItemsSource =
                _maps;

            MapCountText.Text =
                _maps.Count.ToString(
                    "N0");

            FooterMapCountText.Text =
                _maps.Count.ToString(
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

            // Maps are usable immediately. The heavier fleet/object scans run
            // afterwards so the map selector no longer waits for the whole
            // OMSI installation to be indexed.
            SetStatus(
                $"{_maps.Count:N0} mapa(s) encontrado(s). Carregando veículos e biblioteca...");

            var busesTask =
                Task.Run(
                    () =>
                        BusDiscovery.DiscoverPlayerSelectable(
                            contentRoot));

            var objectCountTask =
                Task.Run(
                    () =>
                        CountSceneryObjects(
                            contentRoot));

            // Programmatic map selection is complete. From here on the user
            // can switch maps immediately while the heavier fleet/object
            // discovery continues in parallel.
            _refreshing = false;

            if (map is not null)
            {
                await LoadEntryPointsAsync(
                    map);
            }

            _buses =
                await busesTask;

            _sceneryObjectCount =
                await objectCountTask;

            // Fleet/object discovery may finish after the user has already
            // opened the map library. Force a lazy card rebuild with the full
            // data set on the next library access.
            _libraryCardsDirty = true;

            _refreshing = true;

            BusCountText.Text =
                _buses.Count.ToString(
                    "N0");

            ObjectCountText.Text =
                _sceneryObjectCount.ToString(
                    "N0");

            FooterBusCountText.Text =
                _buses.Count.ToString(
                    "N0");

            FooterObjectCountText.Text =
                _sceneryObjectCount.ToString(
                    "N0");

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

            _refreshing = false;

            UpdatePlayAvailability();
            UpdateHomeSummary();
            UpdateControlsView();
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
            ContentDiscoveryProgress.Visibility =
                Visibility.Collapsed;
            _contentDiscoveryRunning = false;
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

        PopulateHofs();
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
        PopulateHofs();

        UpdateBusPreview();

        if (_refreshing)
        {
            return;
        }

        UpdatePlayAvailability();
        UpdateHomeSummary();
        SaveSettings();
    }

    private void HofBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_refreshing)
        {
            return;
        }

        UpdateBusPreview();
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

        PopulateHofs();
        UpdateBusPreview();
    }

    private void PopulateHofs()
    {
        var bus =
            SelectedBus();

        if (bus is null)
        {
            HofBox.ItemsSource = null;
            HofBox.SelectedItem = null;
            return;
        }

        HofSelection[] hofFiles;

        try
        {
            hofFiles =
                Directory.Exists(
                        bus.DirectoryPath)
                    ? Directory
                        .EnumerateFiles(
                            bus.DirectoryPath,
                            "*.hof",
                            SearchOption.TopDirectoryOnly)
                        .Select(
                            path =>
                                new HofSelection(
                                    Path.GetFileNameWithoutExtension(
                                        path),
                                    path))
                        .OrderBy(
                            static item =>
                                item.Name,
                            StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                    : Array.Empty<HofSelection>();
        }
        catch
        {
            hofFiles =
                Array.Empty<HofSelection>();
        }

        HofBox.ItemsSource =
            hofFiles;

        var savedName =
            _settings.HofName;

        var mapName =
            SelectedMap()?.FolderName;

        var target =
            hofFiles.FirstOrDefault(
                item =>
                    !string.IsNullOrWhiteSpace(
                        savedName) &&
                    string.Equals(
                        item.Name,
                        savedName,
                        StringComparison.OrdinalIgnoreCase))
            ?? hofFiles.FirstOrDefault(
                item =>
                    !string.IsNullOrWhiteSpace(
                        mapName) &&
                    item.Name.Contains(
                        mapName,
                        StringComparison.OrdinalIgnoreCase))
            ?? hofFiles.FirstOrDefault();

        HofBox.SelectedItem =
            target;
    }

    private HofSelection? SelectedHof() =>
        HofBox.SelectedItem
            as HofSelection;

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

        HofBox.IsEnabled =
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
            BusPreviewEmptyPanel.Visibility =
                Visibility.Visible;

            StopEmbeddedVehiclePreview();
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

        var hof =
            SelectedHof();

        BusPreviewSkin.Text =
            selection is null
                ? string.Empty
                : (selection.Repaint is null
                    ? $"Skin base: {bus!.Skin}"
                    : $"Repaint CTI: {selection.Repaint.Name}") +
                  (hof is null
                      ? " · HOF: não selecionado"
                      : $" · HOF: {hof.Name}");

        ApplyImage(
            BusPreviewImage,
            bus?.PreviewImagePath);

        BusPreviewEmptyText.Text =
            "Sem imagem de prévia";

        BusPreviewEmptyPanel.Visibility =
            string.IsNullOrWhiteSpace(
                bus?.PreviewImagePath)
                ? Visibility.Visible
                : Visibility.Collapsed;

        QueueEmbeddedVehiclePreview();
        UpdateHomeSummary();
    }

    private void QueueEmbeddedVehiclePreview(
        bool force = false)
    {
        _embeddedPreviewCancellation?.Cancel();

        var bus =
            SelectedBus();

        var repaint =
            SelectedRepaint();

        var contentPath =
            ContentPathBox.Text?.Trim();

        if (SessionView.Visibility !=
                Visibility.Visible ||
            NoBusCheckBox.IsChecked ==
                true ||
            bus is null ||
            string.IsNullOrWhiteSpace(
                contentPath) ||
            !_runtimeOptions.ShowVehiclePreview)
        {
            return;
        }

        var key =
            string.Join(
                "|",
                contentPath,
                bus.RelativePath,
                repaint?.Name ??
                    string.Empty,
                repaint?.RelativeCtiPath ??
                    string.Empty);

        if (!force &&
            string.Equals(
                key,
                _embeddedPreviewKey,
                StringComparison.OrdinalIgnoreCase) &&
            _embeddedPreviewWindow !=
                nint.Zero)
        {
            LayoutEmbeddedPreview();
            return;
        }

        var cancellation =
            new CancellationTokenSource();

        _embeddedPreviewCancellation =
            cancellation;

        _ =
            RefreshEmbeddedVehiclePreviewAsync(
                key,
                contentPath,
                bus,
                repaint,
                cancellation.Token);
    }

    private async Task RefreshEmbeddedVehiclePreviewAsync(
        string key,
        string contentPath,
        OmsiBusInfo bus,
        OmsiVehicleRepaint? repaint,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                250,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            _runtime.StopVehiclePreview();
            _embeddedPreviewWindow =
                nint.Zero;
            _embeddedPreviewKey =
                null;

            BusPreviewImage.Visibility =
                Visibility.Visible;

            if (!_runtime.StartVehiclePreview(
                    contentPath,
                    bus.RelativePath,
                    repaint?.Name,
                    repaint?.RelativeCtiPath))
            {
                return;
            }

            for (var attempt = 0;
                 attempt <
                     200;
                 attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_runtime.TryGetPreviewWindowHandle(
                        out var previewWindow) &&
                    previewWindow !=
                        nint.Zero)
                {
                    AttachEmbeddedPreview(
                        previewWindow,
                        key);

                    SetStatus(
                        $"Prévia 3D embutida: {bus.SelectionLabel}");
                    return;
                }

                await Task.Delay(
                    50,
                    cancellationToken);
            }

            SetStatus(
                $"A prévia 3D de {bus.SelectionLabel} iniciou, mas a janela D3D11 não ficou disponível para embutir.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus(
                $"Falha na prévia 3D embutida: {ex.Message}");
        }
    }

    private void AttachEmbeddedPreview(
        nint previewWindow,
        string key)
    {
        var parentWindow =
            WindowNative.GetWindowHandle(
                this);

        if (parentWindow ==
                nint.Zero ||
            previewWindow ==
                nint.Zero)
        {
            return;
        }

        var style =
            GetWindowLongPtr(
                previewWindow,
                GwlStyle);

        style &=
            ~(WsCaption |
              WsThickFrame |
              WsPopup);

        style |=
            WsChild;

        SetWindowLongPtr(
            previewWindow,
            GwlStyle,
            style);

        SetParent(
            previewWindow,
            parentWindow);

        _embeddedPreviewWindow =
            previewWindow;
        _embeddedPreviewKey =
            key;

        BusPreviewImage.Visibility =
            Visibility.Collapsed;
        BusPreviewEmptyPanel.Visibility =
            Visibility.Collapsed;

        LayoutEmbeddedPreview();
    }

    private void EmbeddedPreviewHost_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        LayoutEmbeddedPreview();
    }

    private void LayoutEmbeddedPreview()
    {
        if (_embeddedPreviewWindow ==
                nint.Zero ||
            SessionView.Visibility !=
                Visibility.Visible ||
            EmbeddedPreviewHost.ActualWidth <=
                1.0 ||
            EmbeddedPreviewHost.ActualHeight <=
                1.0)
        {
            if (_embeddedPreviewWindow !=
                nint.Zero)
            {
                ShowWindow(
                    _embeddedPreviewWindow,
                    SwHide);
            }

            return;
        }

        try
        {
            var origin =
                EmbeddedPreviewHost
                    .TransformToVisual(
                        null)
                    .TransformPoint(
                        new Windows.Foundation.Point(
                            0,
                            0));

            var scale =
                EmbeddedPreviewHost.XamlRoot
                    ?.RasterizationScale ??
                1.0;

            var x =
                (int)Math.Round(
                    origin.X *
                    scale);
            var y =
                (int)Math.Round(
                    origin.Y *
                    scale);
            var width =
                Math.Max(
                    1,
                    (int)Math.Round(
                        EmbeddedPreviewHost.ActualWidth *
                        scale));
            var height =
                Math.Max(
                    1,
                    (int)Math.Round(
                        EmbeddedPreviewHost.ActualHeight *
                        scale));

            SetWindowPos(
                _embeddedPreviewWindow,
                nint.Zero,
                x,
                y,
                width,
                height,
                SwpNoActivate |
                SwpFrameChanged |
                SwpShowWindow);

            ShowWindow(
                _embeddedPreviewWindow,
                SwShowNa);
        }
        catch
        {
            ShowWindow(
                _embeddedPreviewWindow,
                SwHide);
        }
    }

    private void StopEmbeddedVehiclePreview()
    {
        _embeddedPreviewCancellation?.Cancel();
        _embeddedPreviewCancellation?.Dispose();
        _embeddedPreviewCancellation =
            null;

        if (_embeddedPreviewWindow !=
            nint.Zero)
        {
            ShowWindow(
                _embeddedPreviewWindow,
                SwHide);
        }

        _embeddedPreviewWindow =
            nint.Zero;
        _embeddedPreviewKey =
            null;

        _runtime.StopVehiclePreview();

        BusPreviewImage.Visibility =
            Visibility.Visible;
    }

    private async Task LoadEntryPointsAsync(
        OmsiMapInfo map)
    {
        var loadVersion =
            ++_entryPointLoadVersion;

        SpawnBox.ItemsSource = null;
        _entryPoints =
            Array.Empty<OmsiMapEntryPointGroup>();

        SetStatus(
            $"Lendo pontos iniciais de {map.FolderName}...");

        var discovered =
            await Task.Run(
                () =>
                    MapEntryPointDiscovery.Discover(
                        map));

        if (loadVersion !=
                _entryPointLoadVersion ||
            !string.Equals(
                SelectedMap()?.FolderName,
                map.FolderName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _entryPoints =
            discovered;

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

        UpdatePlayAvailability();
        UpdateHomeSummary();
    }

    private void Preview3DButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var bus =
            SelectedBus();

        if (bus is null)
        {
            SetStatus(
                "Selecione um ônibus para a prévia 3D.");
            return;
        }

        QueueEmbeddedVehiclePreview(
            force:
                true);
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
            StopEmbeddedVehiclePreview();
            ApplyMultiplayerOptionsFromUi(
                showStatus:
                    false);
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
                    repaint?.RelativeCtiPath,
                    SelectedHof()?.FullPath))
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
                if (line.StartsWith(
                        "[runtime-select-bus]",
                        StringComparison.Ordinal))
                {
                    NoBusCheckBox.IsChecked =
                        false;

                    var parts =
                        line.Split(
                            '|',
                            3,
                            StringSplitOptions.None);

                    if (parts.Length >= 2 &&
                        !string.IsNullOrWhiteSpace(
                            parts[1]))
                    {
                        var requestedBus =
                            _buses.FirstOrDefault(
                                bus =>
                                    string.Equals(
                                        bus.RelativePath,
                                        parts[1],
                                        StringComparison.OrdinalIgnoreCase));

                        if (requestedBus is not null)
                        {
                            SelectBus(
                                requestedBus);

                            if (parts.Length >= 3 &&
                                !string.IsNullOrWhiteSpace(
                                    parts[2]))
                            {
                                var requestedHof =
                                    (HofBox.ItemsSource
                                     as IEnumerable<HofSelection>)
                                        ?.FirstOrDefault(
                                            hof =>
                                                string.Equals(
                                                    hof.Name,
                                                    parts[2],
                                                    StringComparison.OrdinalIgnoreCase));

                                if (requestedHof is not null)
                                {
                                    HofBox.SelectedItem =
                                        requestedHof;
                                }
                            }

                            SaveSettings();

                            _restartRuntimeAfterInGameBusSelection =
                                true;

                            SetStatus(
                                $"Ônibus selecionado no jogo: {requestedBus.SelectionLabel}. Aplicando seleção...");
                            return;
                        }
                    }

                    Activate();

                    CarroceriaBox.Focus(
                        FocusState.Programmatic);

                    SetStatus(
                        "Selecione o próximo ônibus e pressione JOGAR.");
                    return;
                }

                if (line.StartsWith(
                        "[multiplayer-status]|",
                        StringComparison.Ordinal))
                {
                    var fields =
                        line.Split(
                            '|',
                            7,
                            StringSplitOptions.None);

                    if (fields.Length >=
                        6)
                    {
                        var role =
                            fields[1];
                        var connected =
                            fields[2] ==
                            "1";
                        var peers =
                            fields[3];
                        var session =
                            fields[4];
                        var port =
                            fields[5];
                        var reason =
                            fields.Length >
                                6
                                ? fields[6]
                                : string.Empty;

                        MultiplayerStatusText.Text =
                            $"{role} · {(connected ? "conectado" : "conectando")} · {peers} peer(s)";

                        MultiplayerLiveText.Text =
                            $"Sessão {session} · UDP {port}" +
                            (string.IsNullOrWhiteSpace(
                                reason)
                                ? string.Empty
                                : $" · {reason}");
                    }

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
                var restartAfterBusSelection =
                    _restartRuntimeAfterInGameBusSelection &&
                    exitCode ==
                        0;

                _restartRuntimeAfterInGameBusSelection =
                    false;

                HideLoading();

                UpdatePlayAvailability();
                UpdateRuntimeStatusCards();

                if (restartAfterBusSelection)
                {
                    SetStatus(
                        "Aplicando ônibus e HOF selecionados no jogo...");

                    PlayButton_Click(
                        this,
                        new RoutedEventArgs());

                    return;
                }

                SetStatus(
                    exitCode == 0
                        ? "Runtime encerrado."
                        : $"Runtime encerrado com erro {exitCode}.");
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

        Preview3DButton.IsEnabled =
            !noBus &&
            SelectedBus() is not null;

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

        _entryPointLoadVersion++;

        _sceneryObjectCount = 0;
        _libraryCardsDirty = false;

        _repaintCache.Clear();

        MapBox.ItemsSource = null;
        CarroceriaBox.ItemsSource = null;
        ModeloBox.ItemsSource = null;
        SkinBox.ItemsSource = null;
        HofBox.ItemsSource = null;
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
        UpdateControlsView();
        UpdateCompatibilityAndDiagnostics();
    }

    private void EnsureLibraryCards()
    {
        if (!_libraryCardsDirty)
        {
            return;
        }

        RebuildLibraryCards();
        _libraryCardsDirty = false;
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
        EnsureLibraryCards();
        UpdateLibraryViews();
    }

    private void VehiclesSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        EnsureLibraryCards();
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

    private void RefreshControlsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _runtimeOptions =
            OmsiRuntimeOptions.Load();

        UpdateControlsView();

        SetStatus(
            "Controles recarregados dos arquivos OMSI.");
    }

    private void UpdateControlsView()
    {
        var contentRoot =
            ContentPathBox.Text
                ?.Trim();

        var keyboardPath =
            string.IsNullOrWhiteSpace(
                contentRoot)
                ? string.Empty
                : Path.Combine(
                    contentRoot,
                    "Inputs",
                    "keyboard.cfg");

        var controllerPath =
            string.IsNullOrWhiteSpace(
                contentRoot)
                ? string.Empty
                : Path.Combine(
                    contentRoot,
                    "Inputs",
                    "gamectrler.cfg");

        ControlsKeyboardPathText.Text =
            File.Exists(
                keyboardPath)
                ? keyboardPath
                : $"Não encontrado: {keyboardPath}";

        ControlsControllerPathText.Text =
            File.Exists(
                controllerPath)
                ? controllerPath
                : $"Não encontrado: {controllerPath}";

        var keyboardText =
            TryReadText(
                keyboardPath);

        var keyTable =
            ReadOmsiKeyTable(
                contentRoot,
                _runtimeOptions.Language,
                out var keyTablePath);

        var rows =
            ParseKeyboardDisplayRows(
                keyboardText,
                keyTable);

        ControlsKeyboardList.ItemsSource =
            rows;

        ControlsKeyboardCountText.Text =
            rows.Count.ToString(
                "N0");

        ControlsKeyTableCountText.Text =
            keyTable.Count.ToString(
                "N0");

        ControlsKeyTableFileText.Text =
            string.IsNullOrWhiteSpace(
                keyTablePath)
                ? "nenhum .kyb encontrado"
                : Path.GetFileName(
                    keyTablePath);

        var controllerText =
            TryReadText(
                controllerPath);

        var controllerCount =
            CountSectionMarkers(
                controllerText,
                "[ctrl]");

        ControlsControllerCountText.Text =
            controllerCount.ToString(
                "N0");

        ControlsGameControllerStateText.Text =
            _runtimeOptions.GameControllerEnabled
                ? "Habilitado"
                : "Desabilitado";
    }

    private static string TryReadText(
        string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(
                       path) &&
                   File.Exists(
                       path)
                ? File.ReadAllText(
                    path)
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IReadOnlyDictionary<int, string>
        ReadOmsiKeyTable(
            string? contentRoot,
            string? preferredLanguage,
            out string? selectedPath)
    {
        selectedPath =
            null;

        if (string.IsNullOrWhiteSpace(
                contentRoot))
        {
            return new Dictionary<int, string>();
        }

        var inputs =
            Path.Combine(
                contentRoot,
                "Inputs");

        if (!Directory.Exists(
                inputs))
        {
            return new Dictionary<int, string>();
        }

        var candidates =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                preferredLanguage))
        {
            candidates.Add(
                Path.Combine(
                    inputs,
                    preferredLanguage +
                    ".kyb"));
        }

        candidates.Add(
            Path.Combine(
                inputs,
                "PTB.kyb"));

        candidates.Add(
            Path.Combine(
                inputs,
                "ENG.kyb"));

        candidates.Add(
            Path.Combine(
                inputs,
                "DEU.kyb"));

        selectedPath =
            candidates.FirstOrDefault(
                File.Exists);

        try
        {
            selectedPath ??=
                Directory
                    .EnumerateFiles(
                        inputs,
                        "*.kyb",
                        SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
        }
        catch
        {
        }

        if (selectedPath is null)
        {
            return new Dictionary<int, string>();
        }

        var result =
            new Dictionary<int, string>();

        try
        {
            foreach (var raw in
                     File.ReadLines(
                         selectedPath))
            {
                var line =
                    raw.Trim();

                if (line.Length == 0 ||
                    line.StartsWith(
                        '#') ||
                    line.StartsWith(
                        "//",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var split =
                    line.Split(
                        [' ', '\t'],
                        2,
                        StringSplitOptions.RemoveEmptyEntries);

                if (split.Length < 2 ||
                    !int.TryParse(
                        split[0],
                        out var index))
                {
                    continue;
                }

                result.TryAdd(
                    index,
                    split[1].Trim());
            }
        }
        catch
        {
        }

        return result;
    }

    private static IReadOnlyList<KeyboardDisplayRow>
        ParseKeyboardDisplayRows(
            string text,
            IReadOnlyDictionary<int, string> keys)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return Array.Empty<KeyboardDisplayRow>();
        }

        var lines =
            text
                .Replace(
                    "\r\n",
                    "\n")
                .Replace(
                    '\r',
                    '\n')
                .Split(
                    '\n');

        var result =
            new List<KeyboardDisplayRow>();

        for (var index = 0;
             index < lines.Length;
             index++)
        {
            if (!string.Equals(
                    lines[index].Trim(),
                    "[entry]",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values =
                new List<string>(
                    3);

            for (var cursor = index + 1;
                 cursor < lines.Length &&
                 values.Count < 3;
                 cursor++)
            {
                var value =
                    lines[cursor]
                        .Trim();

                if (value.Length == 0 ||
                    value.StartsWith(
                        '#') ||
                    value.StartsWith(
                        "//",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (value.StartsWith(
                        '[') &&
                    value.EndsWith(
                        ']'))
                {
                    break;
                }

                values.Add(
                    value);
            }

            if (values.Count < 3 ||
                !int.TryParse(
                    values[1],
                    out var keyIndex) ||
                !int.TryParse(
                    values[2],
                    out var flags))
            {
                continue;
            }

            var keyName =
                keys.TryGetValue(
                    keyIndex,
                    out var known)
                    ? known
                    : $"OMSI #{keyIndex}";

            var flagNames =
                new List<string>();

            if ((flags & 1) != 0)
            {
                flagNames.Add(
                    "Contínuo");
            }

            if ((flags & 2) != 0)
            {
                flagNames.Add(
                    "Shift");
            }

            if ((flags & 4) != 0)
            {
                flagNames.Add(
                    "Ctrl");
            }

            result.Add(
                new KeyboardDisplayRow(
                    values[0],
                    keyName,
                    flagNames.Count == 0
                        ? "—"
                        : string.Join(
                            " · ",
                            flagNames)));
        }

        return result;
    }

    private static int CountSectionMarkers(
        string text,
        string marker)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return 0;
        }

        return text
            .Replace(
                "\r\n",
                "\n")
            .Replace(
                '\r',
                '\n')
            .Split(
                '\n')
            .Count(
                line =>
                    string.Equals(
                        line.Trim(),
                        marker,
                        StringComparison.OrdinalIgnoreCase));
    }

    private void LoadMultiplayerOptionsIntoUi()
    {
        var mode =
            (_runtimeOptions.MultiplayerMode ??
             "off")
                .Trim()
                .ToLowerInvariant();

        MultiplayerHostRadio.IsChecked =
            mode == "host";
        MultiplayerJoinRadio.IsChecked =
            mode == "join";
        MultiplayerOffRadio.IsChecked =
            mode is not ("host" or "join");

        MultiplayerPlayerNameBox.Text =
            string.IsNullOrWhiteSpace(
                _runtimeOptions.MultiplayerPlayerName)
                ? "Driver"
                : _runtimeOptions.MultiplayerPlayerName;

        MultiplayerTargetBox.Text =
            _runtimeOptions.MultiplayerTarget ??
            string.Empty;

        MultiplayerPortBox.Text =
            Math.Clamp(
                    _runtimeOptions.MultiplayerPort,
                    1,
                    ushort.MaxValue)
                .ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

        UpdateMultiplayerStatus();
    }

    private void ApplyMultiplayerOptionsFromUi(
        bool showStatus)
    {
        var mode =
            MultiplayerHostRadio.IsChecked ==
                true
                ? "host"
                : MultiplayerJoinRadio.IsChecked ==
                    true
                    ? "join"
                    : "off";

        var name =
            MultiplayerPlayerNameBox.Text
                ?.Trim();

        if (string.IsNullOrWhiteSpace(
                name))
        {
            name =
                "Driver";
        }

        if (name.Length >
            32)
        {
            name =
                name[..32];
        }

        var port =
            int.TryParse(
                MultiplayerPortBox.Text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedPort) &&
            parsedPort is >= 1 and <=
                ushort.MaxValue
                ? parsedPort
                : 27015;

        _runtimeOptions.MultiplayerMode =
            mode;
        _runtimeOptions.MultiplayerPlayerName =
            name;
        _runtimeOptions.MultiplayerTarget =
            MultiplayerTargetBox.Text
                ?.Trim() ??
            string.Empty;
        _runtimeOptions.MultiplayerPort =
            port;

        MultiplayerPortBox.Text =
            port.ToString(
                System.Globalization.CultureInfo.InvariantCulture);

        _runtimeOptions.Save();
        UpdateMultiplayerStatus();

        if (showStatus)
        {
            SetStatus(
                mode switch
                {
                    "host" =>
                        $"Multiplayer: hospedagem UDP preparada na porta {port}.",
                    "join" =>
                        string.IsNullOrWhiteSpace(
                            _runtimeOptions.MultiplayerTarget)
                            ? "Multiplayer: informe o host para entrar."
                            : $"Multiplayer: entrada preparada para {_runtimeOptions.MultiplayerTarget}.",
                    _ =>
                        "Multiplayer desativado."
                });
        }
    }

    private void UpdateMultiplayerStatus()
    {
        if (MultiplayerStatusText is null)
        {
            return;
        }

        MultiplayerStatusText.Text =
            (_runtimeOptions.MultiplayerMode ??
             "off")
                .Trim()
                .ToLowerInvariant()
            switch
            {
                "host" =>
                    $"Hospedar · UDP {_runtimeOptions.MultiplayerPort}",
                "join" =>
                    string.IsNullOrWhiteSpace(
                        _runtimeOptions.MultiplayerTarget)
                        ? "Entrar · host não informado"
                        : $"Entrar · {_runtimeOptions.MultiplayerTarget}",
                _ =>
                    "Desativado"
            };
    }

    private void LoadRuntimeOptionsIntoUi()
    {
        var culture =
            System.Globalization.CultureInfo.InvariantCulture;

        SettingsLanguageBox.Text =
            _runtimeOptions.Language;
        SettingsTicketSalesModeBox.Text =
            _runtimeOptions.TicketSalesMode.ToString(culture);
        SettingsInternetRadioBox.Text =
            _runtimeOptions.InternetRadioUrl;
        SettingsTypewriterFontBox.Text =
            _runtimeOptions.TypewriterFont;
        SettingsAutoSaveCheck.IsChecked =
            _runtimeOptions.AutoSave;
        SettingsCurrentTimeCheck.IsChecked =
            _runtimeOptions.UseCurrentTime;
        SettingsCurrentDateCheck.IsChecked =
            _runtimeOptions.UseCurrentDate;
        SettingsCurrentYearCheck.IsChecked =
            _runtimeOptions.UseCurrentYear;
        SettingsShowVehiclePreviewCheck.IsChecked =
            _runtimeOptions.ShowVehiclePreview;
        SettingsShowErrorMessagesCheck.IsChecked =
            _runtimeOptions.ShowErrorMessages;
        SettingsScheduleAnalysisCheck.IsChecked =
            _runtimeOptions.ScheduleAnalysisPopup;
        SettingsSeeOwnDriverCheck.IsChecked =
            _runtimeOptions.SeeOwnDriver;
        SettingsShowTicketPassengerInfoCheck.IsChecked =
            _runtimeOptions.ShowTicketPassengerInfo;
        SettingsWearLifespanBox.Text =
            _runtimeOptions.WearLifespan.ToString(culture);

        SettingsTargetFpsBox.Text =
            _runtimeOptions.TargetFps.ToString(culture);
        SettingsVsyncCheck.IsChecked =
            _runtimeOptions.RuntimeVSync;
        SettingsBorderlessCheck.IsChecked =
            _runtimeOptions.RuntimeBorderlessFullscreen;
        SettingsHardwareGpuCheck.IsChecked =
            _runtimeOptions.RuntimePreferHardwareGpu;
        SettingsMsaaSamplesBox.Text =
            _runtimeOptions.RuntimeMsaaSamples.ToString(culture);
        SettingsSharpenStrengthBox.Text =
            _runtimeOptions.RuntimeSharpenStrength.ToString("0.00", culture);
        SettingsAnisotropicBox.Text =
            _runtimeOptions.AnisotropicFiltering.ToString(culture);
        SettingsTextureFilterModeBox.Text =
            _runtimeOptions.TextureFilterMode.ToString(culture);
        SettingsObjectDistanceBox.Text =
            _runtimeOptions.MaximumObjectVisibilityMeters.ToString(culture);
        SettingsMinimumObjectSizeBox.Text =
            _runtimeOptions.MinimumObjectSize.ToString(culture);
        SettingsNeighborTilesBox.Text =
            _runtimeOptions.NeighborTiles.ToString(culture);
        SettingsStreamingRadiusBox.Text =
            _runtimeOptions.RuntimeStreamingRadius.ToString(culture);
        SettingsShadowsCheck.IsChecked =
            _runtimeOptions.Shadows;
        SettingsStencilEffectsCheck.IsChecked =
            _runtimeOptions.StencilBufferEffects;
        SettingsSunGlowCheck.IsChecked =
            _runtimeOptions.SunGlow;
        SettingsRainReflectionsCheck.IsChecked =
            _runtimeOptions.RainReflections;
        SettingsHumansRainReflectionsCheck.IsChecked =
            _runtimeOptions.HumansInRainReflections;
        SettingsMaximumObjectComplexityBox.Text =
            _runtimeOptions.MaximumObjectComplexity.ToString(culture);
        SettingsMaximumMapComplexityBox.Text =
            _runtimeOptions.MaximumMapComplexity.ToString(culture);

        SettingsTextureMemoryBox.Text =
            _runtimeOptions.HighResolutionTextureMemoryMb.ToString(culture);
        SettingsOnlyLowTexturesCheck.IsChecked =
            _runtimeOptions.OnlyLowResolutionTextures;
        SettingsLimitTextures256Check.IsChecked =
            _runtimeOptions.LimitTexturesTo256;
        SettingsLowTexturesAtDistanceCheck.IsChecked =
            _runtimeOptions.LowResolutionTexturesAtDistance;
        SettingsNightMapCheck.IsChecked =
            _runtimeOptions.MaterialNightMap;
        SettingsLightMapCheck.IsChecked =
            _runtimeOptions.MaterialLightMap;
        SettingsTerrainLightMapCheck.IsChecked =
            _runtimeOptions.MaterialTerrainLightMap;
        SettingsReflectionMapCheck.IsChecked =
            _runtimeOptions.MaterialReflectionMap;
        SettingsBumpMapCheck.IsChecked =
            _runtimeOptions.MaterialBumpMap;
        SettingsReflectionModeBox.Text =
            _runtimeOptions.RealTimeReflections;
        SettingsReflectionSizeBox.Text =
            _runtimeOptions.RealTimeReflectionTextureSize.ToString(culture);
        SettingsMinimumObjectSizeReflectionBox.Text =
            _runtimeOptions.MinimumObjectSizeReflections.ToString(culture);
        SettingsReflectionEconomyFpsBox.Text =
            _runtimeOptions.ReflectionEconomyBelowFps.ToString(culture);
        SettingsReflectionFullFpsBox.Text =
            _runtimeOptions.ReflectionFullAboveFps.ToString(culture);

        SettingsMasterVolumeBox.Text =
            _runtimeOptions.MasterVolumePercent.ToString(culture);
        SettingsMaximumSoundCountBox.Text =
            _runtimeOptions.MaximumSoundCount.ToString(culture);
        SettingsStereoEffectBox.Text =
            _runtimeOptions.StereoEffect.ToString(culture);
        SettingsDopplerCheck.IsChecked =
            _runtimeOptions.DopplerEffect;
        SettingsReverbCheck.IsChecked =
            _runtimeOptions.ReverbEffects;
        SettingsAiSoundsCheck.IsChecked =
            _runtimeOptions.AiVehicleSounds;
        SettingsScenerySoundsCheck.IsChecked =
            _runtimeOptions.ScenerySounds;

        SettingsMaximumUnscheduledTrafficBox.Text =
            _runtimeOptions.MaximumUnscheduledTraffic.ToString(culture);
        SettingsRoadTrafficBox.Text =
            _runtimeOptions.RoadTrafficFactorPercent.ToString(culture);
        SettingsParkedCarsBox.Text =
            _runtimeOptions.ParkedCarsPercent.ToString(culture);
        SettingsMaximumPeopleBox.Text =
            _runtimeOptions.MaximumPeople.ToString(culture);
        SettingsPassengerFactorBox.Text =
            _runtimeOptions.PassengerFactorPercent.ToString(culture);
        SettingsMaximumScheduledTrafficBox.Text =
            _runtimeOptions.MaximumScheduledTraffic.ToString(culture);
        SettingsScheduledTrafficPriorityBox.Text =
            _runtimeOptions.ScheduledTrafficPriority.ToString(culture);
        SettingsReducedAiListCheck.IsChecked =
            _runtimeOptions.UseReducedAiList;

        SettingsGameControllerCheck.IsChecked =
            _runtimeOptions.GameControllerEnabled;
        SettingsControllerDeadZoneBox.Text =
            _runtimeOptions.ControllerDeadZone.ToString("0.00", culture);
        SettingsMouseSteeringSensitivityBox.Text =
            _runtimeOptions.MouseSteeringSensitivity.ToString("0.00", culture);
        SettingsThrottlePedalResponseBox.Text =
            _runtimeOptions.ThrottlePedalResponse.ToString("0.00", culture);
        SettingsBrakePedalResponseBox.Text =
            _runtimeOptions.BrakePedalResponse.ToString("0.00", culture);
        SettingsWheelRangeBox.Text =
            _runtimeOptions.WheelRangeDegrees.ToString("0", culture);
        SettingsWheelLockBox.Text =
            _runtimeOptions.WheelLockDegrees.ToString("0", culture);
        SettingsFieldOfViewBox.Text =
            _runtimeOptions.FieldOfViewDegrees.ToString("0", culture);
        SettingsAutomaticSteeringCenterCheck.IsChecked =
            _runtimeOptions.AutomaticSteeringCenter;
        SettingsAutomaticClutchCheck.IsChecked =
            _runtimeOptions.AutomaticClutch;
        SettingsReducedSteeringSpeedCheck.IsChecked =
            _runtimeOptions.ReducedSteeringSpeed;
        SettingsSmoothDriverViewCheck.IsChecked =
            _runtimeOptions.SmoothDriverViewTransitions;
        SettingsDriverHeadMovementCheck.IsChecked =
            _runtimeOptions.DriverHeadMovement;
        SettingsAlternativeViewCheck.IsChecked =
            _runtimeOptions.AlternativeView;
        SettingsLandscapeCollisionsCheck.IsChecked =
            _runtimeOptions.VehicleLandscapeCollisions;
        SettingsTerrainCollisionsCheck.IsChecked =
            _runtimeOptions.TerrainCollisions;
        SettingsVehicleCollisionsCheck.IsChecked =
            _runtimeOptions.VehicleToVehicleCollisions;
        SettingsPedestrianCollisionsCheck.IsChecked =
            _runtimeOptions.UserVehiclePedestrianCollisions;

        SettingsReducedMultithreadingCheck.IsChecked =
            _runtimeOptions.ReducedMultithreading;
        SettingsLoadWholeMapCheck.IsChecked =
            _runtimeOptions.LoadWholeMapAtStart;
        SettingsParticleSystemsCheck.IsChecked =
            _runtimeOptions.ParticleSystems;
        SettingsMaximumParticlesBox.Text =
            _runtimeOptions.MaximumParticlesPerEmitter.ToString(culture);
        SettingsParticleOwnVehicleCheck.IsChecked =
            _runtimeOptions.ParticleOnlyOwnVehicle;
        SettingsParticleReflectionsCheck.IsChecked =
            _runtimeOptions.ParticleSystemsInReflections;
        SettingsReduceTilesBelowFpsBox.Text =
            _runtimeOptions.ReduceTilesBelowFps.ToString(culture);
        SettingsIncreaseTilesAboveFpsBox.Text =
            _runtimeOptions.IncreaseTilesAboveFps.ToString(culture);
        SettingsManualAspectCheck.IsChecked =
            _runtimeOptions.ManualAspectRatioEnabled;
        SettingsManualAspectRatioBox.Text =
            _runtimeOptions.ManualAspectRatio.ToString("0.###", culture);
        SettingsShowFpsCheck.IsChecked =
            _runtimeOptions.RuntimeShowFps;
        SettingsDiagnosticsCheck.IsChecked =
            _runtimeOptions.RuntimeDiagnostics;
    }

    private void SaveRuntimeOptionsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            ApplyRuntimeOptionsFromUi();

            _runtimeOptions.Save();

            UpdateControlsView();

            SetStatus(
                $"Configurações salvas em {OmsiRuntimeOptions.SettingsPath}");
        }
        catch (Exception ex)
        {
            SetStatus(
                $"Falha ao salvar configurações: {ex.Message}");
        }
    }

    private void ImportOmsiOptionsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var contentRoot =
            ContentPathBox.Text
                ?.Trim();

        if (string.IsNullOrWhiteSpace(
                contentRoot) ||
            !Directory.Exists(
                contentRoot))
        {
            SetStatus(
                "Configure primeiro uma biblioteca OMSI válida.");
            return;
        }

        var optionsPath =
            Path.Combine(
                contentRoot,
                "options.cfg");

        if (!File.Exists(
                optionsPath))
        {
            SetStatus(
                $"options.cfg não encontrado em {contentRoot}");
            return;
        }

        _runtimeOptions =
            OmsiRuntimeOptions.ImportFromOmsi(
                contentRoot,
                _runtimeOptions);

        LoadRuntimeOptionsIntoUi();
        UpdateControlsView();

        SetStatus(
            $"options.cfg importado: {optionsPath}");
    }

    private void ApplyRuntimeOptionsFromUi()
    {
        var language =
            SettingsLanguageBox.Text?.Trim();

        if (!string.IsNullOrWhiteSpace(language))
        {
            _runtimeOptions.Language =
                language;
        }

        _runtimeOptions.TicketSalesMode =
            ToInt(ParseNumber(SettingsTicketSalesModeBox.Text), 0, 10, _runtimeOptions.TicketSalesMode);
        _runtimeOptions.InternetRadioUrl =
            SettingsInternetRadioBox.Text?.Trim() ?? string.Empty;
        _runtimeOptions.TypewriterFont =
            string.IsNullOrWhiteSpace(SettingsTypewriterFontBox.Text)
                ? _runtimeOptions.TypewriterFont
                : SettingsTypewriterFontBox.Text.Trim();
        _runtimeOptions.AutoSave =
            SettingsAutoSaveCheck.IsChecked == true;
        _runtimeOptions.UseCurrentTime =
            SettingsCurrentTimeCheck.IsChecked == true;
        _runtimeOptions.UseCurrentDate =
            SettingsCurrentDateCheck.IsChecked == true;
        _runtimeOptions.UseCurrentYear =
            SettingsCurrentYearCheck.IsChecked == true;
        _runtimeOptions.ShowVehiclePreview =
            SettingsShowVehiclePreviewCheck.IsChecked == true;
        _runtimeOptions.ShowErrorMessages =
            SettingsShowErrorMessagesCheck.IsChecked == true;
        _runtimeOptions.ScheduleAnalysisPopup =
            SettingsScheduleAnalysisCheck.IsChecked == true;
        _runtimeOptions.SeeOwnDriver =
            SettingsSeeOwnDriverCheck.IsChecked == true;
        _runtimeOptions.ShowTicketPassengerInfo =
            SettingsShowTicketPassengerInfoCheck.IsChecked == true;
        _runtimeOptions.WearLifespan =
            ToInt(ParseNumber(SettingsWearLifespanBox.Text), 0, 100, _runtimeOptions.WearLifespan);

        _runtimeOptions.TargetFps =
            ToInt(ParseNumber(SettingsTargetFpsBox.Text), 15, 240, _runtimeOptions.TargetFps);
        _runtimeOptions.RuntimeVSync =
            SettingsVsyncCheck.IsChecked == true;
        _runtimeOptions.RuntimeBorderlessFullscreen =
            SettingsBorderlessCheck.IsChecked == true;
        _runtimeOptions.RuntimePreferHardwareGpu =
            SettingsHardwareGpuCheck.IsChecked == true;

        var requestedMsaa =
            ToInt(ParseNumber(SettingsMsaaSamplesBox.Text), 0, 4, _runtimeOptions.RuntimeMsaaSamples);

        _runtimeOptions.RuntimeMsaaSamples =
            requestedMsaa >= 4
                ? 4
                : requestedMsaa >= 2
                    ? 2
                    : 0;

        _runtimeOptions.RuntimeSharpenStrength =
            ToDouble(ParseNumber(SettingsSharpenStrengthBox.Text), 0.0, 1.0, _runtimeOptions.RuntimeSharpenStrength);
        _runtimeOptions.AnisotropicFiltering =
            ToInt(ParseNumber(SettingsAnisotropicBox.Text), 1, 16, _runtimeOptions.AnisotropicFiltering);
        _runtimeOptions.TextureFilterMode =
            ToInt(ParseNumber(SettingsTextureFilterModeBox.Text), 0, 8, _runtimeOptions.TextureFilterMode);
        _runtimeOptions.MaximumObjectVisibilityMeters =
            ToDouble(ParseNumber(SettingsObjectDistanceBox.Text), 100, 10000, _runtimeOptions.MaximumObjectVisibilityMeters);
        _runtimeOptions.MinimumObjectSize =
            ToDouble(ParseNumber(SettingsMinimumObjectSizeBox.Text), 0.0001, 10.0, _runtimeOptions.MinimumObjectSize);
        _runtimeOptions.NeighborTiles =
            ToInt(ParseNumber(SettingsNeighborTilesBox.Text), 0, 8, _runtimeOptions.NeighborTiles);
        _runtimeOptions.RuntimeStreamingRadius =
            ToInt(ParseNumber(SettingsStreamingRadiusBox.Text), 1, 8, _runtimeOptions.RuntimeStreamingRadius);
        _runtimeOptions.Shadows =
            SettingsShadowsCheck.IsChecked == true;
        _runtimeOptions.StencilBufferEffects =
            SettingsStencilEffectsCheck.IsChecked == true;
        _runtimeOptions.SunGlow =
            SettingsSunGlowCheck.IsChecked == true;
        _runtimeOptions.RainReflections =
            SettingsRainReflectionsCheck.IsChecked == true;
        _runtimeOptions.HumansInRainReflections =
            SettingsHumansRainReflectionsCheck.IsChecked == true;
        _runtimeOptions.MaximumObjectComplexity =
            ToInt(ParseNumber(SettingsMaximumObjectComplexityBox.Text), 0, 10, _runtimeOptions.MaximumObjectComplexity);
        _runtimeOptions.MaximumMapComplexity =
            ToInt(ParseNumber(SettingsMaximumMapComplexityBox.Text), 0, 10, _runtimeOptions.MaximumMapComplexity);

        _runtimeOptions.HighResolutionTextureMemoryMb =
            ToDouble(ParseNumber(SettingsTextureMemoryBox.Text), 128, 32768, _runtimeOptions.HighResolutionTextureMemoryMb);
        _runtimeOptions.OnlyLowResolutionTextures =
            SettingsOnlyLowTexturesCheck.IsChecked == true;
        _runtimeOptions.LimitTexturesTo256 =
            SettingsLimitTextures256Check.IsChecked == true;
        _runtimeOptions.LowResolutionTexturesAtDistance =
            SettingsLowTexturesAtDistanceCheck.IsChecked == true;
        _runtimeOptions.MaterialNightMap =
            SettingsNightMapCheck.IsChecked == true;
        _runtimeOptions.MaterialLightMap =
            SettingsLightMapCheck.IsChecked == true;
        _runtimeOptions.MaterialTerrainLightMap =
            SettingsTerrainLightMapCheck.IsChecked == true;
        _runtimeOptions.MaterialReflectionMap =
            SettingsReflectionMapCheck.IsChecked == true;
        _runtimeOptions.MaterialBumpMap =
            SettingsBumpMapCheck.IsChecked == true;

        var reflectionMode =
            SettingsReflectionModeBox.Text?.Trim().ToLowerInvariant();

        if (reflectionMode is "economy" or "full" or "off")
        {
            _runtimeOptions.RealTimeReflections =
                reflectionMode;
        }

        _runtimeOptions.RealTimeReflectionTextureSize =
            ToInt(ParseNumber(SettingsReflectionSizeBox.Text), 64, 4096, _runtimeOptions.RealTimeReflectionTextureSize);
        _runtimeOptions.MinimumObjectSizeReflections =
            ToDouble(ParseNumber(SettingsMinimumObjectSizeReflectionBox.Text), 0.0001, 10.0, _runtimeOptions.MinimumObjectSizeReflections);
        _runtimeOptions.ReflectionEconomyBelowFps =
            ToDouble(ParseNumber(SettingsReflectionEconomyFpsBox.Text), 1, 240, _runtimeOptions.ReflectionEconomyBelowFps);
        _runtimeOptions.ReflectionFullAboveFps =
            ToDouble(ParseNumber(SettingsReflectionFullFpsBox.Text), 1, 240, _runtimeOptions.ReflectionFullAboveFps);

        _runtimeOptions.MasterVolumePercent =
            ToInt(ParseNumber(SettingsMasterVolumeBox.Text), 0, 100, _runtimeOptions.MasterVolumePercent);
        _runtimeOptions.MaximumSoundCount =
            ToInt(ParseNumber(SettingsMaximumSoundCountBox.Text), 1, 4096, _runtimeOptions.MaximumSoundCount);
        _runtimeOptions.StereoEffect =
            ToInt(ParseNumber(SettingsStereoEffectBox.Text), 0, 100, _runtimeOptions.StereoEffect);
        _runtimeOptions.DopplerEffect =
            SettingsDopplerCheck.IsChecked == true;
        _runtimeOptions.ReverbEffects =
            SettingsReverbCheck.IsChecked == true;
        _runtimeOptions.AiVehicleSounds =
            SettingsAiSoundsCheck.IsChecked == true;
        _runtimeOptions.ScenerySounds =
            SettingsScenerySoundsCheck.IsChecked == true;

        _runtimeOptions.MaximumUnscheduledTraffic =
            ToInt(ParseNumber(SettingsMaximumUnscheduledTrafficBox.Text), 0, 10000, _runtimeOptions.MaximumUnscheduledTraffic);
        _runtimeOptions.RoadTrafficFactorPercent =
            ToInt(ParseNumber(SettingsRoadTrafficBox.Text), 0, 500, _runtimeOptions.RoadTrafficFactorPercent);
        _runtimeOptions.ParkedCarsPercent =
            ToInt(ParseNumber(SettingsParkedCarsBox.Text), 0, 500, _runtimeOptions.ParkedCarsPercent);
        _runtimeOptions.MaximumPeople =
            ToInt(ParseNumber(SettingsMaximumPeopleBox.Text), 0, 10000, _runtimeOptions.MaximumPeople);
        _runtimeOptions.PassengerFactorPercent =
            ToInt(ParseNumber(SettingsPassengerFactorBox.Text), 0, 500, _runtimeOptions.PassengerFactorPercent);
        _runtimeOptions.MaximumScheduledTraffic =
            ToInt(ParseNumber(SettingsMaximumScheduledTrafficBox.Text), 0, 10000, _runtimeOptions.MaximumScheduledTraffic);
        _runtimeOptions.ScheduledTrafficPriority =
            ToInt(ParseNumber(SettingsScheduledTrafficPriorityBox.Text), 0, 10, _runtimeOptions.ScheduledTrafficPriority);
        _runtimeOptions.UseReducedAiList =
            SettingsReducedAiListCheck.IsChecked == true;

        _runtimeOptions.GameControllerEnabled =
            SettingsGameControllerCheck.IsChecked == true;
        _runtimeOptions.ControllerDeadZone =
            ToDouble(
                ParseNumber(SettingsControllerDeadZoneBox.Text),
                0.0,
                0.30,
                _runtimeOptions.ControllerDeadZone);
        _runtimeOptions.MouseSteeringSensitivity =
            ToDouble(
                ParseNumber(SettingsMouseSteeringSensitivityBox.Text),
                0.25,
                4.0,
                _runtimeOptions.MouseSteeringSensitivity);
        _runtimeOptions.ThrottlePedalResponse =
            ToDouble(
                ParseNumber(SettingsThrottlePedalResponseBox.Text),
                0.25,
                4.0,
                _runtimeOptions.ThrottlePedalResponse);
        _runtimeOptions.BrakePedalResponse =
            ToDouble(
                ParseNumber(SettingsBrakePedalResponseBox.Text),
                0.25,
                4.0,
                _runtimeOptions.BrakePedalResponse);
        _runtimeOptions.WheelRangeDegrees =
            ToDouble(
                ParseNumber(SettingsWheelRangeBox.Text),
                90.0,
                2880.0,
                _runtimeOptions.WheelRangeDegrees);

        var requestedWheelLock =
            ToDouble(
                ParseNumber(SettingsWheelLockBox.Text),
                0.0,
                2880.0,
                _runtimeOptions.WheelLockDegrees);

        _runtimeOptions.WheelLockDegrees =
            requestedWheelLock < 45.0
                ? 0.0
                : requestedWheelLock;

        var requestedFov =
            ToDouble(
                ParseNumber(SettingsFieldOfViewBox.Text),
                0.0,
                120.0,
                _runtimeOptions.FieldOfViewDegrees);

        _runtimeOptions.FieldOfViewDegrees =
            requestedFov < 20.0
                ? 0.0
                : requestedFov;

        _runtimeOptions.AutomaticSteeringCenter =
            SettingsAutomaticSteeringCenterCheck.IsChecked == true;
        _runtimeOptions.AutomaticClutch =
            SettingsAutomaticClutchCheck.IsChecked == true;
        _runtimeOptions.ReducedSteeringSpeed =
            SettingsReducedSteeringSpeedCheck.IsChecked == true;
        _runtimeOptions.SmoothDriverViewTransitions =
            SettingsSmoothDriverViewCheck.IsChecked == true;
        _runtimeOptions.DriverHeadMovement =
            SettingsDriverHeadMovementCheck.IsChecked == true;
        _runtimeOptions.AlternativeView =
            SettingsAlternativeViewCheck.IsChecked == true;
        _runtimeOptions.VehicleLandscapeCollisions =
            SettingsLandscapeCollisionsCheck.IsChecked == true;
        _runtimeOptions.TerrainCollisions =
            SettingsTerrainCollisionsCheck.IsChecked == true;
        _runtimeOptions.VehicleToVehicleCollisions =
            SettingsVehicleCollisionsCheck.IsChecked == true;
        _runtimeOptions.UserVehiclePedestrianCollisions =
            SettingsPedestrianCollisionsCheck.IsChecked == true;

        _runtimeOptions.ReducedMultithreading =
            SettingsReducedMultithreadingCheck.IsChecked == true;
        _runtimeOptions.LoadWholeMapAtStart =
            SettingsLoadWholeMapCheck.IsChecked == true;
        _runtimeOptions.ParticleSystems =
            SettingsParticleSystemsCheck.IsChecked == true;
        _runtimeOptions.MaximumParticlesPerEmitter =
            ToInt(ParseNumber(SettingsMaximumParticlesBox.Text), 0, 100000, _runtimeOptions.MaximumParticlesPerEmitter);
        _runtimeOptions.ParticleOnlyOwnVehicle =
            SettingsParticleOwnVehicleCheck.IsChecked == true;
        _runtimeOptions.ParticleSystemsInReflections =
            SettingsParticleReflectionsCheck.IsChecked == true;
        _runtimeOptions.ReduceTilesBelowFps =
            ToDouble(ParseNumber(SettingsReduceTilesBelowFpsBox.Text), 1, 240, _runtimeOptions.ReduceTilesBelowFps);
        _runtimeOptions.IncreaseTilesAboveFps =
            ToDouble(ParseNumber(SettingsIncreaseTilesAboveFpsBox.Text), 1, 240, _runtimeOptions.IncreaseTilesAboveFps);
        _runtimeOptions.ManualAspectRatioEnabled =
            SettingsManualAspectCheck.IsChecked == true;
        _runtimeOptions.ManualAspectRatio =
            ToDouble(ParseNumber(SettingsManualAspectRatioBox.Text), 0.25, 8.0, _runtimeOptions.ManualAspectRatio);
        _runtimeOptions.RuntimeShowFps =
            SettingsShowFpsCheck.IsChecked == true;
        _runtimeOptions.RuntimeDiagnostics =
            SettingsDiagnosticsCheck.IsChecked == true;
    }

    private void ResetRuntimeOptionsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _runtimeOptions =
            new OmsiRuntimeOptions();

        _runtimeOptions.Save();
        LoadRuntimeOptionsIntoUi();
        UpdateControlsView();

        SetStatus(
            "Configurações restauradas para os padrões do runtime.");
    }

    private static double ParseNumber(
        string? text)
    {
        if (string.IsNullOrWhiteSpace(
                text))
        {
            return double.NaN;
        }

        var normalized =
            text.Trim();

        if (double.TryParse(
                normalized,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var invariant))
        {
            return invariant;
        }

        if (double.TryParse(
                normalized,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture,
                out var current))
        {
            return current;
        }

        return double.NaN;
    }

    private static int ToInt(
        double value,
        int minimum,
        int maximum,
        int fallback)
    {
        if (!double.IsFinite(
                value))
        {
            return fallback;
        }

        return Math.Clamp(
            (int)Math.Round(
                value),
            minimum,
            maximum);
    }

    private static double ToDouble(
        double value,
        double minimum,
        double maximum,
        double fallback)
    {
        if (!double.IsFinite(
                value))
        {
            return fallback;
        }

        return Math.Clamp(
            value,
            minimum,
            maximum);
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
                HofName =
                    SelectedHof()?.Name,
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
