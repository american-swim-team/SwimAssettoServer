using System.Text.Json;
using Serilog;
using TimeTrialPlugin.Api;
using TimeTrialPlugin.Configuration;

namespace TimeTrialPlugin.Timing;

public class LeaderboardManager
{
    private readonly TimeTrialConfiguration _configuration;
    private readonly TimeTrialApiClient _apiClient;
    private readonly Dictionary<string, Dictionary<ulong, LapTime>> _personalBests = new();
    private readonly object _lock = new();

    public LeaderboardManager(TimeTrialConfiguration configuration, TimeTrialApiClient apiClient)
    {
        _configuration = configuration;
        _apiClient = apiClient;

        foreach (var track in configuration.Tracks)
        {
            _personalBests[track.Id] = new Dictionary<ulong, LapTime>();
        }

        if (configuration.PersistBestTimes)
        {
            LoadFromFile();
        }

        // Load personal bests from API asynchronously if configured
        if (configuration.IsApiConfigured)
        {
            _ = LoadFromApiAsync();
        }
    }

    private async Task LoadFromApiAsync()
    {
        try
        {
            foreach (var track in _configuration.Tracks)
            {
                var times = await _apiClient.GetLeaderboardAsync(track.Id, 100);
                if (times.Count > 0)
                {
                    lock (_lock)
                    {
                        foreach (var lapTime in times)
                        {
                            _personalBests[track.Id][lapTime.PlayerGuid] = lapTime;
                        }
                    }
                    Log.Information("Loaded {Count} personal bests from API for track {TrackId}",
                        times.Count, track.Id);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load personal bests from API");
        }
    }

    public LapTime? GetPersonalBest(string trackId, ulong playerGuid)
    {
        lock (_lock)
        {
            if (_personalBests.TryGetValue(trackId, out var trackBests) &&
                trackBests.TryGetValue(playerGuid, out var pb))
            {
                return pb;
            }
            return null;
        }
    }

    public bool RecordLapTime(LapTime lapTime)
    {
        lock (_lock)
        {
            if (!_personalBests.TryGetValue(lapTime.TrackId, out var personalBests))
            {
                return false;
            }

            // Check if personal best
            var isPersonalBest = false;
            if (!personalBests.TryGetValue(lapTime.PlayerGuid, out var existingPb) ||
                lapTime.TotalTimeMs < existingPb.TotalTimeMs)
            {
                personalBests[lapTime.PlayerGuid] = lapTime;
                isPersonalBest = true;
            }

            if (isPersonalBest)
            {
                if (_configuration.PersistBestTimes)
                {
                    SaveToFile();
                }

                // Submit to API asynchronously if configured
                Log.Debug("API configured check: SubmitToApi={SubmitToApi}, ApiUrl={ApiUrl}, ApiKey={HasKey}, IsApiConfigured={IsConfigured}",
                    _configuration.SubmitToApi, _configuration.ApiUrl, !string.IsNullOrEmpty(_configuration.ApiKey), _configuration.IsApiConfigured);

                if (_configuration.IsApiConfigured)
                {
                    Log.Information("Submitting lap time to API for {PlayerName} on {TrackId}", lapTime.PlayerName, lapTime.TrackId);
                    _ = SubmitToApiAsync(lapTime);
                }
            }

            return isPersonalBest;
        }
    }

    private async Task SubmitToApiAsync(LapTime lapTime)
    {
        try
        {
            await _apiClient.SubmitLapTimeAsync(lapTime);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to submit lap time to API for player {PlayerName}", lapTime.PlayerName);
        }
    }

    private void LoadFromFile()
    {
        try
        {
            if (!File.Exists(_configuration.BestTimesFilePath))
            {
                Log.Information("No existing best times file found at {Path}", _configuration.BestTimesFilePath);
                return;
            }

            var json = File.ReadAllText(_configuration.BestTimesFilePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, List<LapTime>>>(json);
            if (data == null) return;

            lock (_lock)
            {
                foreach (var (trackId, times) in data)
                {
                    if (!_personalBests.ContainsKey(trackId)) continue;

                    foreach (var time in times)
                    {
                        if (!_personalBests[trackId].ContainsKey(time.PlayerGuid) ||
                            time.TotalTimeMs < _personalBests[trackId][time.PlayerGuid].TotalTimeMs)
                        {
                            _personalBests[trackId][time.PlayerGuid] = time;
                        }
                    }
                }
            }

            Log.Information("Loaded best times from {Path}", _configuration.BestTimesFilePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load best times from {Path}", _configuration.BestTimesFilePath);
        }
    }

    private void SaveToFile()
    {
        try
        {
            // Convert personal bests to lists for serialization
            var data = _personalBests.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Values.ToList()
            );
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_configuration.BestTimesFilePath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save best times to {Path}", _configuration.BestTimesFilePath);
        }
    }
}
