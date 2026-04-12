using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using OptiscalerClient.Models;
using OptiscalerClient.Services;

namespace OptiscalerClient.Views;

public partial class BulkInstallWindow : Window
{
    private readonly ComponentManagementService _componentService;
    private readonly GameInstallationService _installService;
    private readonly IGpuDetectionService _gpuService;
    private readonly ObservableCollection<BulkGameItem> _gameItems;
    private readonly ObservableCollection<BulkGameItem> _filteredGameItems;
    private List<BulkGameItem> _allGames = new List<BulkGameItem>();
    private bool _isInstalling = false;

    public BulkInstallWindow()
    {
        InitializeComponent();
        
        // Initialize fields to avoid nullable warnings
        _componentService = null!;
        _installService = null!;
        _gpuService = null!;
        _gameItems = new ObservableCollection<BulkGameItem>();
        _filteredGameItems = new ObservableCollection<BulkGameItem>();
    }

    public BulkInstallWindow(
        ComponentManagementService componentService,
        GameInstallationService installService,
        List<Game> games)
    {
        InitializeComponent();
        
        _componentService = componentService;
        _installService = installService;
        _gameItems = new ObservableCollection<BulkGameItem>();
        _filteredGameItems = new ObservableCollection<BulkGameItem>();

        // Initialize GPU service
        _gpuService = GpuDetectionServiceFactory.Create();

        // Populate games list
        foreach (var game in games.OrderBy(g => g.Name))
        {
            var gameItem = new BulkGameItem
            {
                Game = game,
                Name = game.Name,
                Platform = game.Platform.ToString(),
                CoverPath = game.CoverImageUrl,
                IsInstalled = game.IsOptiscalerInstalled,
                CanInstall = !game.IsOptiscalerInstalled,
                IsSelected = false, // Start with all items unchecked
                OptiscalerVersion = game.OptiscalerVersion,
                IsOptiscalerInstalled = game.IsOptiscalerInstalled
            };
            
            _gameItems.Add(gameItem);
            _allGames.Add(gameItem);
            _filteredGameItems.Add(gameItem);
        }

        var gamesList = this.FindControl<ItemsControl>("GamesList");
        if (gamesList != null)
        {
            gamesList.ItemsSource = _filteredGameItems;
        }

        // Load versions
        _ = LoadVersionsAsync();
        
        // Update selection count
        UpdateSelectionCount();

        // Subscribe to selection changes
        foreach (var item in _gameItems)
        {
            item.PropertyChanged += GameItem_PropertyChanged;
        }

        // Setup version selection handler
        var cmbOptiVersion = this.FindControl<ComboBox>("CmbOptiVersion");
        if (cmbOptiVersion != null)
        {
            cmbOptiVersion.SelectionChanged += CmbOptiVersion_SelectionChanged;
        }

        // Initialize injection method selector
        var cmbInjectionMethod = this.FindControl<ComboBox>("CmbInjectionMethod");
        if (cmbInjectionMethod != null)
        {
            cmbInjectionMethod.SelectedIndex = 0; // Default to dxgi.dll
        }

        // Populate FSR4 INT8 versions
        PopulateExtrasComboBox();

        // Fade in animation
        var rootPanel = this.FindControl<Panel>("RootPanel");
        if (rootPanel != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                rootPanel.Transitions = new Avalonia.Animation.Transitions
                {
                    new Avalonia.Animation.DoubleTransition
                    {
                        Property = Panel.OpacityProperty,
                        Duration = TimeSpan.FromMilliseconds(200)
                    }
                };
                rootPanel.Opacity = 1;
            }, DispatcherPriority.Render);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async Task LoadVersionsAsync()
    {
        // Always refresh the service state here; internal rate limiting keeps this cheap.
        await _componentService.CheckForUpdatesAsync();

        Dispatcher.UIThread.Post(() =>
        {
            var allVersions = _componentService.OptiScalerAvailableVersions;
            var betaVersions = _componentService.BetaVersions;
            var latestBeta = _componentService.LatestBetaVersion;
            var showBetaVersions = _componentService.Config.ShowBetaVersions;

            var cmbOptiVersion = this.FindControl<ComboBox>("CmbOptiVersion");
            if (cmbOptiVersion == null) return;

            cmbOptiVersion.Items.Clear();

            if (allVersions.Count == 0)
            {
                cmbOptiVersion.Items.Add("No versions available");
                cmbOptiVersion.SelectedIndex = 0;
                cmbOptiVersion.IsEnabled = false;
                return;
            }

            var stableVersions = allVersions.Where(v => !betaVersions.Contains(v)).ToList();
            var otherBetas = allVersions.Where(v => betaVersions.Contains(v) && v != latestBeta).ToList();

            int selectedIndex = 0;
            int currentIndex = 0;

            bool hasBeta = !string.IsNullOrEmpty(latestBeta);

            // Add latest beta first - NO LATEST badge for beta
            if (hasBeta && latestBeta != null)
            {
                cmbOptiVersion.Items.Add(BuildVersionItem(latestBeta, isBeta: true, isLatest: false));
                selectedIndex = 0; // Select beta by default
                currentIndex++;
            }

            // Add stable versions - first stable gets LATEST badge
            bool isLatestStableMarked = false;
            foreach (var ver in stableVersions)
            {
                bool isFirstStable = !isLatestStableMarked && !ver.Contains("nightly", StringComparison.OrdinalIgnoreCase);
                bool shouldMarkAsLatest = isFirstStable;

                if (isFirstStable)
                {
                    isLatestStableMarked = true;
                }

                if (showBetaVersions && hasBeta)
                {
                    selectedIndex = 0;
                }
                else if (isFirstStable)
                {
                    selectedIndex = currentIndex;
                }

                cmbOptiVersion.Items.Add(BuildVersionItem(ver, isBeta: false, isLatest: shouldMarkAsLatest));
                currentIndex++;
            }

            // Add other betas
            foreach (var ver in otherBetas)
            {
                cmbOptiVersion.Items.Add(BuildVersionItem(ver, isBeta: true, isLatest: false));
                currentIndex++;
            }

            cmbOptiVersion.SelectedIndex = selectedIndex;
            UpdateCheckboxStatesForVersion(cmbOptiVersion);
            PopulateExtrasComboBox();
            UpdateSelectionCount();
        });
    }

