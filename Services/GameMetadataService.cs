using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace OptiscalerClient.Services;

public class GameMetadataService
{
    private readonly HttpClient _httpClient;
    private readonly string _coversCachePath;

    public GameMetadataService()
    {
        _httpClient = new HttpClient();
        
        // Caching covers in AppData
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _coversCachePath = Path.Combine(appData, "OptiscalerClient", "Covers");
        
        if (!Directory.Exists(_coversCachePath))
        {
            Directory.CreateDirectory(_coversCachePath);
        }
    }

    /// <summary>
    /// Searches the Steam Store API for the game by name and returns the local file path for its poster image.
    /// If it's already downloaded, it returns the cached path.
    /// </summary>
    public async Task<string?> FetchAndCacheCoverImageAsync(string gameName, string appIdKey)
    {
        // Check if we already have it in cache by appId/guid
        string cacheFileName = $"{appIdKey}.jpg".Replace(":", "_");
        string localPath = Path.Combine(_coversCachePath, cacheFileName);

        if (File.Exists(localPath))
        {
            return localPath;
        }

        try
        {
            // Simple sanitization to improve search results
            string queryName = Uri.EscapeDataString(gameName);
            string url = $"https://store.steampowered.com/api/storesearch/?term={queryName}&l=english&cc=US";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (root.TryGetProperty("total", out var totalEl) && totalEl.GetInt32() > 0)
            {
                if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    // Get the first matching game's AppID
                    var firstItem = items[0];
                    if (firstItem.TryGetProperty("id", out var idEl))
                    {
                        int actualAppId = idEl.GetInt32();
                        string remoteUrl = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{actualAppId}/library_600x900_2x.jpg";
                        
                        // Download it locally
                        var imgBytes = await _httpClient.GetByteArrayAsync(remoteUrl);
                        await File.WriteAllBytesAsync(localPath, imgBytes);
                        
                        return localPath;
                    }
                }
            }
        }
        catch
        {
            // Ignore network or parsing errors and just return null (no cover art)
        }

        return null;
    }

    public async Task<string?> FetchCoverImageUrlAsync(string gameName)
    {
        // Legacy method if still used elsewhere
        try
        {
            string queryName = Uri.EscapeDataString(gameName);
            string url = $"https://store.steampowered.com/api/storesearch/?term={queryName}&l=english&cc=US";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (root.TryGetProperty("total", out var totalEl) && totalEl.GetInt32() > 0)
            {
                if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    var firstItem = items[0];
                    if (firstItem.TryGetProperty("id", out var idEl))
                    {
                        int appId = idEl.GetInt32();
                        return $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg";
                    }
                }
            }
        }
        catch { }
        return null;
    }
}
