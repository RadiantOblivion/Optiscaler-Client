using OptiscalerClient.Models;
using OptiscalerClient.Views;
using System.IO;
using System.Runtime.Versioning;

namespace OptiscalerClient.Services;

public class GameScannerService
{
    private readonly IGameScanner _steamScanner;
    private readonly IGameScanner _epicScanner;
    private readonly IGameScanner _gogScanner;
    private readonly IGameScanner _xboxScanner;
    private readonly IGameScanner _eaScanner;
    private readonly IGameScanner _battleNetScanner;
    private readonly IGameScanner _ubisoftScanner;
    private readonly ExclusionService _exclusions;

    public GameScannerService()
    {
        _steamScanner = new SteamScanner();
        _epicScanner = new EpicScanner();
        _gogScanner = new GogScanner();
        _xboxScanner = new XboxScanner();
        _eaScanner = new EaScanner();
        _battleNetScanner = new BattleNetScanner();
        _ubisoftScanner = new UbisoftScanner();

        // config.json lives next to the executable (copied by the build)
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        _exclusions = new ExclusionService(configPath);
    }

    public async Task<List<Game>> ScanAllGamesAsync(ScanSourcesConfig? scanConfig = null)
    {
        var games = new List<Game>();
        var analyzer = new GameAnalyzerService();
        DebugWindow.Log("[Scanner] Executing HIGH-PERFORMANCE concurrent game scan...");

        if (scanConfig == null) scanConfig = new ScanSourcesConfig();

        // 1. Concurrent Platform Scans
        var scanTasks = new List<Task<List<Game>>>();

        if (scanConfig.ScanSteam)   scanTasks.Add(Task.Run(() => _steamScanner.Scan()));
        if (scanConfig.ScanEpic)    scanTasks.Add(Task.Run(() => _epicScanner.Scan()));
        if (scanConfig.ScanGOG)     scanTasks.Add(Task.Run(() => _gogScanner.Scan()));
        if (scanConfig.ScanXbox)    scanTasks.Add(Task.Run(() => _xboxScanner.Scan()));
        if (scanConfig.ScanEA)      scanTasks.Add(Task.Run(() => _eaScanner.Scan()));
        if (scanConfig.ScanUbisoft) scanTasks.Add(Task.Run(() => _ubisoftScanner.Scan()));
        scanTasks.Add(Task.Run(() => _battleNetScanner.Scan())); // Always scan Battle.net

        // 2. Custom Folders (Concurrent)
        if (scanConfig.CustomFolders != null)
        {
            foreach (var folder in scanConfig.CustomFolders)
                scanTasks.Add(Task.Run(() => ScanCustomFolder(folder)));
        }

        // Wait for all raw scanners to finish
        var results = await Task.WhenAll(scanTasks);
        var rawGames = results.SelectMany(r => r).ToList();
        DebugWindow.Log($"[Scanner] Raw discovery finished. Found {rawGames.Count} candidate games. Starting parallel analysis...");

        // 3. Concurrent Game Analysis (I/O Bound)
        // We use a semaphore to limit concurrency to avoid disk thrashing (max 4 concurrent tasks)
        using (var semaphore = new SemaphoreSlim(4))
        {
            var analysisTasks = rawGames.Select(async game =>
            {
                await semaphore.WaitAsync();
                try
                {
                    if (_exclusions.IsExcluded(game)) return;
                    analyzer.AnalyzeGame(game);
                    lock (games) { games.Add(game); }
                }
                catch (Exception ex) { DebugWindow.Log($"[Scanner] Analysis error for {game.Name}: {ex.Message}"); }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(analysisTasks);
        }

        DebugWindow.Log($"[Scanner] Scan completed. Found {games.Count} valid games.");
        return games.OrderBy(g => g.Platform).ThenBy(g => g.Name).ToList();
    }

    private List<Game> ScanCustomFolder(string rootFolder)
    {
        var games = new List<Game>();
        if (!Directory.Exists(rootFolder)) return games;

        try
        {
            // Use EnumerateDirectories for better performance on large disks
            foreach (var gameFolder in Directory.EnumerateDirectories(rootFolder))
            {
                try
                {
                    // Find .exe files with EnumerateFiles
                    foreach (var exePath in Directory.EnumerateFiles(gameFolder, "*.exe", SearchOption.AllDirectories))
                    {
                        var exeName = Path.GetFileNameWithoutExtension(exePath).ToLower();
                        if (exeName.Contains("unins") || exeName.Contains("setup") || 
                            exeName.Contains("installer") || exeName.Contains("crash") ||
                            exeName.Contains("launcher") && !exeName.Contains("game"))
                        {
                            continue;
                        }

                        games.Add(new Game
                        {
                            Name = Path.GetFileName(gameFolder),
                            ExecutablePath = exePath,
                            InstallPath = gameFolder,
                            Platform = GamePlatform.Custom,
                            AppId = "Custom_" + Path.GetFileName(gameFolder)
                        });
                        break; // Only one exe per folder
                    }
                }
                catch { }
            }
        }
        catch (Exception ex) { DebugWindow.Log($"[Scanner] Custom folder error: {ex.Message}"); }
        return games;
    }
}
