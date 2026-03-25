using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using OptiscalerClient.Models;
using OptiscalerClient.Views;

namespace OptiscalerClient.Services
{
    /// <summary>
    /// Manages OptiScaler, Fakenvapi, and NukemFG components
    /// </summary>
    public class ComponentManagementService
    {
        private readonly string _baseDir;
        private readonly string _cacheDir;
        private readonly string _versionFile;
        private readonly string _configFile;
        private readonly HttpClient _httpClient;

        public AppConfiguration Config => _config;
        private AppConfiguration _config = new();
        private ComponentVersions _localVersions = new();
        private ComponentVersions _remoteVersions = new();

        private static System.Collections.Generic.List<string>? _cachedOptiScalerVersions = null;
        private static System.Collections.Generic.HashSet<string> _cachedBetaVersions = new();
        private static string? _cachedLatestBetaVersion = null;
        private static string? _cachedFakenvapiVersion = null;
        private static string? _cachedNukemFGVersion = null;
        private static DateTime _lastApiCheckTime = DateTime.MinValue;

        public System.Collections.Generic.List<string> OptiScalerAvailableVersions
            => _cachedOptiScalerVersions ?? GetDownloadedOptiScalerVersions();
        public System.Collections.Generic.HashSet<string> BetaVersions => _cachedBetaVersions;
        public string? LatestBetaVersion => _cachedLatestBetaVersion;

        public string? OptiScalerVersion => _localVersions.OptiScalerVersion;
        public string? FakenvapiVersion => _localVersions.FakenvapiVersion;
        public string? NukemFGVersion => _localVersions.NukemFGVersion;

        public bool IsOptiScalerUpdateAvailable { get; private set; }
        public bool IsFakenvapiUpdateAvailable { get; private set; }
        public bool IsNukemFGUpdateAvailable { get; private set; }

        /// <summary>
        /// True if the NukemFG DLL is present in local cache.
        /// </summary>
        public bool IsNukemFGInstalled => File.Exists(GetNukemFGDllPath());

        public event Action? OnStatusChanged;
        public Exception? LastError { get; private set; }

        public ComponentManagementService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _baseDir = Path.Combine(appData, "OptiscalerClient");
            _cacheDir = Path.Combine(_baseDir, "Cache");
            _versionFile = Path.Combine(_baseDir, "versions.json");
            _configFile = Path.Combine(_baseDir, "config.json");

            Directory.CreateDirectory(_cacheDir);

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OptiscalerClient");

            LoadConfiguration();
            LoadLocalVersions();
        }

        private void LoadConfiguration()
        {
            try
            {
                // Check local directory first (portable/dev friendly)
                var localConfig = Path.Combine(AppContext.BaseDirectory, "config.json");
                if (File.Exists(localConfig))
                {
                    var json = File.ReadAllText(localConfig);
                    _config = JsonSerializer.Deserialize(json, OptimizerContext.Default.AppConfiguration) ?? new();
                }
                else if (File.Exists(_configFile))
                {
                    var json = File.ReadAllText(_configFile);
                    _config = JsonSerializer.Deserialize(json, OptimizerContext.Default.AppConfiguration) ?? new();
                }
                else
                {
                    // Create default config
                    _config = new AppConfiguration();
                    var json = JsonSerializer.Serialize(_config, OptimizerContext.Default.AppConfiguration);
                    File.WriteAllText(_configFile, json);
                }
            }
            catch { /* Use defaults */ }
        }

