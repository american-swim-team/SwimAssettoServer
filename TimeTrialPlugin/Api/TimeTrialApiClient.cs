using System.Net.Http.Json;
using System.Text.Json;
using Serilog;
using TimeTrialPlugin.Configuration;
using TimeTrialPlugin.Timing;

namespace TimeTrialPlugin.Api;

public class TimeTrialApiClient
{
    private readonly HttpClient _http;
    private readonly TimeTrialConfiguration _config;
    private readonly JsonSerializerOptions _jsonOptions;

    public TimeTrialApiClient(HttpClient httpClient, TimeTrialConfiguration configuration)
    {
        _http = httpClient;
        _config = configuration;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        if (!string.IsNullOrEmpty(_config.ApiUrl))
        {
            _http.BaseAddress = new Uri(_config.ApiUrl.TrimEnd('/') + "/");
        }

        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            _http.DefaultRequestHeaders.Add("X-API-Key", _config.ApiKey);
        }
    }

    public async Task<SubmitResult?> SubmitLapTimeAsync(LapTime lapTime)
    {
        if (!_config.IsApiConfigured)
        {
            Log.Warning("API not configured, skipping lap time submission");
            return null;
        }

        try
        {
            var request = new SubmitTimeTrialRequest
            {
                Steamid = (long)lapTime.PlayerGuid,
                TrackId = lapTime.TrackId,
                PlayerName = lapTime.PlayerName,
                CarModel = lapTime.CarModel,
                TotalTimeMs = lapTime.TotalTimeMs,
                SectorTimesMs = lapTime.SectorTimesMs
            };

            var response = await _http.PostAsJsonAsync("timetrial/submit", request, _jsonOptions);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error("Failed to submit lap time to API: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<SubmitResult>(_jsonOptions);
            Log.Information("Lap time submitted to API: IsPersonalBest={IsPersonalBest}, Position={Position}",
                result?.IsPersonalBest, result?.LeaderboardPosition);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while submitting lap time to API");
            return null;
        }
    }

    public async Task<List<LapTime>> GetLeaderboardAsync(string trackId, int limit = 10)
    {
        if (string.IsNullOrEmpty(_config.ApiUrl))
        {
            return [];
        }

        try
        {
            var response = await _http.GetAsync($"timetrial/leaderboard?track_id={Uri.EscapeDataString(trackId)}&limit={limit}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error("Failed to get leaderboard from API: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);
                return [];
            }

            var entries = await response.Content.ReadFromJsonAsync<List<LeaderboardEntryDto>>(_jsonOptions);
            if (entries == null) return [];

            return entries.Select(e => new LapTime
            {
                TrackId = trackId,
                PlayerName = e.PlayerName,
                PlayerGuid = (ulong)e.Steamid,
                CarModel = e.CarModel,
                TotalTimeMs = e.TotalTimeMs,
                SectorTimesMs = e.SectorTimesMs,
                RecordedAt = DateTime.Parse(e.RecordedAt)
            }).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while getting leaderboard from API");
            return [];
        }
    }

    public async Task<LapTime?> GetPersonalBestAsync(string trackId, ulong steamId)
    {
        if (string.IsNullOrEmpty(_config.ApiUrl))
        {
            return null;
        }

        try
        {
            var response = await _http.GetAsync($"timetrial/personal_best?steamid={steamId}&track_id={Uri.EscapeDataString(trackId)}");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error("Failed to get personal best from API: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content) || content == "null")
            {
                return null;
            }

            var entry = JsonSerializer.Deserialize<LeaderboardEntryDto>(content, _jsonOptions);
            if (entry == null) return null;

            return new LapTime
            {
                TrackId = trackId,
                PlayerName = entry.PlayerName,
                PlayerGuid = (ulong)entry.Steamid,
                CarModel = entry.CarModel,
                TotalTimeMs = entry.TotalTimeMs,
                SectorTimesMs = entry.SectorTimesMs,
                RecordedAt = DateTime.Parse(entry.RecordedAt)
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while getting personal best from API");
            return null;
        }
    }
}

public record SubmitTimeTrialRequest
{
    public long Steamid { get; init; }
    public required string TrackId { get; init; }
    public required string PlayerName { get; init; }
    public required string CarModel { get; init; }
    public long TotalTimeMs { get; init; }
    public required long[] SectorTimesMs { get; init; }
}

public record SubmitResult
{
    public bool IsPersonalBest { get; init; }
    public int? LeaderboardPosition { get; init; }
}

public record LeaderboardEntryDto
{
    public long Steamid { get; init; }
    public required string PlayerName { get; init; }
    public required string CarModel { get; init; }
    public long TotalTimeMs { get; init; }
    public required long[] SectorTimesMs { get; init; }
    public required string RecordedAt { get; init; }
}
