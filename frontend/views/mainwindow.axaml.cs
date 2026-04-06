// OptiScaler Client - A frontend for managing OptiScaler installations
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using OptiscalerClient.Models;
using OptiscalerClient.Services;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using System.Collections.ObjectModel;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System.IO;
using System.Collections.Generic;
using OptiscalerClient.Helpers;
using Ic = FluentIcons.Avalonia;
using Avalonia.Layout;
using FluentIcons.Common;

namespace OptiscalerClient.Views
{
    public partial class MainWindow : Window
    {
        private readonly GameScannerService _scannerService;
        private readonly GamePersistenceService _persistenceService;
        private ObservableCollection<Game> _games;
        private List<Game> _allGames = new List<Game>();
        private readonly ComponentManagementService _componentService;
        private readonly IGpuDetectionService _gpuService;
        private readonly CompatibilityListService _compatibilityService;

        private GpuInfo? _lastDetectedGpu;
        private bool _isInitializingLanguage = true;


        private readonly GameAnalyzerService _analyzerService = new();
        private GameMetadataService _metadataService = null!;

        private ListBox? _lstGames;
        private TextBlock? _txtStatus;
        private Button? _btnScan;
        private Grid? _overlayScanning;
        private TextBox? _txtSearch;
        private TextBlock? _txtSearchPlaceholder;
        private TextBlock? _txtGpuInfo;

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public MainWindow()
        {
            InitializeComponent();
            _scannerService = new GameScannerService();
            _persistenceService = new GamePersistenceService();
            _componentService = new ComponentManagementService();
            _metadataService = new GameMetadataService(_componentService);
            App.ChangeLanguage(_componentService.Config.Language);
            _gpuService = GpuDetectionServiceFactory.Create();
            _compatibilityService = new CompatibilityListService();
            _games = new ObservableCollection<Game>();

            // Debug Window check
            if (_componentService.Config.Debug)
            {
                var debugWindow = new DebugWindow(true);
                debugWindow.Show();
                DebugWindow.Log("Application Started in DEBUG mode.");
            }
            
            _componentService.OnStatusChanged += ComponentStatusChanged;
            this.Loaded += MainWindow_Loaded;
            
            // Restore window state
            RestoreWindowState();
            
            // Handle window state changes
            this.PropertyChanged += Window_PropertyChanged;
        }

        private void ComponentStatusChanged()
        {
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            _lstGames = this.FindControl<ListBox>("LstGames");
            _txtStatus = this.FindControl<TextBlock>("TxtStatus");
            _btnScan = this.FindControl<Button>("BtnScan");
            _overlayScanning = this.FindControl<Grid>("OverlayScanning");
            _txtSearch = this.FindControl<TextBox>("TxtSearch");
            _txtSearchPlaceholder = this.FindControl<TextBlock>("TxtSearchPlaceholder");
            _txtGpuInfo = this.FindControl<TextBlock>("TxtGpuInfo");

            if (_lstGames != null) _lstGames.ItemsSource = _games;

            bool hadSavedGames = LoadSavedGames();
            GpuDetectionServiceFactory.WarmUpAsync(); // Pre-warm GPU cache in background
            _ = LoadGpuInfoAsync();
            _ = CheckUpdatesOnStartupAsync();
            
            // Compatibility List Initial Setup
            var tglComp = this.FindControl<ToggleSwitch>("TglCompatibleOnly");
            if (tglComp != null) tglComp.IsChecked = _componentService.Config.ShowCompatibleOnly;
            UpdateCompatibilityStatusText();

            // Auto-refresh compatibility list on first launch if empty
            if (_compatibilityService.GameCount == 0)
            {
                _ = Task.Run(async () => {
                    await _compatibilityService.RefreshAsync();
                    Dispatcher.UIThread.Post(() => {
                        UpdateCompatibilityStatusText();
                        // Re-tag games after fetch
                        foreach (var g in _allGames) g.IsCompatibleWithOptiscaler = _compatibilityService.IsCompatible(g.Name);
                        ApplyFilter(_txtSearch?.Text);
                    });
                });
            }
            
            UpdateAnimationsState(_componentService.Config.AnimationsEnabled);

            if (_componentService.Config.AutoScan)
            {
                // **UX REFINEMENT**: Render window first, then show scanning animation, then scan
                _ = Task.Run(async () => 
                {
                    await Task.Delay(600); // Wait for premium fade-in
                    
                    Dispatcher.UIThread.Post(() => {
                        // Pre-show scanning state before the heavy work starts
                        if (_txtStatus != null) _txtStatus.Text = GetResourceString("TxtScanningShort", "Scanning for games...");
                        if (_overlayScanning != null) _overlayScanning.IsVisible = true;
                    });

                    await Task.Delay(600); // Visual buffer so user sees the 'Scanning' state
                    Dispatcher.UIThread.Post(() => BtnScan_Click(null!, null!));
                });
            }
        }

