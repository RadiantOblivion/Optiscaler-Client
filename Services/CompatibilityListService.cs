using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OptiscalerClient.Models;

namespace OptiscalerClient.Services
{
    public class CompatibilityListService
    {
        private const string WikiUrl = "https://raw.githubusercontent.com/wiki/optiscaler/OptiScaler/Compatibility-List.md";
        private readonly string _cacheFile;
        private readonly HttpClient _httpClient;
        private CompatibilityListCache _cache = new();
        private HashSet<string> _normalizedNames = new();

        public DateTime LastUpdated => _cache.LastUpdated;
        public int GameCount => _cache.Games.Count;

        public CompatibilityListService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _cacheFile = Path.Combine(appData, "OptiscalerClient", "compatibility_list.json");
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OptiscalerClient");
            
            LoadFromCache();
        }

        private void LoadFromCache()
        {
            if (File.Exists(_cacheFile))
            {
                try
                {
                    var json = File.ReadAllText(_cacheFile);
                    _cache = JsonSerializer.Deserialize(json, OptimizerContext.Default.CompatibilityListCache) ?? new();
                    UpdateNormalizedNames();
                }
                catch { }
            }
        }

        private void UpdateNormalizedNames()
        {
            _normalizedNames = new HashSet<string>(_cache.Games.Select(NormalizeName).Where(n => !string.IsNullOrEmpty(n)), StringComparer.OrdinalIgnoreCase);
        }

        private string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            // Lowercase, remove punctuation and special characters, unify Roman numerals/digits if possible (simple version for now)
            var normalized = name.ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"[^\w\s]", ""); // Remove non-word/non-space
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        public async Task RefreshAsync()
        {
            try
            {
                var markdown = await _httpClient.GetStringAsync(WikiUrl);
                var games = ParseMarkdown(markdown);
                
                if (games.Count > 0)
                {
                    _cache.Games = games;
                    _cache.LastUpdated = DateTime.Now;
                    
                    var json = JsonSerializer.Serialize(_cache, OptimizerContext.Default.CompatibilityListCache);
                    File.WriteAllText(_cacheFile, json);
                    
                    UpdateNormalizedNames();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CompatibilityService] Refresh error: {ex.Message}");
            }
        }

        private List<string> ParseMarkdown(string markdown)
        {
            var games = new List<string>();
            // Matches [Game Name](Link)
            var matches = Regex.Matches(markdown, @"\[([^\]]+)\]\(([^)]+)\)");
            foreach (Match match in matches)
            {
                var name = match.Groups[1].Value.Trim();
                // Basic validation: Avoid template or short garbage
                if (!string.IsNullOrEmpty(name) && name != "Template" && name.Length > 1)
                {
                    games.Add(name);
                }
            }
            return games.Distinct().ToList();
        }

        public bool IsCompatible(string gameName)
        {
            if (string.IsNullOrEmpty(gameName)) return false;
            
            // 1. Exact match
            if (_cache.Games.Any(g => g.Equals(gameName, StringComparison.OrdinalIgnoreCase))) return true;

            // 2. Normalized match
            var normalizedInput = NormalizeName(gameName);
            if (string.IsNullOrEmpty(normalizedInput)) return false;
            
            if (_normalizedNames.Contains(normalizedInput)) return true;

            // 3. Substring match for leniency
            foreach (var compName in _normalizedNames)
            {
                if (compName.Length < 4 || normalizedInput.Length < 4) continue;

                if (normalizedInput.Contains(compName) || compName.Contains(normalizedInput))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
