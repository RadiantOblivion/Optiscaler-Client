using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using OptiscalerClient.Models;
using OptiscalerClient.Views;

namespace OptiscalerClient.Services
{
    public class GameInstallationService
    {
        private const string BackupFolderName = "OptiScalerBackup";
        private const string ManifestFileName = "optiscaler_manifest.json";

        // Files that we want to track specifically for backup purposes if they exist in the game folder
        // essentially anything that OptiScaler might replace.
        // We will backup ANYTHING we overwrite, but these are known criticals.
        private readonly string[] _criticalFiles = { "dxgi.dll", "version.dll", "winmm.dll", "nvngx.dll", "nvngx_dlssg.dll", "libxess.dll" };

        public void InstallOptiScaler(Game game, string cachePath, string injectionDllName = "dxgi.dll",
                                     bool installFakenvapi = false, string fakenvapiCachePath = "",
                                     bool installNukemFG = false, string nukemFGCachePath = "",
                                     string? optiscalerVersion = null,
                                     string? overrideGameDir = null)
        {
            DebugWindow.Log($"[Install] Starting OptiScaler installation for game: {game.Name}");
            DebugWindow.Log($"[Install] Version: {optiscalerVersion}, Injection: {injectionDllName}");
            DebugWindow.Log($"[Install] Cache path: {cachePath}");
            
            if (!Directory.Exists(cachePath))
                throw new DirectoryNotFoundException("Updates cache directory not found. Please download OptiScaler first.");

            // Verify cache is not empty
            var cacheFiles = Directory.GetFiles(cachePath, "*.*", SearchOption.AllDirectories);
            if (cacheFiles.Length == 0)
                throw new Exception("Cache directory is empty. Download update again.");

            DebugWindow.Log($"[Install] Cache contains {cacheFiles.Length} files");

            // Determine game directory intelligently (rules for base exe, Phoenix override, or user modal)
            string? gameDir = null;
            
            // Search for an existing manifest first to ensure we update the correctly detected directory
            // from the previous installation. This prevents "splinter" installs on updates.
            if (overrideGameDir == null)
            {
                var searchRoot = !string.IsNullOrEmpty(game.InstallPath) ? game.InstallPath : 
                                 (!string.IsNullOrEmpty(game.ExecutablePath) ? Path.GetDirectoryName(game.ExecutablePath) : null);
                
                if (!string.IsNullOrEmpty(searchRoot) && Directory.Exists(searchRoot))
                {
                    try
                    {
                        var searchOptions = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
                        var manifests = Directory.GetFiles(searchRoot, ManifestFileName, searchOptions);
                        if (manifests.Length > 0)
                        {
                            var existingManifestJson = File.ReadAllText(manifests[0]);
                            var existingManifest = JsonSerializer.Deserialize(existingManifestJson, OptimizerContext.Default.InstallationManifest);
                            if (existingManifest?.InstalledGameDirectory != null && Directory.Exists(existingManifest.InstalledGameDirectory))
                            {
                                gameDir = existingManifest.InstalledGameDirectory;
                                DebugWindow.Log($"[Install] Found existing installation via manifest at: {gameDir}");
                            }
                        }
                    }
                    catch { /* Fall through to auto-detection */ }
                }
            }

            if (overrideGameDir != null)
            {
                gameDir = overrideGameDir;
                DebugWindow.Log($"[Install] Using override game directory: {gameDir}");
            }
            else if (gameDir == null)
            {
                gameDir = DetermineInstallDirectory(game);
                if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                {
                    throw new Exception("Could not automatically detect the game directory. Please use Manual Install.");
                }
                DebugWindow.Log($"[Install] Detected game directory: {gameDir}");
            }

            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                throw new Exception("Installation cancelled or valid directory not found.");

            var backupDir = Path.Combine(gameDir, BackupFolderName);
            DebugWindow.Log($"[Install] Backup directory: {backupDir}");

            // Create backup folder
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
                DebugWindow.Log($"[Install] Created backup directory");
            }