        private void UpdateSearchPlaceholderVisibility()
        {
            if (_txtSearchPlaceholder == null || _txtSearch == null) return;

            if (_txtSearch.IsFocused)
            {
                _txtSearchPlaceholder.IsVisible = false;
            }
            else
            {
                _txtSearchPlaceholder.IsVisible = string.IsNullOrEmpty(_txtSearch.Text);
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSearchPlaceholderVisibility();
            if (sender is TextBox textBox)
            {
                ApplyFilter(textBox.Text);
            }
        }

        private void ApplyFilter(string? searchText)
        {
            if (_allGames == null) return;

            var sorted = _allGames.Where(g => PassesFilter(g, searchText)).OrderBy(g => g.Name).ToList();

            _games.Clear();
            foreach (var game in sorted)
            {
                _games.Add(game);
            }
        }

        private bool PassesFilter(Game game, string? searchText = null)
        {
            // 1. Search text filter
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                if (game.Name == null || !game.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // 2. Compatibility filter
            if (_componentService.Config.ShowCompatibleOnly)
            {
                // Visible if: compatible in wiki OR already has OptiScaler installed
                if (!game.IsCompatibleWithOptiscaler && !game.IsOptiscalerInstalled)
                    return false;
            }

            return true;
        }

        private void TxtSearch_GotFocus(object sender, GotFocusEventArgs e)
        {
            UpdateSearchPlaceholderVisibility();
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateSearchPlaceholderVisibility();
        }

        private async void BtnGuide_Click2(object sender, RoutedEventArgs e)
        {
            var guide = new GuideWindow(this);
            await guide.ShowDialog(this);
        }

        private static readonly string[] _viewNames = { "ViewGames", "ViewSettings", "ViewHelp" };

        private void SwitchToView(string viewName)
        {
            foreach (var name in _viewNames)
            {
                var grid = this.FindControl<Grid>(name);
                if (grid == null) continue;
                bool isActive = name == viewName;
                grid.Opacity = isActive ? 1.0 : 0.0;
                grid.IsHitTestVisible = isActive;
            }
        }

        private void NavGames_Click(object sender, RoutedEventArgs e)
        {
            SwitchToView("ViewGames");
        }

        private void NavHelp_Click(object sender, RoutedEventArgs e)
        {
            SwitchToView("ViewHelp");
            PopulateHelpContent();
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            SwitchToView("ViewSettings");

            _isInitializingLanguage = true;
            var cmbLanguage = this.FindControl<ComboBox>("CmbLanguage");
            if (cmbLanguage != null)
            {
                foreach (var baseItem in cmbLanguage.Items)
                {
                    if (baseItem is ComboBoxItem item && item.Tag?.ToString() == App.CurrentLanguage)
                    {
                        cmbLanguage.SelectedItem = item;
                        break;
                    }
                }
            }
            var tglAutoScan = this.FindControl<ToggleSwitch>("TglAutoScan");
            if (tglAutoScan != null)
            {
                tglAutoScan.IsChecked = _componentService.Config.AutoScan;
            }
            var tglAnimations = this.FindControl<ToggleSwitch>("TglAnimations");
            if (tglAnimations != null)
            {
                tglAnimations.IsChecked = _componentService.Config.AnimationsEnabled;
            }
            var tglBetaVersions = this.FindControl<ToggleSwitch>("TglBetaVersions");
            if (tglBetaVersions != null)
            {
                tglBetaVersions.IsChecked = _componentService.Config.ShowBetaVersions;
            }

            // Populate FSR4 INT8 default version selector
            var cmbDefaultExtras = this.FindControl<ComboBox>("CmbDefaultExtrasVersion");
            if (cmbDefaultExtras != null)
            {
                _isInitializingLanguage = true; // reuse flag to suppress SelectionChanged during init
                cmbDefaultExtras.Items.Clear();
                cmbDefaultExtras.Items.Add(new ComboBoxItem { Content = "None", Tag = "none" });
                foreach (var ver in _componentService.ExtrasAvailableVersions)
                {
                    cmbDefaultExtras.Items.Add(new ComboBoxItem { Content = ver, Tag = ver });
                }

                var savedDefault = _componentService.Config.DefaultExtrasVersion;
                cmbDefaultExtras.SelectedIndex = 0; // default: None
                if (!string.IsNullOrEmpty(savedDefault) &&
                    !savedDefault.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 1; i < cmbDefaultExtras.Items.Count; i++)
                    {
                        if ((cmbDefaultExtras.Items[i] as ComboBoxItem)?.Tag?.ToString() == savedDefault)
                        {
                            cmbDefaultExtras.SelectedIndex = i;
                            break;
                        }
                    }
                }
                _isInitializingLanguage = false;
            }
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            var cmbLanguage = sender as ComboBox;
            if (cmbLanguage?.SelectedItem is ComboBoxItem selectedItem)
            {
                string? langCode = selectedItem.Tag?.ToString();
                if (!string.IsNullOrEmpty(langCode))
                {
                    App.ChangeLanguage(langCode);
                    _componentService.Config.Language = langCode;
                    _componentService.SaveConfiguration();
                }
            }
        }

        private async void BtnManageCache_Click(object sender, RoutedEventArgs e)
        {
            var cacheWindow = new CacheManagementWindow(this);
            await cacheWindow.ShowDialog<object>(this);
        }

        private async void BtnManageScanSources_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ManageScanSourcesWindow(this, _componentService);
            await dialog.ShowDialog<bool?>(this);
        }

