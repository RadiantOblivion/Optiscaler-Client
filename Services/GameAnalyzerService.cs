// OptiScaler Client - A frontend for managing OptiScaler installations
// Copyright (C) 2026 Agustín Montaña (Agustinm28)
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

using OptiscalerClient.Models;
using System.Diagnostics;
using System.IO;

namespace OptiscalerClient.Services;

public class GameAnalyzerService
{
    private static readonly string[] _dlssNames = new[] { "nvngx_dlss.dll" };
    private static readonly string[] _dlssFrameGenNames = new[] { "nvngx_dlssg.dll" };
    private static readonly string[] _fsrNames = new[] {
        "amd_fidelityfx_dx12.dll",
        "amd_fidelityfx_vk.dll",
        "amd_fidelityfx_upscaler_dx12.dll",
        "amd_fidelityfx_loader_dx12.dll",
        "ffx_fsr2_api_x64.dll",
        "ffx_fsr2_api_dx12_x64.dll",
        "ffx_fsr2_api_vk_x64.dll",
        "ffx_fsr3_api_x64.dll",
        "ffx_fsr3_api_dx12_x64.dll"
    };
    private static readonly string[] _xessNames = new[] { "libxess.dll" };

    public void AnalyzeGame(Game game)
    {
        if (string.IsNullOrEmpty(game.InstallPath) || !Directory.Exists(game.InstallPath))
            return;

        // Reset current versions before analysis
        game.DlssVersion = null;
        game.DlssPath = null;
        game.FsrVersion = null;
        game.FsrPath = null;
        game.XessVersion = null;
        game.XessPath = null;
        game.IsOptiscalerInstalled = false;
        game.OptiscalerVersion = null;

        var discoveredFiles = new DiscoveredFiles();
        
        // Single Pass Discovery
        DiscoverFiles(game.InstallPath, discoveredFiles, 0);

        HashSet<string> ignoredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Detect OptiScaler ──────────────────────────────────────────────────
        try
        {
            // ── Priority 1: manifest ────────────────────────────────────────────
            if (discoveredFiles.Manifests.Count > 0)
            {
                try
                {
                    var manifestJson = File.ReadAllText(discoveredFiles.Manifests[0]);
                    var manifest = System.Text.Json.JsonSerializer.Deserialize<Models.InstallationManifest>(manifestJson);
                    if (manifest != null)
                    {
                        game.IsOptiscalerInstalled = true;
                        if (!string.IsNullOrEmpty(manifest.OptiscalerVersion))
                            game.OptiscalerVersion = manifest.OptiscalerVersion;

                        string originDir = string.IsNullOrEmpty(manifest.InstalledGameDirectory)
                            ? Path.GetDirectoryName(Path.GetDirectoryName(discoveredFiles.Manifests[0]))!
                            : manifest.InstalledGameDirectory;

                        if (!string.IsNullOrEmpty(originDir))
                        {
                            foreach (var relFile in manifest.InstalledFiles)
                            {
                                ignoredFiles.Add(Path.GetFullPath(Path.Combine(originDir, relFile)));
                            }
                        }
                    }
                }
                catch { }
            }

            // ── Priority 2: runtime log ──────────────────────────────────────────
            if (!game.IsOptiscalerInstalled || string.IsNullOrEmpty(game.OptiscalerVersion))
            {
                if (discoveredFiles.Logs.Count > 0)
                {
                    try
                    {
                        foreach (var line in File.ReadLines(discoveredFiles.Logs[0]).Take(10))
                        {
                            if (line.Contains("OptiScaler v", StringComparison.OrdinalIgnoreCase))
                            {
                                var idx = line.IndexOf("OptiScaler v", StringComparison.OrdinalIgnoreCase);
                                if (idx != -1)
                                {
                                    var verPart = line.Substring(idx + 12).Trim();
                                    var endIdx = verPart.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
                                    if (endIdx != -1) verPart = verPart.Substring(0, endIdx);
                                    if (!string.IsNullOrEmpty(verPart))
                                    {
                                        game.IsOptiscalerInstalled = true;
                                        game.OptiscalerVersion = verPart;
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // ── Priority 3: OptiScaler.ini ───────────────────────────────────────
            if (!game.IsOptiscalerInstalled && discoveredFiles.Inis.Count > 0)
            {
                game.IsOptiscalerInstalled = true;
            }
        }
        catch { }

        // ── Process DLLs ───────────────────────────────────────────────────────
        
        // DLSS
        ProcessBestVersion(game, discoveredFiles.Dlls, _dlssNames, ignoredFiles, (g, path, ver) => { g.DlssPath = path; g.DlssVersion = ver; });
        // DLSS Frame Gen
        ProcessBestVersion(game, discoveredFiles.Dlls, _dlssFrameGenNames, ignoredFiles, (g, path, ver) => { g.DlssFrameGenPath = path; g.DlssFrameGenVersion = ver; });
        // FSR
        ProcessBestVersion(game, discoveredFiles.Dlls, _fsrNames, ignoredFiles, (g, path, ver) => { g.FsrPath = path; g.FsrVersion = ver; });
        // XeSS
        ProcessBestVersion(game, discoveredFiles.Dlls, _xessNames, ignoredFiles, (g, path, ver) => { g.XessPath = path; g.XessVersion = ver; });
    }

    private void DiscoverFiles(string path, DiscoveredFiles results, int depth)
    {
        if (depth > 5) return; // Safeguard

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file).ToLower();
                
                if (fileName == "optiscaler_manifest.json") results.Manifests.Add(file);
                else if (fileName == "optiscaler.log") results.Logs.Add(file);
                else if (fileName == "optiscaler.ini") results.Inis.Add(file);
                else if (fileName.EndsWith(".dll"))
                {
                    if (!results.Dlls.ContainsKey(fileName)) results.Dlls[fileName] = new List<string>();
                    results.Dlls[fileName].Add(file);
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                var dirName = Path.GetFileName(dir).ToLower();
                
                // Smart Exclusions: Skip folders that never contain binaries or upscaler DLLs
                if (dirName.StartsWith(".") || dirName == "data" || dirName == "content" || 
                    dirName == "resources" || dirName == "shadercache" || dirName == "_commonredist" ||
                    dirName == "textures" || dirName == "movies" || dirName == "ui")
                    continue;

                DiscoverFiles(dir, results, depth + 1);
            }
        }
        catch { }
    }

    private void ProcessBestVersion(Game game, Dictionary<string, List<string>> discoveredDlls, string[] targetDllNames, HashSet<string> ignoredFiles, Action<Game, string, string> updateAction)
    {
        var highestVer = new Version(0, 0);
        string? bestPath = null;
        string? bestVerStr = null;

        foreach (var dllName in targetDllNames)
        {
            if (discoveredDlls.TryGetValue(dllName.ToLower(), out var files))
            {
                foreach (var file in files)
                {
                    if (ignoredFiles.Contains(Path.GetFullPath(file))) continue;

                    var versionStr = GetFileVersion(file);
                    string parseableVerStr = versionStr;
                    if (parseableVerStr.StartsWith("FSR ", StringComparison.OrdinalIgnoreCase))
                        parseableVerStr = parseableVerStr.Substring(4).Trim();
                    
                    parseableVerStr = parseableVerStr.Split(' ')[0];

                    if (Version.TryParse(parseableVerStr, out var currentVer))
                    {
                        if (currentVer >= highestVer || bestPath == null)
                        {
                            highestVer = currentVer;
                            bestPath = file;
                            bestVerStr = versionStr;
                        }
                    }
                    else if (bestPath == null)
                    {
                        bestPath = file;
                        bestVerStr = "Unknown";
                    }
                }
            }
        }

        if (bestPath != null && bestVerStr != null)
        {
            updateAction(game, bestPath, bestVerStr);
        }
    }

    private class DiscoveredFiles
    {
        public List<string> Manifests { get; } = new();
        public List<string> Logs { get; } = new();
        public List<string> Inis { get; } = new();
        public Dictionary<string, List<string>> Dlls { get; } = new(StringComparer.OrdinalIgnoreCase);
    }


    private string GetFileVersion(string filePath)
    {
        try
        {
            // Cross-platform PE parsing: Native Linux/macOS .NET cannot reliably read Win32 resources from DLLs.
            try
            {
                var peFile = new PeNet.PeFile(filePath);
                var stringTable = peFile.Resources?.VsVersionInfo?.StringFileInfo?.StringTable?.FirstOrDefault();
                
                if (stringTable != null)
                {
                    if (!string.IsNullOrEmpty(stringTable.ProductVersion) && stringTable.ProductVersion != "1.0.0.0" && !stringTable.ProductVersion.StartsWith("1.0."))
                    {
                        return stringTable.ProductVersion.Replace(',', '.').Split(' ')[0];
                    }
                    if (!string.IsNullOrEmpty(stringTable.FileVersion))
                    {
                        return stringTable.FileVersion.Replace(',', '.').Split(' ')[0];
                    }
                }
            }
            catch { /* Fallback to OS native if PeNet traversal fails */ }

            var info = FileVersionInfo.GetVersionInfo(filePath);

            // ProductVersion is usually more accurate for libraries like DLSS (e.g. "3.7.10.0")
            // FileVersion might be "1.0.0.0" wrapper.
            if (!string.IsNullOrEmpty(info.ProductVersion) && info.ProductVersion != "1.0.0.0" && !info.ProductVersion.StartsWith("1.0."))
            {
                return info.ProductVersion.Replace(',', '.').Split(' ')[0];
            }

            if (!string.IsNullOrEmpty(info.FileVersion))
            {
                return info.FileVersion.Replace(',', '.').Split(' ')[0];
            }

            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        }
        catch
        {
            return "0.0.0.0";
        }
    }
}