            // Create installation manifest — OptiscalerVersion is the authoritative source for the UI
            var manifest = new InstallationManifest
            {
                InjectionMethod = injectionDllName,
                InstallDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                OptiscalerVersion = optiscalerVersion,
                // Store the EXACT directory used (already resolved for Phoenix/UE5 games).
                // Uninstall will read this directly, avoiding re-detection issues.
                InstalledGameDirectory = gameDir
            };

            // Load existing manifest if present to preserve chain of custody for tracked files
            var manifestPath = Path.Combine(backupDir, ManifestFileName);
            if (File.Exists(manifestPath))
            {
                try
                {
                    var oldManifestJson = File.ReadAllText(manifestPath);
                    var oldManifest = JsonSerializer.Deserialize(oldManifestJson, OptimizerContext.Default.InstallationManifest);
                    if (oldManifest != null)
                    {
                        // Preserve tracking of ALL previously installed files and directories
                        // to ensure clean uninstalls of older versions.
                        foreach (var ifile in oldManifest.InstalledFiles)
                            if (!manifest.InstalledFiles.Contains(ifile)) manifest.InstalledFiles.Add(ifile);

                        foreach (var idir in oldManifest.InstalledDirectories)
                            if (!manifest.InstalledDirectories.Contains(idir)) manifest.InstalledDirectories.Add(idir);
                    }
                }
                catch { /* Ignore corrupt manifest */ }
            }