        private void TglAutoScan_IsCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            if (sender is ToggleSwitch tgl)
            {
                _componentService.Config.AutoScan = tgl.IsChecked ?? true;
                _componentService.SaveConfiguration();
            }
        }

        private void TglAnimations_IsCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            if (sender is ToggleSwitch tgl)
            {
                _componentService.Config.AnimationsEnabled = tgl.IsChecked ?? true;
                _componentService.SaveConfiguration();
                UpdateAnimationsState(_componentService.Config.AnimationsEnabled);
            }
        }

        private void TglBetaVersions_IsCheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            if (sender is ToggleSwitch tgl)
            {
                _componentService.Config.ShowBetaVersions = tgl.IsChecked ?? false;
                _componentService.SaveConfiguration();
            }
        }

        private void TglCompatibleOnly_IsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            if (sender is ToggleSwitch ts)
            {
                _componentService.Config.ShowCompatibleOnly = ts.IsChecked ?? false;
                _componentService.SaveConfiguration();
                ApplyFilter(_txtSearch?.Text);
            }
        }

        private async void BtnRefreshCompatibility_Click(object? sender, RoutedEventArgs e)
        {
            var btn = this.FindControl<Button>("BtnRefreshCompatibility");
            if (btn != null) btn.IsEnabled = false;
            
            var status = this.FindControl<TextBlock>("TxtCompatibilityStatus");
            if (status != null) status.Text = "Refreshing wiki list...";

            await _compatibilityService.RefreshAsync();

            // Refresh game compatibility tags
            foreach (var game in _allGames)
            {
                game.IsCompatibleWithOptiscaler = _compatibilityService.IsCompatible(game.Name);
            }

            UpdateCompatibilityStatusText();
            ApplyFilter(_txtSearch?.Text);
            
            if (btn != null) btn.IsEnabled = true;
        }

        private void UpdateCompatibilityStatusText()
        {
            var status = this.FindControl<TextBlock>("TxtCompatibilityStatus");
            if (status != null && _compatibilityService.GameCount > 0)
            {
                var date = _compatibilityService.LastUpdated.ToString("yyyy-MM-dd");
                status.Text = $"Last updated: {date} ({_compatibilityService.GameCount} games)";
            }
        }

        private void CmbDefaultExtrasVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingLanguage) return;
            if (sender is ComboBox cmb && cmb.SelectedItem is ComboBoxItem item)
            {
                var ver = item.Tag?.ToString() ?? "none";
                _componentService.Config.DefaultExtrasVersion = ver.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : ver;
                _componentService.SaveConfiguration();
            }
        }

        private void UpdateAnimationsState(bool enabled)
        {
            var duration = enabled ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero;
            
            // Update main view transitions
            foreach (var viewName in _viewNames)
            {
                var grid = this.FindControl<Grid>(viewName);
                if (grid?.Transitions != null)
                {
                    grid.Transitions.Clear();
                    if (enabled)
                    {
                        grid.Transitions.Add(new Avalonia.Animation.DoubleTransition 
                        { 
                            Property = Visual.OpacityProperty, 
                            Duration = duration,
                            Easing = new Avalonia.Animation.Easings.CubicEaseOut()
                        });
                    }
                }
            }
        }

        private async Task CheckUpdatesOnStartupAsync()
        {
            try
            {
                if (_txtStatus != null) _txtStatus.Text = GetResourceString("TxtCheckingUpdates", "Checking for updates...");
                await _componentService.CheckForUpdatesAsync();
            }
            catch { }
            finally
            {
                ComponentStatusChanged();
                if (_txtStatus != null) _txtStatus.Text = GetResourceString("TxtReady", "Ready");
            }
        }

        private void PopulateHelpContent()
        {
            var txtAppVersion = this.FindControl<TextBlock>("TxtAppVersion");
            var txtBuildDate = this.FindControl<TextBlock>("TxtBuildDate");
            
            if (txtAppVersion != null) txtAppVersion.Text = $"v{App.AppVersion}";

            try
            {
                var buildDate = System.IO.File.GetLastWriteTime(System.AppContext.BaseDirectory);
                if (txtBuildDate != null) txtBuildDate.Text = buildDate.ToString("yyyy-MM-dd");
            }
            catch
            {
                if (txtBuildDate != null) txtBuildDate.Text = "Unknown";
            }

            var txtOptiVersion = this.FindControl<TextBlock>("TxtOptiVersion");
            var bdOptiUpdate = this.FindControl<Border>("BdOptiUpdate");
            
            if (txtOptiVersion != null)
                txtOptiVersion.Text = string.IsNullOrWhiteSpace(_componentService.OptiScalerVersion) ? "Not installed" : _componentService.OptiScalerVersion;
            
            if (bdOptiUpdate != null)
                bdOptiUpdate.IsVisible = _componentService.IsOptiScalerUpdateAvailable;

            var txtFakeVersion = this.FindControl<TextBlock>("TxtFakeVersion");
            if (txtFakeVersion != null)
                txtFakeVersion.Text = string.IsNullOrWhiteSpace(_componentService.FakenvapiVersion) ? "Not installed" : _componentService.FakenvapiVersion;

            var txtNukemVersion = this.FindControl<TextBlock>("TxtNukemVersion");
            var btnUpdateNukemFG = this.FindControl<Button>("BtnUpdateNukemFG");
            if (_componentService.IsNukemFGInstalled)
            {
                var ver = _componentService.NukemFGVersion;
                if (txtNukemVersion != null) txtNukemVersion.Text = (string.IsNullOrWhiteSpace(ver) || ver == "manual") ? "Available" : ver;
                if (btnUpdateNukemFG != null) btnUpdateNukemFG.Content = "Update";
            }
            else
            {
                if (txtNukemVersion != null) txtNukemVersion.Text = "Not installed";
                if (btnUpdateNukemFG != null) btnUpdateNukemFG.Content = "Install";
            }
        }

        private async void BtnUpdateFakenvapi_Click(object sender, RoutedEventArgs e)
        {
            var btnUpdateFakenvapi = this.FindControl<Button>("BtnUpdateFakenvapi");
            if (btnUpdateFakenvapi == null) return;
            
            btnUpdateFakenvapi.IsEnabled = false;
            var originalContent = btnUpdateFakenvapi.Content;
            btnUpdateFakenvapi.Content = "Checking...";
            try
            {
                await _componentService.CheckForUpdatesAsync();
                
                if (_componentService.IsFakenvapiUpdateAvailable || string.IsNullOrEmpty(_componentService.FakenvapiVersion))
                {
                    btnUpdateFakenvapi.Content = "Downloading...";
                    await _componentService.DownloadAndExtractFakenvapiAsync();
                    await new ConfirmDialog(this, "Success", "Fakenvapi downloaded successfully.").ShowDialog<object>(this);
                    PopulateHelpContent();
                }
                else
                {
                    await new ConfirmDialog(this, "Up to date", "You already have the latest version of Fakenvapi.").ShowDialog<object>(this);
                }
            }
            catch (Exception ex)
            {
                await new ConfirmDialog(this, "Error", $"Error updating Fakenvapi: {ex.Message}").ShowDialog<object>(this);
            }
            finally
            {
                btnUpdateFakenvapi.Content = originalContent;
                btnUpdateFakenvapi.IsEnabled = true;
            }
        }

        private async void BtnUpdateNukemFG_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isUpdate = _componentService.IsNukemFGInstalled;
                DebugWindow.Log($"[NukemFG] Starting manual {(isUpdate ? "update" : "install")}");
                
                bool result = await _componentService.ProvideNukemFGManuallyAsync(isUpdate);
                
                if (result)
                {
                    DebugWindow.Log("[NukemFG] Manual process completed successfully.");
                    PopulateHelpContent();
                }
                else
                {
                    DebugWindow.Log("[NukemFG] Manual process cancelled or failed.");
                }
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[NukemFG] Error: {ex.Message}");
                await new ConfirmDialog(this, "Error", $"Error installing NukemFG: {ex.Message}").ShowDialog<object>(this);
            }
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            var btnCheckUpdates = this.FindControl<Button>("BtnCheckUpdates");
            if (btnCheckUpdates == null) return;

            btnCheckUpdates.IsEnabled = false;
            var originalContent = btnCheckUpdates.Content;
            btnCheckUpdates.Content = GetResourceString("TxtCheckingUpdates", "Checking…");

            try
            {
                // 1. Check for component updates (Fakenvapi, etc)
                await _componentService.CheckForUpdatesAsync();
                PopulateHelpContent();

                // 2. Check for App Updates
                var appUpdateService = new AppUpdateService(_componentService);
                bool hasUpdate = await appUpdateService.CheckForAppUpdateAsync();

                if (hasUpdate)
                {
                    var updateTitle = GetResourceString("TxtUpdateAvailableTitle", "Update Available");
                    var updateMsgFormat = GetResourceString("TxtUpdateAvailableMsg", "A new version is available (v{0}). Download now?");
                    var updateMsg = string.Format(updateMsgFormat, appUpdateService.LatestVersion);

                    var dialog = new ConfirmDialog(this, updateTitle, updateMsg, false);
                    if (await dialog.ShowDialog<bool>(this)) // true if confirmed
                    {
                        btnCheckUpdates.Content = GetResourceString("TxtUpdatingApp", "Updating...");
                        
                        await appUpdateService.DownloadAndPrepareUpdateAsync(new Progress<double>(p => {
                            btnCheckUpdates.Content = $"{GetResourceString("TxtUpdatingApp", "Updating")} ({p:F0}%)";
                        }));

                        var readyTitle = GetResourceString("TxtUpdateReady", "Update Ready");
                        var readyMsg = GetResourceString("TxtUpdateReadyMsg", "Update downloaded. Restarting...");
                        
                        await new ConfirmDialog(this, readyTitle, readyMsg).ShowDialog<object>(this);
                        
                        appUpdateService.FinalizeAndRestart();
                        
                        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.Shutdown();
                        }
                    }
                }
                else if (appUpdateService.IsError)
                {
                    var errorMsg = GetResourceString("TxtUpdateCheckError", "There was a problem checking for updates.");
                    await new ConfirmDialog(this, GetResourceString("TxtUpdateError", "Error"), errorMsg).ShowDialog<object>(this);
                }
                else
                {
                    var noUpdateMsg = GetResourceString("TxtNoUpdateFound", "No new updates found.");
                    await new ConfirmDialog(this, GetResourceString("TxtReady", "Updates"), noUpdateMsg).ShowDialog<object>(this);
                }
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[AppUpdate] Fatal exception: {ex.Message}");
                var errorTitle = GetResourceString("TxtUpdateError", "Error");
                await new ConfirmDialog(this, errorTitle, $"Error: {ex.Message}").ShowDialog<object>(this);
            }
            finally
            {
                btnCheckUpdates.Content = originalContent;
                btnCheckUpdates.IsEnabled = true;
            }
        }

        private void BtnGithub_Click(object sender, RoutedEventArgs e)
        {
            var repoOwner = _componentService.Config.App.RepoOwner ?? "Agustinm28";
            var repoName = _componentService.Config.App.RepoName ?? "Optiscaler-Switcher";
            var url = $"https://github.com/{repoOwner}/{repoName}";

            ProcessHelper.OpenUrl(url);
        }

        private bool LoadSavedGames()
        {
            var savedGames = _persistenceService.LoadGames().OrderBy(g => g.Name).ToList();
            _allGames = savedGames;
            
            ApplyFilter(_txtSearch?.Text);

            var loadedFormat = GetResourceString("TxtLoadedGamesFormat", "Loaded {0} games.");
            if (_txtStatus != null) _txtStatus.Text = string.Format(loadedFormat, savedGames.Count);

            if (savedGames.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    foreach (var game in savedGames)
                    {
                        game.IsCompatibleWithOptiscaler = _compatibilityService.IsCompatible(game.Name);
                        try { _analyzerService.AnalyzeGame(game); }
                        catch { }

                        if (string.IsNullOrEmpty(game.CoverImageUrl) || game.CoverImageUrl.StartsWith("http"))
                        {
                            var appIdKey = !string.IsNullOrEmpty(game.AppId) ? game.AppId :
                                         !string.IsNullOrEmpty(game.Name) ? game.Name : Guid.NewGuid().ToString();

                            game.CoverImageUrl = await _metadataService.FetchAndCacheCoverImageAsync(game.Name, appIdKey);
                        }
                    }

                    // Save the updated analysis results back to disk silently
                    _persistenceService.SaveGames(_allGames);
                });
            }

            return savedGames.Count > 0;
        }

        private async void BtnScan_Click(object? sender, RoutedEventArgs e)
        {
            if (_btnScan != null) _btnScan.IsEnabled = false;
            if (_txtStatus != null) _txtStatus.Text = GetResourceString("TxtScanningShort", "Scanning for games...");
            if (_overlayScanning != null) _overlayScanning.IsVisible = true;

            try
            {
                // **ULTRA-SMOOTH DISCOVERY**: We offload EVERYTHING to background threads.
                // UI thread only handles the dot animation and final collection edits.
                
                var confirmedPaths = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // ── 1. BACKGROUND DISCOVERY & ANALYSIS ──
                var manualGames = _allGames.Where(g => g.Platform == GamePlatform.Manual).ToList();
                var manualAnalysisTask = Task.Run(async () => {
                    using var semaphore = new System.Threading.SemaphoreSlim(4);
                    var tasks = manualGames.Select(async mg => {
                        await semaphore.WaitAsync();
                        try {
                            _analyzerService.AnalyzeGame(mg); // Reactive update via INotifyPropertyChanged
                            lock(confirmedPaths) confirmedPaths.Add(mg.InstallPath);
                        } finally {
                            semaphore.Release();
                        }
                    });
                    await Task.WhenAll(tasks);
                });

                var discoveryTask = Task.Run(() => _scannerService.ScanAllGamesAsync(_componentService.Config.ScanSources));

                // Wait for background tasks to complete without blocking UI thread
                await Task.WhenAll(manualAnalysisTask, discoveryTask);
                
                var scanResults = await discoveryTask;

                // ── 2. BACKGROUND RECONCILIATION ──
                // Determine additions and removals on a separate thread
                var reconciliationPlan = await Task.Run(async () => {
                    var toAdd = new List<Game>();
                    var toRemove = new List<Game>();
                    
                    foreach (var scannedGame in scanResults)
                    {
                        var existing = _allGames.FirstOrDefault(g => g.InstallPath.Equals(scannedGame.InstallPath, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            lock(confirmedPaths) confirmedPaths.Add(existing.InstallPath);
                            existing.Platform = scannedGame.Platform; 
                        }
                        else
                        {
                            // Prepare cover images in background too
                            if (string.IsNullOrEmpty(scannedGame.CoverImageUrl))
                            {
                                var appIdKey = !string.IsNullOrEmpty(scannedGame.AppId) ? scannedGame.AppId : scannedGame.Name;
                                scannedGame.CoverImageUrl = await _metadataService.FetchAndCacheCoverImageAsync(scannedGame.Name, appIdKey);
                            }
                            scannedGame.IsCompatibleWithOptiscaler = _compatibilityService.IsCompatible(scannedGame.Name);
                            toAdd.Add(scannedGame);
                            lock(confirmedPaths) confirmedPaths.Add(scannedGame.InstallPath);
                        }
                    }

                    // Identify stale games
                    var stale = _allGames.Where(g => g.Platform != GamePlatform.Manual && !confirmedPaths.Contains(g.InstallPath)).ToList();
                    toRemove.AddRange(stale);

                    return (toAdd, toRemove);
                });

                // ── 3. FINAL UI UPDATES (Thread-Safe, Throttled & Fluid) ──
                // Update master list first
                foreach (var game in reconciliationPlan.toAdd) _allGames.Add(game);
                foreach (var game in reconciliationPlan.toRemove) _allGames.Remove(game);
                _allGames = _allGames.OrderBy(g => g.Name).ToList();

                // Stream only filtered results into visibility
                int batchSize = 5;
                int processed = 0;
                string? currentSearch = _txtSearch?.Text;

                foreach (var game in reconciliationPlan.toAdd.OrderBy(g => g.Name))
                {
                    if (!PassesFilter(game, currentSearch)) continue;

                    int insertIndex = 0;
                    while (insertIndex < _games.Count && string.Compare(_games[insertIndex].Name, game.Name, StringComparison.OrdinalIgnoreCase) <= 0)
                    {
                        insertIndex++;
                    }
                    _games.Insert(insertIndex, game);
                    processed++;

                    if (processed % batchSize == 0)
                    {
                        await Task.Delay(12); 
                    }
                }

                processed = 0;
                foreach (var game in reconciliationPlan.toRemove)
                {
                    _games.Remove(game);
                    processed++;

                    if (processed % batchSize == 0)
                    {
                        await Task.Delay(12);
                    }
                }

                ApplyFilter(currentSearch);

                _persistenceService.SaveGames(_allGames);

                var scanCompleteFormat = GetResourceString("TxtScanCompleteFormat", "Scan complete. Total games: {0}");
                if (_txtStatus != null) _txtStatus.Text = string.Format(scanCompleteFormat, _games.Count);
            }
            catch (Exception ex)
            {
                var errorFormat = GetResourceString("TxtErrorFormat", "Error: {0}");
                if (_txtStatus != null) _txtStatus.Text = string.Format(errorFormat, ex.Message);
            }
            finally
            {
                if (_btnScan != null) _btnScan.IsEnabled = true;
                if (_overlayScanning != null) _overlayScanning.IsVisible = false;
            }
        }

        private async void BtnAddManual_Click(object sender, RoutedEventArgs e)
        {
            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = GetResourceString("TxtSelectExe", "Select Game Executable"),
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Executable Files (*.exe)")
                    {
                        Patterns = new List<string> { "*.exe" }
                    }
                }
            });

            if (files != null && files.Count > 0)
            {
                try
                {
                    var filePath = files[0].Path.LocalPath;
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    var exePath = files[0].Path.LocalPath;
                    var newGame = new Game
                    {
                        Name = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(exePath)) ?? "Manual Game",
                        ExecutablePath = exePath,
                        InstallPath = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty,
                        Platform = GamePlatform.Manual,
                        AppId = "Manual_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        IsCompatibleWithOptiscaler = _compatibilityService.IsCompatible(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(exePath)) ?? "")
                    };

                    _analyzerService.AnalyzeGame(newGame);
                    newGame.CoverImageUrl = await _metadataService.FetchAndCacheCoverImageAsync(newGame.Name, newGame.AppId);

                    _allGames.Add(newGame);
                    _allGames = _allGames.OrderBy(g => g.Name).ToList();
                    _persistenceService.SaveGames(_allGames);
                    
                    ApplyFilter(_txtSearch?.Text);

                    if (_txtStatus != null) _txtStatus.Text = string.Format(GetResourceString("TxtAddedRefFormat", "Added {0} manually."), newGame.Name);
                }
                catch (Exception ex)
                {
                    await new ConfirmDialog(this, GetResourceString("TxtError", "Error"), ex.Message, isAlert: true).ShowDialog<object>(this);
                }
            }
        }

        private async void BtnBulkInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_games.Count == 0)
            {
                await new ConfirmDialog(
                    this,
                    GetResourceString("TxtNoGames", "No Games"),
                    GetResourceString("TxtNoGamesFound", "No games found. Please scan for games first."),
                    isAlert: true
                ).ShowDialog<bool>(this);
                return;
            }

            var installService = new GameInstallationService();
            var bulkWindow = new BulkInstallWindow(_componentService, installService, _games.ToList());
            await bulkWindow.ShowDialog<object>(this);

            // Refresh game list after bulk install
            if (_lstGames != null)
            {
                _lstGames.ItemsSource = null;
                _lstGames.ItemsSource = _games;
            }
        }

        private async void BtnManage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Game selectedGame)
            {
                var manageWindow = new ManageGameWindow(this, selectedGame);
                await manageWindow.ShowDialog<object>(this);

                var index = _games.IndexOf(selectedGame);
                if (index != -1)
                {
                    _games[index] = selectedGame;
                    _persistenceService.SaveGames(_games);
                }


            }
        }



        private async void BtnFastInstall_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Game selectedGame)
            {
                try
                {
                    // Check if OptiScaler is already installed
                    if (selectedGame.IsOptiscalerInstalled)
                    {
                        // Uninstall OptiScaler directly without confirmation
                        var installService = new GameInstallationService();
                        installService.UninstallOptiScaler(selectedGame);
                        
                        // Update game status via analysis to be 100% sure
                        _analyzerService.AnalyzeGame(selectedGame);
                        

                        
                        _persistenceService.SaveGames(_allGames);
                    }
                    else
                    {
                        // Install OptiScaler
                        var installService = new GameInstallationService();
                        
                        // Determine version to install based on beta setting
                        string versionToInstall;
                        
                        if (_componentService.Config.ShowBetaVersions)
                        {
                            // Install latest beta
                            versionToInstall = _componentService.LatestBetaVersion ?? "";
                        }
                        else
                        {
                            // Install latest stable (use the version marked as latest in GitHub)
                            versionToInstall = _componentService.LatestStableVersion ?? "";
                        }
                        
                        if (string.IsNullOrEmpty(versionToInstall))
                        {
                            await new ConfirmDialog(
                                this,
                                GetResourceString("TxtNoVersions", "No Versions Available"),
                                GetResourceString("TxtNoVersionsFound", "No OptiScaler versions are available for installation."),
                                isAlert: true
                            ).ShowDialog<bool>(this);
                            return;
                        }
                        
                        // Get cache paths
                        var optiCacheDir = _componentService.GetOptiScalerCachePath(versionToInstall);
                        
                        // Download OptiScaler if not in cache
                        if (!Directory.Exists(optiCacheDir) || Directory.GetFiles(optiCacheDir, "*.*", SearchOption.AllDirectories).Length == 0)
                        {
                            // Show downloading dialog
                            var downloadDialog = new ConfirmDialog(
                                this,
                                "Downloading OptiScaler",
                                $"Downloading OptiScaler {versionToInstall}...\n\nPlease wait.",
                                isAlert: true,
                                hideButtons: true
                            );
                            
                            // Activate the progress bar before showing
                            downloadDialog.ShowProgress();
                            
                            // Establish a thread-safe progress bridge
                            var progress = new Progress<double>(p => 
                            {
                                downloadDialog.UpdateProgress(p);
                            });

                            var statusText = new Progress<string>(s => 
                            {
                                downloadDialog.UpdateMessage($"{s}\n\nPlease wait.");
                            });
                            
                            // Start download in background
                            var downloadTask = _componentService.DownloadOptiScalerAsync(versionToInstall, progress, statusText);
                            
                            // Show dialog without blocking
                            var dialogTask = downloadDialog.ShowDialog<bool>(this);
                            
                            try
                            {
                                // Wait for download to complete
                                await downloadTask;
                                
                                // Close dialog after download completes
                                downloadDialog.Close();
                            }
                            catch (Exception downloadEx)
                            {
                                // Close downloading dialog
                                downloadDialog.Close();
                                
                                // Show error dialog
                                await new ConfirmDialog(
                                    this,
                                    GetResourceString("TxtError", "Error"),
                                    $"Failed to download OptiScaler {versionToInstall}: {downloadEx.Message}",
                                    isAlert: true
                                ).ShowDialog<bool>(this);
                                return;
                            }
                        }
                        
                        var fakeCacheDir = _componentService.GetFakenvapiCachePath();
                        var nukemCacheDir = _componentService.GetNukemFGCachePath();
                        
                        // Install with default settings (backup always enabled)
                        // Always install Fakenvapi and NukemFG by default
                        installService.InstallOptiScaler(
                            selectedGame,
                            optiCacheDir,
                            "dxgi.dll",
                            installFakenvapi: true, // Always install Fakenvapi
                            fakenvapiCachePath: fakeCacheDir,
                            installNukemFG: true,  // Always install NukemFG
                            nukemFGCachePath: nukemCacheDir,
                            optiscalerVersion: versionToInstall
                        );
                        
                        // Update game status
                        selectedGame.IsOptiscalerInstalled = true;
                        selectedGame.OptiscalerVersion = versionToInstall;
                        

                        
                        _persistenceService.SaveGames(_allGames);
                    }
                }
                catch (Exception ex)
                {
                    await new ConfirmDialog(
                        this,
                        GetResourceString("TxtError", "Error"),
                        ex.Message,
                        isAlert: true
                    ).ShowDialog<bool>(this);
                }
            }
        }

        private async void BtnRemoveGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Game game)
            {
                var title = GetResourceString("TxtRemoveGameTitle", "Remove Game");
                var confirmFormat = GetResourceString("TxtRemoveGameConfirm", "Are you sure you want to remove '{0}' from the list?");
                var message = string.Format(confirmFormat, game.Name);

                var dialog = new ConfirmDialog(this, title, message, false);
                var result = await dialog.ShowDialog<bool>(this); // true if confirmed

                if (result)
                {
                    _games.Remove(game);
                    _allGames.Remove(game);
                    _persistenceService.SaveGames(_allGames);
                }
            }
        }

        private async Task LoadGpuInfoAsync()
        {
            try
            {
                if (_txtGpuInfo == null) return;
                
                GpuInfo? gpu;
                if (_lastDetectedGpu != null)
                {
                    gpu = _lastDetectedGpu;
                }
                else
                {
                    _txtGpuInfo!.Text = GetResourceString("TxtDefaultGpu", "Detecting GPU...");
                    gpu = await Task.Run(() =>
                    {
                        if (_gpuService != null)
                        {
                            try
                            {
                                return _gpuService.GetDiscreteGPU() ?? _gpuService.GetPrimaryGPU();
                            }
                            catch { return null; }
                        }
                        return null;
                    });
                    _lastDetectedGpu = gpu;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (gpu != null)
                    {
                        string icon = "⚪";
                        IBrush color = Brushes.Gray;

                        switch (gpu.Vendor)
                        {
                            case GpuVendor.NVIDIA:
                                icon = "🟢"; color = new SolidColorBrush(Color.FromRgb(118, 185, 0)); break;
                            case GpuVendor.AMD:
                                icon = "🔴"; color = new SolidColorBrush(Color.FromRgb(237, 28, 36)); break;
                            case GpuVendor.Intel:
                                icon = "🔵"; color = new SolidColorBrush(Color.FromRgb(0, 113, 197)); break;
                        }

                        _txtGpuInfo!.Text = $"{icon} {gpu.Name}";
                        _txtGpuInfo.Foreground = color;
                        ToolTip.SetTip(_txtGpuInfo, $"{gpu.Name}\nVendor: {gpu.Vendor}\nVRAM: {gpu.VideoMemoryGB}\nDriver: {gpu.DriverVersion}");
                    }
                    else
                    {
                        _txtGpuInfo!.Text = GetResourceString("TxtNoGpu", "⚠️ No GPU detected");
                        _txtGpuInfo.Foreground = Brushes.Orange;
                        ToolTip.SetTip(_txtGpuInfo, GetResourceString("TxtNoGpuTip", "No GPU was detected on this system"));
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_txtGpuInfo != null)
                    {
                        _txtGpuInfo.Text = GetResourceString("TxtGpuFail", "⚠️ GPU detection failed");
                        _txtGpuInfo.Foreground = Brushes.Gray;
                        var format = GetResourceString("TxtGpuFailTipFormat", "Error detecting GPU: {0}");
                        ToolTip.SetTip(_txtGpuInfo, string.Format(format, ex.Message));
                    }
                });
            }
        }



        #region Window State Persistence

        private void RestoreWindowState()
        {
            var config = _componentService.Config;
            
            // Restore window size
            if (config.WindowWidth > 0 && config.WindowHeight > 0)
            {
                this.Width = config.WindowWidth;
                this.Height = config.WindowHeight;
            }
            
            // Restore window position (only if valid)
            if (!double.IsNaN(config.WindowLeft) && !double.IsNaN(config.WindowTop) &&
                config.WindowLeft >= 0 && config.WindowTop >= 0)
            {
                this.Position = new PixelPoint((int)config.WindowLeft, (int)config.WindowTop);
            }
            
            // Restore maximized state
            if (config.WindowMaximized)
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        private void Window_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            // Save window state on any relevant property change
            SaveWindowState();
        }

        private void SaveWindowState()
        {
            var config = _componentService.Config;
            
            // Save window size
            if (this.WindowState != WindowState.Maximized)
            {
                config.WindowWidth = this.Width;
                config.WindowHeight = this.Height;
            }
            
            // Save window position
            var position = this.Position;
            config.WindowLeft = position.X;
            config.WindowTop = position.Y;
            
            // Save maximized state
            config.WindowMaximized = this.WindowState == WindowState.Maximized;
            
            // Save configuration
            _componentService.SaveConfiguration();
        }

        #endregion

        private string GetResourceString(string key, string fallback)
        {
            return Application.Current?.TryFindResource(key, out var res) == true && res is string str ? str : fallback;
        }
    }
}