        public void SaveConfiguration()
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, OptimizerContext.Default.AppConfiguration);

                var localConfig = Path.Combine(AppContext.BaseDirectory, "config.json");
                if (File.Exists(localConfig))
                {
                    File.WriteAllText(localConfig, json);
                }
                else
                {
                    File.WriteAllText(_configFile, json);
                }
            }
            catch { /* Ignore save errors */ }
        }

        private void LoadLocalVersions()
        {
            if (File.Exists(_versionFile))
            {
                try
                {
                    var json = File.ReadAllText(_versionFile);
                    _localVersions = JsonSerializer.Deserialize(json, OptimizerContext.Default.ComponentVersions) ?? new();
                }
                catch { /* Corrupt file */ }
            }
        }

        private void SaveLocalVersions()
        {
            try
            {
                var json = JsonSerializer.Serialize(_localVersions, OptimizerContext.Default.ComponentVersions);
                File.WriteAllText(_versionFile, json);
            }
            catch { /* Ignore save errors */ }
        }

        public async Task CheckForUpdatesAsync()
        {
            LastError = null;
            try
            {
                // To avoid spamming GitHub API (rate limits), only check every 15 minutes max per session
                if (_cachedOptiScalerVersions == null || (DateTime.Now - _lastApiCheckTime).TotalMinutes > 15)
                {
                    DebugWindow.Log($"[ComponentCheck] Fetching updates from GitHub API (Rates: {(DateTime.Now - _lastApiCheckTime).ToString(@"hh\:mm\:ss")} since last check)");
                    // Always fetch both stable and beta versions
                    var optiVersionsTask = FetchAllComponentVersionsAsync(_config.OptiScaler);
                    var optiBetasTask = FetchAllComponentVersionsAsync(_config.OptiScalerBetas);
                    var fakeTask = CheckComponentUpdateAsync("Fakenvapi", _config.Fakenvapi);
                    var nukemTask = CheckComponentUpdateAsync("NukemFG", _config.NukemFG);

                    await Task.WhenAll(optiVersionsTask, optiBetasTask, fakeTask, nukemTask);

                    var stableVersions = await optiVersionsTask;
                    var betaVersions = await optiBetasTask;

                    // Track which versions are betas
                    _cachedBetaVersions = new System.Collections.Generic.HashSet<string>(betaVersions ?? new());
                    _cachedLatestBetaVersion = betaVersions?.FirstOrDefault();

                    // Merge stable and beta versions, removing duplicates
                    var allVersions = new System.Collections.Generic.List<string>();
                    if (stableVersions != null) allVersions.AddRange(stableVersions);
                    if (betaVersions != null) allVersions.AddRange(betaVersions);
                    
                    if (allVersions.Count > 0)
                    {
                        _cachedOptiScalerVersions = allVersions.Distinct().ToList();
                    }
                    _cachedFakenvapiVersion = await fakeTask ?? _cachedFakenvapiVersion;
                    _cachedNukemFGVersion = await nukemTask ?? _cachedNukemFGVersion;

                    _lastApiCheckTime = DateTime.Now;
                }

                // Select default version based on user preference
                if (_config.ShowBetaVersions && _cachedLatestBetaVersion != null)
                {
                    // User prefers latest beta as default
                    _remoteVersions.OptiScalerVersion = _cachedLatestBetaVersion;
                }
                else
                {
                    // Default to latest stable LTSC version
                    _remoteVersions.OptiScalerVersion = OptiScalerAvailableVersions.FirstOrDefault(v => !v.Contains("nightly", StringComparison.OrdinalIgnoreCase)) ?? OptiScalerAvailableVersions.FirstOrDefault();
                }
                _remoteVersions.FakenvapiVersion = _cachedFakenvapiVersion;
                _remoteVersions.NukemFGVersion = _cachedNukemFGVersion;

                // Check if updates are available
                IsOptiScalerUpdateAvailable = IsUpdateAvailable(_localVersions.OptiScalerVersion, _remoteVersions.OptiScalerVersion);
                IsFakenvapiUpdateAvailable = IsUpdateAvailable(_localVersions.FakenvapiVersion, _remoteVersions.FakenvapiVersion);
                IsNukemFGUpdateAvailable = IsUpdateAvailable(_localVersions.NukemFGVersion, _remoteVersions.NukemFGVersion);

                DebugWindow.Log($"[ComponentUpdate] Status: Opti={IsOptiScalerUpdateAvailable} (Local={_localVersions.OptiScalerVersion}, Remote={_remoteVersions.OptiScalerVersion})");
                DebugWindow.Log($"[ComponentUpdate] Status: Fake={IsFakenvapiUpdateAvailable} (Local={_localVersions.FakenvapiVersion}, Remote={_remoteVersions.FakenvapiVersion})");
                DebugWindow.Log($"[ComponentUpdate] Status: Nukem={IsNukemFGUpdateAvailable} (Local={_localVersions.NukemFGVersion}, Remote={_remoteVersions.NukemFGVersion})");

                OnStatusChanged?.Invoke();
            }
            catch (Exception ex)
            {
                LastError = ex;
                throw;
            }
        }

        private async Task<string?> CheckComponentUpdateAsync(string componentName, RepositoryConfig config)
        {
            try
            {
                var url = $"https://api.github.com/repos/{config.RepoOwner}/{config.RepoName}/releases/latest";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("tag_name", out var tagName))
                {
                    var version = tagName.GetString();
                    DebugWindow.Log($"[ComponentCheck] {componentName} Raw Tag: {version}");
                    // Strip the conventional "v" prefix (e.g. "v0.7.1" → "0.7.1")
                    if (version != null && version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                        version = version.Substring(1);
                    return version;
                }
            }
            catch (Exception ex)
            { 
                DebugWindow.Log($"[ComponentCheck] {componentName} failed: {ex.Message}");
            }

            return null;
        }

        private async Task<System.Collections.Generic.List<string>> FetchAllComponentVersionsAsync(RepositoryConfig config)
        {
            var versions = new System.Collections.Generic.List<string>();
            var repoLabel = $"{config.RepoOwner}/{config.RepoName}";
            try
            {
                if (string.IsNullOrEmpty(config.RepoOwner) || string.IsNullOrEmpty(config.RepoName))
                {
                    DebugWindow.Log($"[FetchVersions] Skipping {repoLabel}: empty config");
                    return versions;
                }

                var url = $"https://api.github.com/repos/{config.RepoOwner}/{config.RepoName}/releases?per_page=30";
                DebugWindow.Log($"[FetchVersions] GET {url}");
                var response = await _httpClient.GetAsync(url);
                DebugWindow.Log($"[FetchVersions] {repoLabel} → HTTP {(int)response.StatusCode}");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("tag_name", out var tagName))
                    {
                        var version = tagName.GetString();
                        if (version != null)
                        {
                            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                                version = version.Substring(1);
                            versions.Add(version);
                        }
                    }
                }
                DebugWindow.Log($"[FetchVersions] {repoLabel} → {versions.Count} version(s): [{string.Join(", ", versions)}]");
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[FetchVersions] {repoLabel} → ERROR: {ex.Message}");
            }

            return versions;
        }

        private bool IsUpdateAvailable(string? localVersion, string? remoteVersion)
        {
            if (string.IsNullOrEmpty(remoteVersion))
                return false;

            if (string.IsNullOrEmpty(localVersion))
                return true;

            return localVersion != remoteVersion;
        }

        public async Task DownloadAndExtractAllAsync()
        {
            var errors = new System.Collections.Generic.List<string>();

            // Try to download each component independently
            try
            {
                // We no longer auto-download OptiScaler here. It's fetched per-version on demand.
            }
            catch (Exception ex)
            {
                errors.Add($"OptiScaler: {ex.Message}");
            }

            try
            {
                await DownloadAndExtractFakenvapiAsync();
            }
            catch (Exception ex)
            {
                errors.Add($"Fakenvapi: {ex.Message}");
            }

            // NukemFG is never downloaded automatically — it is always provided manually.
            // If the DLL is not present yet, we prompt the user here.
            if (!IsNukemFGInstalled)
            {
                bool provided = await ProvideNukemFGManuallyAsync(isUpdate: false);
                if (!provided)
                    errors.Add("NukemFG: Manual download was skipped.");
            }

            // If all failed, throw
            if (errors.Count == 3)
            {
                throw new Exception($"All downloads failed:\n{string.Join("\n", errors)}");
            }

            // If some failed, store in LastError but don't throw
            if (errors.Count > 0)
            {
                LastError = new Exception($"Some downloads failed:\n{string.Join("\n", errors)}");
            }
        }

        public async Task<string> DownloadOptiScalerAsync(string version, IProgress<double>? progress = null)
        {
            if (string.IsNullOrEmpty(version))
                throw new Exception("Version cannot be empty");

            var extractPath = GetOptiScalerCachePath(version);
            if (Directory.Exists(extractPath) && Directory.GetFiles(extractPath).Length > 0)
            {
                DebugWindow.Log($"[Download] OptiScaler v{version} already cached at {extractPath}");
                return extractPath; // Already downloaded
            }

            LastError = null;
            DebugWindow.Log($"[Download] Starting download of OptiScaler v{version}");
            DebugWindow.Log($"[Download] Cache path: {extractPath}");
            
            try
            {
                // Try to retrieve release from stable repo first, then beta repo
                HttpResponseMessage? response = null;
                string? json = null;
                string repoSource = "";
                
                // Try stable repo with v prefix
                var url = $"https://api.github.com/repos/{_config.OptiScaler.RepoOwner}/{_config.OptiScaler.RepoName}/releases/tags/v{version}";
                DebugWindow.Log($"[Download] Trying stable repo (with v prefix): {url}");
                response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    // Try stable repo without v prefix
                    url = $"https://api.github.com/repos/{_config.OptiScaler.RepoOwner}/{_config.OptiScaler.RepoName}/releases/tags/{version}";
                    DebugWindow.Log($"[Download] Trying stable repo (without v prefix): {url}");
                    response = await _httpClient.GetAsync(url);
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    // Try beta repo with v prefix
                    url = $"https://api.github.com/repos/{_config.OptiScalerBetas.RepoOwner}/{_config.OptiScalerBetas.RepoName}/releases/tags/v{version}";
                    DebugWindow.Log($"[Download] Trying beta repo (with v prefix): {url}");
                    response = await _httpClient.GetAsync(url);
                    repoSource = " (beta repo)";
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    // Try beta repo without v prefix
                    url = $"https://api.github.com/repos/{_config.OptiScalerBetas.RepoOwner}/{_config.OptiScalerBetas.RepoName}/releases/tags/{version}";
                    DebugWindow.Log($"[Download] Trying beta repo (without v prefix): {url}");
                    response = await _httpClient.GetAsync(url);
                    repoSource = " (beta repo)";
                }
                
                response.EnsureSuccessStatusCode();
                DebugWindow.Log($"[Download] Release found{repoSource} for OptiScaler v{version}");

                json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                string? downloadUrl = null;
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.TryGetProperty("browser_download_url", out var urlProp))
                        {
                            var assetUrl = urlProp.GetString();
                            if (assetUrl != null && (assetUrl.EndsWith(".zip") || assetUrl.EndsWith(".7z")))
                            {
                                downloadUrl = assetUrl;
                                DebugWindow.Log($"[Download] Found download asset: {Path.GetFileName(assetUrl)}");
                                break;
                            }
                        }
                    }
                }

                if (downloadUrl == null)
                    throw new Exception("No downloadable asset found for the specified OptiScaler version.");

                // Create folder
                Directory.CreateDirectory(extractPath);
                DebugWindow.Log($"[Download] Created cache directory: {extractPath}");

                // Download with optional progress simulation or reading stream. 
                // We'll read the stream for progress:
                DebugWindow.Log($"[Download] Starting file download from: {Path.GetFileName(downloadUrl)}");
                using var dlResponse = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                dlResponse.EnsureSuccessStatusCode();

                var totalBytes = dlResponse.Content.Headers.ContentLength ?? 10 * 1024 * 1024; // fallback 10MB
                var tempZip = Path.Combine(Path.GetTempPath(), $"OptiScaler_{version}_{Guid.NewGuid()}.zip");
                DebugWindow.Log($"[Download] Download size: {totalBytes:N0} bytes, temp file: {Path.GetFileName(tempZip)}");

                long totalRead = 0;
                using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 65536)) // 64KB buffer
                using (var cs = await dlResponse.Content.ReadAsStreamAsync())
                {
                    var buffer = new byte[65536]; // 64KB buffer for faster download
                    var isMoreToRead = true;
                    var lastProgressLog = DateTime.MinValue;

                    do
                    {
                        var read = await cs.ReadAsync(buffer, 0, buffer.Length);
                        if (read == 0)
                        {
                            isMoreToRead = false;
                        }
                        else
                        {
                            await fs.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            var progressPercent = (double)totalRead / totalBytes * 100;
                            progress?.Report(progressPercent);
                            
                            // Log progress every 10 seconds
                            if (DateTime.Now - lastProgressLog > TimeSpan.FromSeconds(10))
                            {
                                DebugWindow.Log($"[Download] Progress: {progressPercent:F1}% ({totalRead:N0}/{totalBytes:N0} bytes)");
                                lastProgressLog = DateTime.Now;
                            }
                        }
                    }
                    while (isMoreToRead);
                }

                DebugWindow.Log($"[Download] Download completed: {totalRead:N0} bytes downloaded");

                // Ensure 100% is reached
                progress?.Report(100);

                // Extract
                DebugWindow.Log($"[Extract] Starting extraction of {Path.GetFileName(tempZip)} to {extractPath}");
                var extractStartTime = DateTime.Now;
                var fileCount = 0;
                
                using (var archive = ArchiveFactory.Open(tempZip))
                {
                    var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                    
                    foreach (var entry in entries)
                    {
                        var entryKey = entry.Key ?? string.Empty;
                        var destPath = Path.Combine(extractPath, entryKey);
                        var destDir = Path.GetDirectoryName(destPath);
                        
                        if (destDir != null && !Directory.Exists(destDir))
                            Directory.CreateDirectory(destDir);
                        
                        using (var entryStream = entry.OpenEntryStream())
                        using (var fileStream = File.Create(destPath))
                        {
                            entryStream.CopyTo(fileStream, 81920); // 80KB buffer for faster extraction
                        }
                        
                        fileCount++;
                    }
                }
                
                var extractDuration = DateTime.Now - extractStartTime;
                DebugWindow.Log($"[Extract] Extraction completed: {fileCount} files extracted in {extractDuration.TotalSeconds:F1} seconds");

                File.Delete(tempZip);
                DebugWindow.Log($"[Download] Temporary file deleted: {Path.GetFileName(tempZip)}");

                _localVersions.OptiScalerVersion = version; // update the locally assumed latest for other components
                SaveLocalVersions();
                DebugWindow.Log($"[Download] OptiScaler v{version} download and extraction completed successfully");

                return extractPath;
            }
            catch (Exception ex)
            {
                LastError = ex;
                DebugWindow.Log($"[Download] ERROR: {ex.Message}");
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                    DebugWindow.Log($"[Download] Cleaned up cache directory due to error: {extractPath}");
                }
                throw;
            }
        }

        public async Task DownloadAndExtractFakenvapiAsync()
        {
            if (_remoteVersions.FakenvapiVersion == null)
                throw new Exception("No remote version available for Fakenvapi");

            await DownloadAndExtractComponentAsync(
                "Fakenvapi",
                _config.Fakenvapi,
                _remoteVersions.FakenvapiVersion,
                "Fakenvapi"
            );

            _localVersions.FakenvapiVersion = _remoteVersions.FakenvapiVersion;
            SaveLocalVersions();
            OnStatusChanged?.Invoke();
        }

        /// <summary>
        /// NukemFG cannot be downloaded automatically from GitHub.
        /// This method shows the manual file picker dialog so the user can
        /// provide the DLL directly. The DLL is stored in the local cache for
        /// future installs, and the provided version tag is saved to versions.json.
        /// </summary>
        /// <param name="isUpdate">True when the user is updating an existing DLL (vs. first install).</param>
        public async Task<bool> ProvideNukemFGManuallyAsync(bool isUpdate = false)
        {
            var targetVersion = _remoteVersions.NukemFGVersion ?? "manual";

            try
            {
                bool confirmed = false;

                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var dialog = new Views.ManualDownloadDialog("Nukem's DLSSG-to-FSR3 Mod", "dlssg_to_fsr3_amd_is_better.dll", GetNukemFGCachePath(), isUpdate);
                    
                    if (desktop.MainWindow != null)
                    {
                        await dialog.ShowDialog(desktop.MainWindow);
                    }
                    else
                    {
                        dialog.Show();
                        // this isn't strictly awaited properly if there's no mainwindow, but fallback
                    }

                    confirmed = dialog.WasSuccessful;
                }

                if (confirmed)
                {
                    _localVersions.NukemFGVersion = targetVersion;
                    IsNukemFGUpdateAvailable = false;
                    SaveLocalVersions();
                    OnStatusChanged?.Invoke();
                }

                return confirmed;
            }
            catch (Exception ex)
            {
                LastError = ex;
                return false;
            }
        }

        private async Task DownloadAndExtractComponentAsync(
            string componentName,
            RepositoryConfig config,
            string version,
            string cacheSubDir)
        {
            LastError = null;
            try
            {
                // Get release info
                var url = $"https://api.github.com/repos/{config.RepoOwner}/{config.RepoName}/releases/latest";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                // Find download URL
                string? downloadUrl = null;
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.TryGetProperty("browser_download_url", out var urlProp))
                        {
                            var assetUrl = urlProp.GetString();
                            if (assetUrl != null && (assetUrl.EndsWith(".zip") || assetUrl.EndsWith(".7z")))
                            {
                                downloadUrl = assetUrl;
                                break;
                            }
                        }
                    }
                }

                // Fallback to zipball_url if no assets found (e.g., NukemFG)
                if (downloadUrl == null && doc.RootElement.TryGetProperty("zipball_url", out var zipballProp))
                {
                    downloadUrl = zipballProp.GetString();
                }

                if (downloadUrl == null)
                    throw new Exception($"No downloadable asset found for {componentName}. Check if the repository has releases with downloadable files.");

                // Download
                try
                {
                    var zipData = await _httpClient.GetByteArrayAsync(downloadUrl);
                    var tempZip = Path.Combine(Path.GetTempPath(), $"{componentName}_{Guid.NewGuid()}.zip");
                    await File.WriteAllBytesAsync(tempZip, zipData);

                    // Extract
                    var extractPath = Path.Combine(_cacheDir, cacheSubDir);
                    if (Directory.Exists(extractPath))
                        Directory.Delete(extractPath, true);

                    Directory.CreateDirectory(extractPath);

                    using (var archive = ArchiveFactory.Open(tempZip))
                    {
                        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                        {
                            entry.WriteToDirectory(extractPath, new ExtractionOptions
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }

                    File.Delete(tempZip);
                }
                catch (HttpRequestException httpEx)
                {
                    throw new Exception($"Failed to download {componentName}: {httpEx.Message}", httpEx);
                }
                catch (IOException ioEx)
                {
                    throw new Exception($"Failed to extract {componentName}: {ioEx.Message}", ioEx);
                }
            }
            catch (Exception ex)
            {
                LastError = ex;
                throw;
            }
        }

        public string GetOptiScalerCachePath() => Path.Combine(_cacheDir, "OptiScaler", OptiScalerVersion ?? "latest");
        public string GetOptiScalerCachePath(string version) => Path.Combine(_cacheDir, "OptiScaler", version);
        public string GetFakenvapiCachePath() => Path.Combine(_cacheDir, "Fakenvapi");
        /// <summary>Returns the cache directory for NukemFG files.</summary>
        public string GetNukemFGCachePath() => Path.Combine(_cacheDir, "NukemFG");
        public string GetNukemFGDllPath() => Path.Combine(GetNukemFGCachePath(), "dlssg_to_fsr3_amd_is_better.dll");

        public System.Collections.Generic.List<string> GetDownloadedOptiScalerVersions()
        {
            var versions = new System.Collections.Generic.List<string>();
            var cachePath = Path.Combine(_cacheDir, "OptiScaler");
            if (Directory.Exists(cachePath))
            {
                foreach (var dir in Directory.GetDirectories(cachePath))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.Equals("D3D12_Optiscaler", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals("DlssOverrides", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals("Licenses", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (System.Linq.Enumerable.Any(dirName, char.IsDigit) || dirName.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    {
                        versions.Add(dirName);
                    }
                }
            }
            // Better to sort by length and alpha descending:
            versions.Sort((a, b) =>
            {
                var comparison = b.Length.CompareTo(a.Length);
                if (comparison == 0) return string.Compare(b, a, StringComparison.OrdinalIgnoreCase);
                return comparison;
            });
            return versions;
        }

        public void DeleteOptiScalerCache(string version)
        {
            var cachePath = Path.Combine(_cacheDir, "OptiScaler", version);
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, true);
            }
            if (_localVersions.OptiScalerVersion == version)
            {
                _localVersions.OptiScalerVersion = GetDownloadedOptiScalerVersions().FirstOrDefault();
                SaveLocalVersions();
            }
        }

        public string GetVersionString()
        {
            var parts = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrEmpty(OptiScalerVersion))
                parts.Add($"OptiScaler {OptiScalerVersion}");

            if (!string.IsNullOrEmpty(FakenvapiVersion))
                parts.Add($"Fakenvapi {FakenvapiVersion}");

            if (!string.IsNullOrEmpty(NukemFGVersion))
                parts.Add($"NukemFG {NukemFGVersion}");

            return parts.Count > 0 ? string.Join(" | ", parts) : "Not installed";
        }
    }
}