            // Find the main OptiScaler DLL (OptiScaler.dll or nvngx.dll for older versions)
            string? optiscalerMainDll = null;
            foreach (var file in cacheFiles)
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("nvngx.dll", StringComparison.OrdinalIgnoreCase))
                {
                    optiscalerMainDll = file;
                    DebugWindow.Log($"[Install] Found main OptiScaler DLL: {fileName}");
                    break;
                }
            }

            if (optiscalerMainDll == null)
                throw new Exception("OptiScaler.dll or nvngx.dll not found in the downloaded package. Please re-download OptiScaler.");

            // Step 1: Install the main OptiScaler DLL
            var injectionDllPath = Path.Combine(gameDir, injectionDllName);
            DebugWindow.Log($"[Install] Installing main DLL as: {injectionDllName}");
            
            // Backup and Document!
            DocumentAndBackupFile(injectionDllName, gameDir, backupDir, manifest);

            // Install
            File.Copy(optiscalerMainDll, injectionDllPath, true);
            if (!manifest.InstalledFiles.Contains(injectionDllName)) manifest.InstalledFiles.Add(injectionDllName);

            // Step 2: Copy all other files
            DebugWindow.Log($"[Install] Copying additional files...");
            var additionalFileCount = 0;
            foreach (var sourcePath in cacheFiles)
            {
                var fileName = Path.GetFileName(sourcePath);
                if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("nvngx.dll", StringComparison.OrdinalIgnoreCase)) continue;

                var relativePath = Path.GetRelativePath(cachePath, sourcePath);
                var destPath = Path.Combine(gameDir, relativePath);
                var destDir = Path.GetDirectoryName(destPath);

                if (destDir != null && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                    var relativeDir = Path.GetRelativePath(gameDir, destDir);
                    if (!manifest.InstalledDirectories.Contains(relativeDir)) manifest.InstalledDirectories.Add(relativeDir);
                }

                // Backup and Document!
                DocumentAndBackupFile(relativePath, gameDir, backupDir, manifest);

                // Install
                File.Copy(sourcePath, destPath, true);
                if (!manifest.InstalledFiles.Contains(relativePath)) manifest.InstalledFiles.Add(relativePath);
                additionalFileCount++;
            }

            DebugWindow.Log($"[Install] Copied {additionalFileCount} additional files");

            // Step 3: Install Fakenvapi if requested (AMD/Intel only)
            if (installFakenvapi && !string.IsNullOrEmpty(fakenvapiCachePath) && Directory.Exists(fakenvapiCachePath))
            {
                DebugWindow.Log($"[Install] Installing Fakenvapi...");
                var fakeFiles = Directory.GetFiles(fakenvapiCachePath, "*.*", SearchOption.AllDirectories);
                var fakeFileCount = 0;

                foreach (var sourcePath in fakeFiles)
                {
                    var fileName = Path.GetFileName(sourcePath);

                    // Only copy nvapi64.dll and fakenvapi.ini
                    if (fileName.Equals("nvapi64.dll", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals("fakenvapi.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPath = Path.Combine(gameDir, fileName);

                        // Backup if exists
                        if (File.Exists(destPath))
                        {
                            var backupPath = Path.Combine(backupDir, fileName);
                            if (!File.Exists(backupPath))
                            {
                                File.Copy(destPath, backupPath);
                                manifest.BackedUpFiles.Add(fileName);
                                DebugWindow.Log($"[Install] Backed up existing Fakenvapi file: {fileName}");
                            }
                        }

                        File.Copy(sourcePath, destPath, true);
                        manifest.InstalledFiles.Add(fileName);
                        fakeFileCount++;
                        DebugWindow.Log($"[Install] Installed Fakenvapi file: {fileName}");
                    }
                }
                
                DebugWindow.Log($"[Install] Installed {fakeFileCount} Fakenvapi files");
            }

            // Step 4: Install NukemFG if requested
            if (installNukemFG && !string.IsNullOrEmpty(nukemFGCachePath) && Directory.Exists(nukemFGCachePath))
            {
                DebugWindow.Log($"[Install] Installing NukemFG...");
                var nukemFiles = Directory.GetFiles(nukemFGCachePath, "*.*", SearchOption.AllDirectories);
                var nukemFileCount = 0;

                foreach (var sourcePath in nukemFiles)
                {
                    var fileName = Path.GetFileName(sourcePath);

                    // ONLY copy dlssg_to_fsr3_amd_is_better.dll
                    // DO NOT copy nvngx.dll (200kb) - it will break the mod!
                    if (fileName.Equals("dlssg_to_fsr3_amd_is_better.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        var destPath = Path.Combine(gameDir, fileName);

                        // Backup if exists
                        if (File.Exists(destPath))
                        {
                            var backupPath = Path.Combine(backupDir, fileName);
                            if (!File.Exists(backupPath))
                            {
                                File.Copy(destPath, backupPath);
                                manifest.BackedUpFiles.Add(fileName);
                                DebugWindow.Log($"[Install] Backed up existing NukemFG file: {fileName}");
                            }
                        }

                        File.Copy(sourcePath, destPath, true);
                        manifest.InstalledFiles.Add(fileName);
                        nukemFileCount++;
                        DebugWindow.Log($"[Install] Installed NukemFG file: {fileName}");

                        // Modify OptiScaler.ini to set FGType=nukems
                        ModifyOptiScalerIni(gameDir, "FGType", "nukems");
                        DebugWindow.Log($"[Install] Modified OptiScaler.ini for NukemFG");
                    }
                }
                
                DebugWindow.Log($"[Install] Installed {nukemFileCount} NukemFG files");
            }

            // Save manifest
            manifestPath = Path.Combine(backupDir, ManifestFileName);
            var manifestJson = JsonSerializer.Serialize(manifest, OptimizerContext.Default.InstallationManifest);
            File.WriteAllText(manifestPath, manifestJson);
            DebugWindow.Log($"[Install] Saved installation manifest");

            // Immediately update the game object so the UI reflects the correct state
            // without waiting for the next full scan/analysis cycle.
            game.IsOptiscalerInstalled = true;
            if (!string.IsNullOrEmpty(optiscalerVersion))
                game.OptiscalerVersion = optiscalerVersion;

            // Post-Install: Re-analyze to refresh DLSS/FSR/XeSS fields.
            // AnalyzeGame will also confirm OptiscalerVersion via the manifest.
            DebugWindow.Log($"[Install] Re-analyzing game to update component information...");
            var analyzer = new GameAnalyzerService();
            analyzer.AnalyzeGame(game);
            
            DebugWindow.Log($"[Install] OptiScaler installation completed successfully for {game.Name}");
            DebugWindow.Log($"[Install] Total files installed: {manifest.InstalledFiles.Count}");
            DebugWindow.Log($"[Install] Total files backed up: {manifest.BackedUpFiles.Count}");
        }

        public void UninstallOptiScaler(Game game)
        {
            // ── Determine candidate root directory ───────────────────────────────
            string? rootDir = null;
            if (!string.IsNullOrEmpty(game.ExecutablePath)) rootDir = Path.GetDirectoryName(game.ExecutablePath);
            if (string.IsNullOrEmpty(rootDir) && !string.IsNullOrEmpty(game.InstallPath)) rootDir = game.InstallPath;

            if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
                throw new Exception($"Invalid game directory for '{game.Name}'.");

            DebugWindow.Log($"[Uninstall] Starting deep cleanup for {game.Name} in {rootDir}");

            // ── Search for all manifests recursively ─────────────────────────────
            string[] manifests = Array.Empty<string>();
            try
            {
                var searchOptions = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
                manifests = Directory.GetFiles(rootDir, ManifestFileName, searchOptions);
            }
            catch { /* Ignore search errors */ }

            var processedAnyManifest = false;
            var involvedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            involvedDirs.Add(rootDir);

            if (manifests.Length > 0)
            {
                DebugWindow.Log($"[Uninstall] Found {manifests.Length} installation manifests. processing...");
                foreach (var manifestPath in manifests)
                {
                    try
                    {
                        var manifestJson = File.ReadAllText(manifestPath);
                        var manifest = JsonSerializer.Deserialize(manifestJson, OptimizerContext.Default.InstallationManifest);
                        if (manifest == null) continue;

                        string? currentGameDir = manifest.InstalledGameDirectory;
                        if (string.IsNullOrEmpty(currentGameDir) || !Directory.Exists(currentGameDir))
                            currentGameDir = Path.GetDirectoryName(Path.GetDirectoryName(manifestPath));

                        if (string.IsNullOrEmpty(currentGameDir) || !Directory.Exists(currentGameDir)) continue;
                        
                        involvedDirs.Add(currentGameDir);
                        var backupDir = Path.Combine(currentGameDir, BackupFolderName);

                        // 1. Delete installed files
                        foreach (var file in manifest.InstalledFiles)
                        {
                            try
                            {
                                var p = Path.Combine(currentGameDir, file);
                                if (File.Exists(p)) { File.Delete(p); DebugWindow.Log($"[Uninstall] Deleted: {file}"); }
                            }
                            catch { }
                        }

                        // 2. Restore backups
                        foreach (var file in manifest.BackedUpFiles)
                        {
                            try
                            {
                                var b = Path.Combine(backupDir, file);
                                var d = Path.Combine(currentGameDir, file);
                                if (File.Exists(b)) { File.Copy(b, d, true); DebugWindow.Log($"[Uninstall] Restored: {file}"); }
                            }
                            catch { }
                        }

                        // 3. Remove directories
                        foreach (var dir in manifest.InstalledDirectories.OrderByDescending(d => d.Length))
                        {
                            try
                            {
                                var p = Path.Combine(currentGameDir, dir);
                                if (Directory.Exists(p) && !Directory.EnumerateFileSystemEntries(p).Any()) Directory.Delete(p);
                            }
                            catch { }
                        }

                        // 4. Cleanup backup folder
                        if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);

                        processedAnyManifest = true;
                    }
                    catch (Exception ex) { DebugWindow.Log($"[Uninstall] Error processing manifest {manifestPath}: {ex.Message}"); }
                }
            }

            if (!processedAnyManifest)
            {
                DebugWindow.Log($"[Uninstall] No manifests found. Using legacy folder-scan...");
                var legacyDirs = new List<string> { rootDir };
                var phoenix = DetectCorrectInstallDirectory(rootDir);
                if (!phoenix.Equals(rootDir, StringComparison.OrdinalIgnoreCase)) legacyDirs.Add(phoenix);

                foreach (var dir in legacyDirs)
                {
                    involvedDirs.Add(dir);
                    var bDir = Path.Combine(dir, BackupFolderName);
                    if (Directory.Exists(bDir))
                    {
                        foreach (var bFile in Directory.GetFiles(bDir, "*.*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                var rel = Path.GetRelativePath(bDir, bFile);
                                if (rel.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase)) continue;
                                File.Copy(bFile, Path.Combine(dir, rel), true);
                                DebugWindow.Log($"[Uninstall] Legacy Restored: {rel}");
                            }
                            catch { }
                        }
                        try { Directory.Delete(bDir, true); } catch { }
                    }
                }
            }

            // ── Step 5: Universal Safety Net ─────────────────────────────────────
            var safetyFiles = new[] { 
                "OptiScaler.dll", "OptiScaler.ini", "OptiScaler.log", 
                "dxgi.dll", "version.dll", "winmm.dll", "nvngx.dll",
                "fakenvapi.ini", "nvapi64.dll",
                "dlssg_to_fsr3_amd_is_better.dll", "amd_fidelityfx_upscaler_dx12.dll" 
            };

            foreach (var dir in involvedDirs)
            {
                foreach (var fileName in safetyFiles)
                {
                    try
                    {
                        var p = Path.Combine(dir, fileName);
                        if (File.Exists(p)) 
                        {
                            // Only delete if it's actually an OptiScaler file (config/log) 
                            // OR if no backup was found (it was orphaned)
                            var bPath = Path.Combine(dir, BackupFolderName, fileName);
                            if (fileName.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) || !File.Exists(bPath))
                            {
                                File.Delete(p);
                                DebugWindow.Log($"[Uninstall] SafetyNet cleaned: {fileName} in {Path.GetFileName(dir)}");
                            }
                        }
                    }
                    catch { }
                }
            }

            // ── State Refresh ────────────────────────────────────────────────────
            game.IsOptiscalerInstalled = false;
            game.OptiscalerVersion = null;
            game.Fsr4ExtraVersion = null;

            var analyzer = new GameAnalyzerService();
            analyzer.AnalyzeGame(game);
            
            DebugWindow.Log($"[Uninstall] Completed for {game.Name}. Files restored and UI updated.");
        }

        /// <summary>
        /// Determines the correct installation directory for games based on user rules.
        /// </summary>
        public string? DetermineInstallDirectory(Game game)
        {
            if (string.IsNullOrEmpty(game.InstallPath) || !Directory.Exists(game.InstallPath))
            {
                // If InstallPath is missing, try ExecutablePath
                if (!string.IsNullOrEmpty(game.ExecutablePath) && File.Exists(game.ExecutablePath))
                    return Path.GetDirectoryName(game.ExecutablePath);

                return null;
            }

            // Rule 2: If Phoenix folder is present, ignore step 1 and search inside Phoenix/Binaries/Win64
            var phoenixPath = Path.Combine(game.InstallPath, "Phoenix", "Binaries", "Win64");
            if (Directory.Exists(phoenixPath))
            {
                var phoenixExes = Directory.GetFiles(phoenixPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !Path.HasExtension(f))
                    .ToArray();
                if (phoenixExes.Length > 0)
                {
                    return phoenixPath;
                }
            }

            // Rule 1: Try to extract in the same folder as the main .exe, scan to find it.
            string[] allBinaries = Array.Empty<string>();
            try
            {
                allBinaries = Directory.GetFiles(game.InstallPath, "*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !Path.HasExtension(f))
                    .ToArray();
            }
            catch { }

            string? bestMatchDir = null;

            if (allBinaries.Length > 0)
            {
                // Try to match by name or context
                int bestScore = -1;
                string? bestExe = null;

                var gameNameLetters = new string(game.Name.Where(char.IsLetterOrDigit).ToArray());

                foreach (var exePath in allBinaries)
                {
                    var fileName = Path.GetFileNameWithoutExtension(exePath);

                    // Filter out known non-game executables
                    if (fileName.Contains("Crash", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("Redist", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("Launcher", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("UnrealCEFSubProcess", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("Prerequisites", StringComparison.OrdinalIgnoreCase))
                        continue;

                    int score = 0;
                    var exeLetters = new string(fileName.Where(char.IsLetterOrDigit).ToArray());

                    if (!string.IsNullOrEmpty(exeLetters) && !string.IsNullOrEmpty(gameNameLetters))
                    {
                        if (exeLetters.Contains(gameNameLetters, StringComparison.OrdinalIgnoreCase) ||
                            gameNameLetters.Contains(exeLetters, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 15;
                        }
                    }

                    if (exePath.IndexOf(Path.Combine("Binaries", "Win64"), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 5;
                    }

                    try
                    {
                        // Main game executables are usually decently sized (> 5MB)
                        var fileInfo = new FileInfo(exePath);
                        if (fileInfo.Length > 5 * 1024 * 1024)
                        {
                            score += 10;
                        }
                    }
                    catch { }

                    var exeDir = Path.GetDirectoryName(exePath);
                    if (exeDir != null)
                    {
                        try
                        {
                            var dlls = Directory.GetFiles(exeDir, "*.dll", SearchOption.TopDirectoryOnly);
                            foreach (var dll in dlls)
                            {
                                var dllName = Path.GetFileName(dll).ToLowerInvariant();
                                if (dllName.Contains("amd") || dllName.Contains("fsr") || dllName.Contains("nvngx") || dllName.Contains("dlss") || dllName.Contains("sl.interposer") || dllName.Contains("xess"))
                                {
                                    score += 25; // High confidence if scaling DLLs are nearby
                                    break;
                                }
                            }
                        }
                        catch { }
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestExe = exePath;
                    }
                }

                if (bestExe != null)
                {
                    bestMatchDir = Path.GetDirectoryName(bestExe);
                }

                // Fallback: If no match by name, check known ExecutablePath
                if (bestMatchDir == null)
                {
                    if (!string.IsNullOrEmpty(game.ExecutablePath) && File.Exists(game.ExecutablePath))
                    {
                        bestMatchDir = Path.GetDirectoryName(game.ExecutablePath);
                    }
                    else
                    {
                        var binariesExes = allBinaries.Where(x => x.IndexOf(Path.Combine("Binaries", "Win64"), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                        if (binariesExes.Count == 1)
                        {
                            bestMatchDir = Path.GetDirectoryName(binariesExes[0]);
                        }
                    }
                }
            }
            else if (allBinaries.Length == 0 && !string.IsNullOrEmpty(game.ExecutablePath) && File.Exists(game.ExecutablePath))
            {
                // Fallback if Directory.GetFiles fails but we have an ExecutablePath
                bestMatchDir = Path.GetDirectoryName(game.ExecutablePath);
            }

            if (bestMatchDir != null && Directory.Exists(bestMatchDir))
            {
                return bestMatchDir;
            }

            // Fallback to the main install path, if nothing else works
            return game.InstallPath;
        }


        /// <summary>
        /// Detects the correct installation directory fallback for older uninstalls.
        /// </summary>
        private string DetectCorrectInstallDirectory(string baseDir)
        {
            // Check for UE5 Phoenix structure: Phoenix/Binaries/Win64
            var phoenixPath = Path.Combine(baseDir, "Phoenix", "Binaries", "Win64");
            if (Directory.Exists(phoenixPath))
            {
                return phoenixPath;
            }

            // Check for generic UE structure: GameName/Binaries/Win64
            var binariesPath = Path.Combine(baseDir, "Binaries", "Win64");
            if (Directory.Exists(binariesPath))
            {
                return binariesPath;
            }

            // Return original path if no special structure detected
            return baseDir;
        }

        /// <summary>
        /// Modifies a setting in OptiScaler.ini
        /// </summary>
        private void ModifyOptiScalerIni(string gameDir, string key, string value)
        {
            var iniPath = Path.Combine(gameDir, "OptiScaler.ini");

            if (!File.Exists(iniPath))
            {
                // Create a basic ini file if it doesn't exist
                File.WriteAllText(iniPath, $"[General]\n{key}={value}\n");
                return;
            }

            try
            {
                var lines = File.ReadAllLines(iniPath).ToList();
                bool keyFound = false;
                bool inGeneralSection = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i].Trim();

                    // Check if we're in [General] section
                    if (line.Equals("[General]", StringComparison.OrdinalIgnoreCase))
                    {
                        inGeneralSection = true;
                        continue;
                    }

                    // Check if we've moved to another section
                    if (line.StartsWith("[") && !line.Equals("[General]", StringComparison.OrdinalIgnoreCase))
                    {
                        if (inGeneralSection && !keyFound)
                        {
                            // Insert the key before the next section
                            lines.Insert(i, $"{key}={value}");
                            keyFound = true;
                            break;
                        }
                        inGeneralSection = false;
                    }

                    // If we're in General section and found the key, update it
                    if (inGeneralSection && line.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"{key}={value}";
                        keyFound = true;
                        break;
                    }
                }

                // If key wasn't found, add it to the end of [General] section or create it
                if (!keyFound)
                {
                    if (inGeneralSection)
                    {
                        lines.Add($"{key}={value}");
                    }
                    else
                    {
                        // Add [General] section if it doesn't exist
                        lines.Add("[General]");
                        lines.Add($"{key}={value}");
                    }
                }

                File.WriteAllLines(iniPath, lines);
            }
            catch
            {
                // If modification fails, try to create a new file
                File.WriteAllText(iniPath, $"[General]\n{key}={value}\n");
            }
        }

        /// <summary>
        /// Essential 'Chain of Custody' logic: 
        /// 1. Documents the file in the manifest.
        /// 2. Backs up the original file if it hasn't been backed up yet.
        /// This ensures that every manifest generation (including updates) results in a complete restoration map.
        /// </summary>
        private void DocumentAndBackupFile(string relativePath, string gameDir, string backupDir, InstallationManifest manifest)
        {
            var fullPath = Path.Combine(gameDir, relativePath);
            
            // If the file doesn't exist in the game folder, there's nothing to back up.
            if (!File.Exists(fullPath)) return;

            var backupPath = Path.Combine(backupDir, relativePath);

            // ALWAYS add to manifest BackedUpFiles. 
            // This is the fix for the "Update" bug: even if the backup already exists, 
            // the NEW manifest must still know it needs to be restored from that backup!
            if (!manifest.BackedUpFiles.Contains(relativePath))
            {
                manifest.BackedUpFiles.Add(relativePath);
            }

            // If it's already in the backup folder, we've already secured the "original" version.
            if (File.Exists(backupPath))
            {
                DebugWindow.Log($"[Backup] Already secured: {relativePath}");
                return;
            }

            // Create subdirectories in the backup folder if needed (for nested files)
            var subDir = Path.GetDirectoryName(backupPath);
            if (subDir != null && !Directory.Exists(subDir))
            {
                Directory.CreateDirectory(subDir);
            }

            try
            {
                File.Copy(fullPath, backupPath);
                DebugWindow.Log($"[Backup] Successfully documented and secured original: {relativePath}");
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[Backup] ERROR documenting {relativePath}: {ex.Message}");
            }
        }
    }
}
