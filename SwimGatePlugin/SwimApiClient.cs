using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace SwimGatePlugin;

public class UserRolesResponse
{
    [JsonPropertyName("keycloak_id")] public string? KeycloakId { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("steam_id")] public long? SteamId { get; set; }
    [JsonPropertyName("discord_id")] public long? DiscordId { get; set; }
    [JsonPropertyName("roles")] public List<string> Roles { get; set; } = [];
}

public class SwimApiClient
{
    private readonly HttpClient _http;
    private readonly SwimGateConfiguration _config;
    private readonly ConcurrentDictionary<ulong, (DateTime Expiry, List<string> Roles)> _cache = new();

    public SwimApiClient(SwimGateConfiguration config)
    {
        _config = config;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);
    }

    public async Task<(bool Success, List<string> Roles)> GetRolesAsync(ulong steamId)
    {
        if (_cache.TryGetValue(steamId, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            return (true, cached.Roles);
        }

        try
        {
            var url = $"{_config.ApiBaseUrl.TrimEnd('/')}/identity/steam/{steamId}/roles";
            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("SwimGate: API returned {StatusCode} for steam ID {SteamId}", response.StatusCode, steamId);
                return (false, []);
            }

            var result = await response.Content.ReadFromJsonAsync<UserRolesResponse>();
            var roles = result?.Roles ?? [];

            _cache[steamId] = (DateTime.UtcNow.AddSeconds(_config.CacheSeconds), roles);
            return (true, roles);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SwimGate: Failed to fetch roles for steam ID {SteamId}", steamId);
            return (false, []);
        }
    }
}