    private static VersionOption BuildVersionItem(string ver, bool isBeta, bool isLatest)
    {
        return new VersionOption
        {
            DisplayVersion = ver,
            Value = ver,
            IsBeta = isBeta,
            IsLatest = isLatest
        };
    }

    private void GameItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BulkGameItem.IsSelected))
        {
            UpdateSelectionCount();
            UpdateSelectAllCheckbox();
        }
    }

    private void UpdateSelectionCount()
    {
        var selectedCount = _gameItems.Count(g => g.IsSelected && g.CanInstall);
        var txtCount = this.FindControl<TextBlock>("TxtSelectionCount");
        var btnInstall = this.FindControl<Button>("BtnInstall");

        if (txtCount != null)
        {
            txtCount.Text = selectedCount == 1 
                ? "1 game selected" 
                : $"{selectedCount} games selected";
        }

        if (btnInstall != null)
        {
            btnInstall.Content = selectedCount == 0
                ? "Install Selected"
                : selectedCount == 1
                    ? "Install 1 game"
                    : $"Install {selectedCount} games";
            btnInstall.IsEnabled = selectedCount > 0 && !_isInstalling && HasSelectedVersion();
        }
    }

    private bool HasSelectedVersion()
    {
        var cmbOptiVersion = this.FindControl<ComboBox>("CmbOptiVersion");
        return cmbOptiVersion?.SelectedItem is VersionOption selectedItem &&
               !string.IsNullOrWhiteSpace(selectedItem.Value);
    }

    private void UpdateSelectAllCheckbox()
    {
        var chkSelectAll = this.FindControl<CheckBox>("ChkSelectAll");
        if (chkSelectAll == null) return;

        var selectableGames = _gameItems.Where(g => g.CanInstall).ToList();
        if (selectableGames.Count == 0)
        {
            chkSelectAll.IsChecked = false;
            return;
        }

        var selectedCount = selectableGames.Count(g => g.IsSelected);
        
        if (selectedCount == 0)
            chkSelectAll.IsChecked = false;
        else if (selectedCount == selectableGames.Count)
            chkSelectAll.IsChecked = true;
        else
            chkSelectAll.IsChecked = null; // Indeterminate state
    }

    private void ChkSelectAll_Click(object? sender, RoutedEventArgs e)
    {
        var chkSelectAll = sender as CheckBox;
        if (chkSelectAll == null) return;

        bool shouldSelect = chkSelectAll.IsChecked == true;

        foreach (var item in _gameItems.Where(g => g.CanInstall))
        {
            item.IsSelected = shouldSelect;
        }
    }

    private async void BtnInstall_Click(object? sender, RoutedEventArgs e)
    {
        var selectedGames = _gameItems.Where(g => g.IsSelected && g.CanInstall).ToList();
        if (selectedGames.Count == 0) return;

        var cmbOptiVersion = this.FindControl<ComboBox>("CmbOptiVersion");
        var cmbInjectionMethod = this.FindControl<ComboBox>("CmbInjectionMethod");
        var cmbExtrasVersion = this.FindControl<ComboBox>("CmbExtrasVersion");
        var chkFakenvapi = this.FindControl<CheckBox>("ChkFakenvapi");
        var chkNukemFG = this.FindControl<CheckBox>("ChkNukemFG");

        if (cmbOptiVersion?.SelectedItem is not VersionOption selectedItem)
        {
            await new ConfirmDialog(
                this,
                "No Versions Available",
                "Please wait for the OptiScaler version list to finish loading, then try again.",
                isAlert: true
            ).ShowDialog<bool>(this);
            return;
        }
        
        string version = selectedItem.Value ?? "";
        bool installFakenvapi = chkFakenvapi?.IsChecked == true;
        bool installNukemFG = chkNukemFG?.IsChecked == true;

        // Get injection method
        var injectionItem = cmbInjectionMethod?.SelectedItem as ComboBoxItem;
        string injectionMethod = injectionItem?.Tag?.ToString() ?? "dxgi.dll";

        // Get selected Extras (FSR4 INT8) version
        var selectedExtrasItem = cmbExtrasVersion?.SelectedItem as ComboBoxItem;
        var selectedExtrasVersion = selectedExtrasItem?.Tag?.ToString();
        bool injectExtras = !string.IsNullOrEmpty(selectedExtrasVersion) &&
                            !selectedExtrasVersion.Equals("none", StringComparison.OrdinalIgnoreCase);

        _isInstalling = true;
        
        var btnInstall = this.FindControl<Button>("BtnInstall");
        var btnCancel = this.FindControl<Button>("BtnCancel");
        var progressSection = this.FindControl<Border>("ProgressSection");
        var txtProgressStatus = this.FindControl<TextBlock>("TxtProgressStatus");
        var txtProgressCount = this.FindControl<TextBlock>("TxtProgressCount");
        var progressBar = this.FindControl<ProgressBar>("ProgressBar");

        if (btnInstall != null) btnInstall.IsEnabled = false;
        if (btnCancel != null) btnCancel.IsEnabled = false;
        if (progressSection != null) progressSection.IsVisible = true;

        int totalGames = selectedGames.Count;
        int successCount = 0;
        var failures = new List<string>();

        try
        {
            var preparedAssets = await PrepareInstallAssetsAsync(
                version,
                installFakenvapi,
                installNukemFG,
                injectExtras ? selectedExtrasVersion : null,
                txtProgressStatus,
                txtProgressCount,
                progressBar);

            if (!preparedAssets.Success)
            {
                return;
            }

            int currentGame = 0;
            foreach (var gameItem in selectedGames)
            {
                currentGame++;

                if (txtProgressStatus != null)
                    txtProgressStatus.Text = $"Installing {gameItem.Name}...";

                if (txtProgressCount != null)
                    txtProgressCount.Text = $"{currentGame} / {totalGames}";

                if (progressBar != null)
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = (currentGame - 1) * 100.0 / totalGames;
                }

                try
                {
                    await Task.Run(() =>
                    {
                        _installService.InstallOptiScaler(
                            gameItem.Game,
                            preparedAssets.OptiCacheDir,
                            injectionMethod,
                            installFakenvapi,
                            preparedAssets.FakenvapiCacheDir,
                            installNukemFG,
                            preparedAssets.NukemFGCacheDir,
                            optiscalerVersion: version
                        );
                    });

                    if (!string.IsNullOrEmpty(preparedAssets.ExtrasDllPath) &&
                        !string.IsNullOrEmpty(selectedExtrasVersion))
                    {
                        await Task.Run(() =>
                        {
                            var gameDir = _installService.DetermineInstallDirectory(gameItem.Game) ?? gameItem.Game.InstallPath;
                            var destPath = Path.Combine(gameDir, "amd_fidelityfx_upscaler_dx12.dll");
                            File.Copy(preparedAssets.ExtrasDllPath, destPath, overwrite: true);
                            gameItem.Game.Fsr4ExtraVersion = selectedExtrasVersion;
                            DebugWindow.Log($"[BulkInstall] Copied FSR4 INT8 DLL to {destPath} for {gameItem.Name}");
                        });
                    }
                    else
                    {
                        gameItem.Game.Fsr4ExtraVersion = null;
                    }

                    gameItem.IsInstalled = true;
                    gameItem.CanInstall = false;
                    gameItem.IsSelected = false;
                    successCount++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{gameItem.Name}: {ex.Message}");
                    DebugWindow.Log($"[BulkInstall] Failed to install {gameItem.Name}: {ex.Message}");
                }

                await Task.Delay(100);
            }

            if (progressBar != null)
            {
                progressBar.IsIndeterminate = false;
                progressBar.Value = 100;
            }

            await Task.Delay(500);

            var summary = successCount == totalGames
                ? $"Successfully installed OptiScaler on {successCount} game{(successCount != 1 ? "s" : "")}."
                : $"Installed OptiScaler on {successCount} of {totalGames} game{(totalGames != 1 ? "s" : "")}.";

            if (failures.Count > 0)
            {
                summary += $"\n\nFailed installs:\n{string.Join("\n", failures)}";
            }

            await new ConfirmDialog(
                this,
                "Bulk Installation Complete",
                summary,
                isAlert: true
            ).ShowDialog<bool>(this);

            if (successCount == totalGames)
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            await new ConfirmDialog(
                this,
                "Installation failed",
                ex.Message,
                isAlert: true
            ).ShowDialog<bool>(this);
        }
        finally
        {
            _isInstalling = false;

            if (progressSection != null) progressSection.IsVisible = false;
            if (btnCancel != null) btnCancel.IsEnabled = true;
            if (progressBar != null) progressBar.IsIndeterminate = false;

            UpdateSelectionCount();
        }
    }

    private async Task<(bool Success, string OptiCacheDir, string FakenvapiCacheDir, string NukemFGCacheDir, string? ExtrasDllPath)>
        PrepareInstallAssetsAsync(
            string version,
            bool installFakenvapi,
            bool installNukemFG,
            string? selectedExtrasVersion,
            TextBlock? txtProgressStatus,
            TextBlock? txtProgressCount,
            ProgressBar? progressBar)
    {
        await _componentService.CheckForUpdatesAsync();

        if (txtProgressCount != null)
        {
            txtProgressCount.Text = "Preparing files...";
        }

        string optiCacheDir = _componentService.GetOptiScalerCachePath(version);
        if (!Directory.Exists(optiCacheDir) ||
            Directory.GetFiles(optiCacheDir, "*.*", SearchOption.AllDirectories).Length == 0)
        {
            if (txtProgressStatus != null)
            {
                txtProgressStatus.Text = $"Downloading OptiScaler {version}...";
            }

            var progress = new Progress<double>(p =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (progressBar != null)
                    {
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = p;
                    }
                }));

            var statusText = new Progress<string>(s =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (txtProgressStatus != null)
                    {
                        txtProgressStatus.Text = s;
                    }
                }));

            await _componentService.DownloadOptiScalerAsync(version, progress, statusText);
        }

        string fakenvapiCacheDir = installFakenvapi ? _componentService.GetFakenvapiCachePath() : "";
        if (installFakenvapi &&
            (!Directory.Exists(fakenvapiCacheDir) ||
             Directory.GetFiles(fakenvapiCacheDir, "*.*", SearchOption.AllDirectories).Length == 0))
        {
            if (txtProgressStatus != null)
            {
                txtProgressStatus.Text = "Downloading Fakenvapi...";
            }

            if (progressBar != null)
            {
                progressBar.IsIndeterminate = true;
            }

            await _componentService.DownloadAndExtractFakenvapiAsync();
        }

        string nukemFGCacheDir = installNukemFG ? _componentService.GetNukemFGCachePath() : "";
        if (installNukemFG &&
            (!Directory.Exists(nukemFGCacheDir) ||
             Directory.GetFiles(nukemFGCacheDir, "*.*", SearchOption.AllDirectories).Length == 0))
        {
            if (txtProgressStatus != null)
            {
                txtProgressStatus.Text = "Waiting for NukemFG file...";
            }

            if (progressBar != null)
            {
                progressBar.IsIndeterminate = true;
            }

            bool provided = await _componentService.ProvideNukemFGManuallyAsync(isUpdate: false);
            if (!provided ||
                !Directory.Exists(nukemFGCacheDir) ||
                Directory.GetFiles(nukemFGCacheDir, "*.*", SearchOption.AllDirectories).Length == 0)
            {
                await new ConfirmDialog(
                    this,
                    "NukemFG required",
                    "Bulk install was cancelled because the NukemFG file was not provided.",
                    isAlert: true
                ).ShowDialog<bool>(this);
                return (false, optiCacheDir, fakenvapiCacheDir, nukemFGCacheDir, null);
            }
        }

        string? extrasDllPath = null;
        if (!string.IsNullOrEmpty(selectedExtrasVersion) &&
            !selectedExtrasVersion.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            if (txtProgressStatus != null)
            {
                txtProgressStatus.Text = $"Downloading FSR4 INT8 v{selectedExtrasVersion}...";
            }

            var extrasProgress = new Progress<double>(p =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (progressBar != null)
                    {
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = p;
                    }
                }));

            try
            {
                extrasDllPath = await _componentService.DownloadExtrasDllAsync(selectedExtrasVersion, extrasProgress);
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[BulkInstall] Failed to download FSR4 INT8 v{selectedExtrasVersion}: {ex.Message}");
                await new ConfirmDialog(
                    this,
                    "FSR4 INT8 download failed",
                    $"OptiScaler will still be installed, but the FSR4 INT8 DLL could not be downloaded:\n{ex.Message}",
                    isAlert: true
                ).ShowDialog<bool>(this);
                extrasDllPath = null;
            }
        }

        return (true, optiCacheDir, fakenvapiCacheDir, nukemFGCacheDir, extrasDllPath);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isInstalling)
        {
            Close();
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isInstalling)
        {
            Close();
        }
    }

    private void CmbOptiVersion_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateCheckboxStatesForVersion(sender as ComboBox);
        UpdateSelectionCount();
    }

    /// <summary>
    /// Populates CmbExtrasVersion with available Extras versions + a "None" option.
    /// Selects the default based on GPU generation: RDNA 4 → None, others → global default or latest.
    /// </summary>
    private void PopulateExtrasComboBox()
    {
        var cmb = this.FindControl<ComboBox>("CmbExtrasVersion");
        if (cmb == null) return;

        cmb.Items.Clear();

        // Add "None" option
        var noneStack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        noneStack.Children.Add(new TextBlock { Text = "None", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        cmb.Items.Add(new ComboBoxItem { Content = noneStack, Tag = "none" });

        // Add available versions
        var versions = _componentService.ExtrasAvailableVersions;
        foreach (var ver in versions)
        {
            var isLatest = ver == _componentService.LatestExtrasVersion;
            var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            stack.Children.Add(new TextBlock { Text = ver, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            if (isLatest)
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#7C3AED")),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 1),
                    Margin = new Thickness(0, 0, 4, 0),
                    Child = new TextBlock { Text = "LATEST", FontSize = 10, Foreground = Brushes.White, FontWeight = FontWeight.Bold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                };
                stack.Children.Add(badge);
            }
            cmb.Items.Add(new ComboBoxItem { Content = stack, Tag = ver });
        }

        // Determine default selection
        bool isRdna4 = false;
        if (_gpuService != null)
        {
            try
            {
                var gpu = _gpuService.GetDiscreteGPU() ?? _gpuService.GetPrimaryGPU();
                // RDNA 4 = Radeon RX 9000 series (GPU name contains "RX 9" or similar)
                isRdna4 = gpu != null && gpu.Vendor == GpuVendor.AMD &&
                          (gpu.Name.Contains(" 9", StringComparison.OrdinalIgnoreCase) ||
                           gpu.Name.Contains("RX 9", StringComparison.OrdinalIgnoreCase));
            }
            catch { /* silent */ }
        }

        // Determine target index
        int targetIndex = 0; // Default to None (index 0)
        var globalDefault = _componentService.Config.DefaultExtrasVersion;

        if (!string.IsNullOrEmpty(globalDefault))
        {
            if (globalDefault.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                targetIndex = 0;
            }
            else
            {
                // Global preference exists (e.g. "v1.0.0"), find it in items
                for (int i = 1; i < cmb.Items.Count; i++)
                {
                    var itemVer = (cmb.Items[i] as ComboBoxItem)?.Tag?.ToString();
                    if (itemVer == globalDefault)
                    {
                        targetIndex = i;
                        break;
                    }
                }

                // If not found (e.g. it was an old version), fallback logic:
                if (targetIndex == 0)
                {
                    // Applying same "intelligent" logic if user's favorite version is gone
                    if (!isRdna4 && versions.Count > 0)
                    {
                        targetIndex = 1; // latest
                    }
                }
            }
        }
        else
        {
            // No global default preference set (DefaultExtrasVersion is null/empty)
            // → Use "intelligent" logic
            if (!isRdna4 && versions.Count > 0)
            {
                targetIndex = 1; // Latest
            }
            else
            {
                targetIndex = 0; // None
            }
        }

        cmb.SelectedIndex = targetIndex;
    }

    private void UpdateCheckboxStatesForVersion(ComboBox? cmb)
    {
        if (cmb == null) return;

        var selectedTag = (cmb?.SelectedItem as VersionOption)?.Value;
        bool isBeta = !string.IsNullOrEmpty(selectedTag) && _componentService.BetaVersions.Contains(selectedTag);

        var chkFakenvapi = this.FindControl<CheckBox>("ChkFakenvapi");
        var chkNukemFG = this.FindControl<CheckBox>("ChkNukemFG");
        var betaInfoPanel = this.FindControl<Border>("BetaInfoPanel");

        if (isBeta)
        {
            if (betaInfoPanel != null)
            {
                betaInfoPanel.IsVisible = true;
            }

            if (chkFakenvapi != null)
            {
                chkFakenvapi.IsEnabled = false;
                chkFakenvapi.IsChecked = false;
                ToolTip.SetTip(chkFakenvapi, "Included in beta version");
            }
            if (chkNukemFG != null)
            {
                chkNukemFG.IsEnabled = false;
                chkNukemFG.IsChecked = false;
                ToolTip.SetTip(chkNukemFG, "Included in beta version");
            }
        }
        else
        {
            if (betaInfoPanel != null)
            {
                betaInfoPanel.IsVisible = false;
            }

            if (chkFakenvapi != null)
            {
                chkFakenvapi.IsEnabled = true;
                ToolTip.SetTip(chkFakenvapi, null);
            }
            if (chkNukemFG != null)
            {
                chkNukemFG.IsEnabled = true;
                ToolTip.SetTip(chkNukemFG, null);
            }
        }
    }

    private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            ApplyFilter(textBox.Text);
        }
    }

    private void TxtSearch_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            // Clear focus when clicking outside
            this.Focus();
        }
    }

    private void ApplyFilter(string? searchText)
    {
        _filteredGameItems.Clear();
        
        if (string.IsNullOrWhiteSpace(searchText))
        {
            // Show all games
            foreach (var game in _allGames)
            {
                _filteredGameItems.Add(game);
            }
        }
        else
        {
            // Filter games
            var filtered = _allGames.Where(g => 
                g.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            
            foreach (var game in filtered)
            {
                _filteredGameItems.Add(game);
            }
        }
    }
}

public class BulkGameItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isInstalled;
    private bool _canInstall;

    public Game Game { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    public string? CoverPath { get; set; }
    public string? OptiscalerVersion { get; set; }
    public bool IsOptiscalerInstalled { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                OnPropertyChanged(nameof(IsInstalled));
            }
        }
    }

    public bool CanInstall
    {
        get => _canInstall;
        set
        {
            if (_canInstall != value)
            {
                _canInstall = value;
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class VersionOption
{
    public string DisplayVersion { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsBeta { get; set; }
    public bool IsLatest { get; set; }
}
