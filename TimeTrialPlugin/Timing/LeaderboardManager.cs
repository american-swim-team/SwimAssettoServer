using System.Text.Json;
using Serilog;
using TimeTrialPlugin.Configuration;

namespace TimeTrialPlugin.Timing;

public class LeaderboardManager
{
    private readonly TimeTrialConfiguration _configuration;
    private readonly Dictionary<string, List<LapTime>> _leaderboards = new();
    private readonly Dictionary<string, Dictionary<ulong, LapTime>> _personalBests = new();
    private readonly object _lock = new();

    public LeaderboardManager(TimeTrialConfiguration configuration)
    {
        _configuration = configuration;
        foreach (var track in configuration.Tracks)
        {
            _leaderboards[track.Id] = [];
            _personalBests[track.Id] = new Dictionary<ulong, LapTime>();
        }

        if (configuration.PersistBestTimes)
        {
            LoadFromFile();
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

    public IReadOnlyList<LapTime> GetLeaderboard(string trackId, int? limit = null)
    {
        lock (_lock)
        {
            if (!_leaderboards.TryGetValue(trackId, out var leaderboard))
            {
                return [];
            }
            var count = limit ?? _configuration.LeaderboardSize;
            return leaderboard.Take(count).ToList();
        }
    }

    public (bool IsPersonalBest, int? LeaderboardPosition) RecordLapTime(LapTime lapTime)
    {
        lock (_lock)
        {
            if (!_leaderboards.TryGetValue(lapTime.TrackId, out var leaderboard) ||
                !_personalBests.TryGetValue(lapTime.TrackId, out var personalBests))
            {
                return (false, null);
            }

            // Check if personal best
            var isPersonalBest = false;
            if (!personalBests.TryGetValue(lapTime.PlayerGuid, out var existingPb) ||
                lapTime.TotalTimeMs < existingPb.TotalTimeMs)
            {
                personalBests[lapTime.PlayerGuid] = lapTime;
                isPersonalBest = true;
            }

            // Update leaderboard (one entry per player, best time only)
            var existingEntry = leaderboard.FindIndex(lt => lt.PlayerGuid == lapTime.PlayerGuid);
            if (existingEntry >= 0)
            {
                if (lapTime.TotalTimeMs < leaderboard[existingEntry].TotalTimeMs)
                {
                    leaderboard.RemoveAt(existingEntry);
                }
                else
                {
                    // Not better than existing leaderboard entry
                    return (isPersonalBest, null);
                }
            }

            // Insert in sorted position
            var insertPosition = leaderboard.FindIndex(lt => lapTime.TotalTimeMs < lt.TotalTimeMs);
            if (insertPosition < 0)
            {
                leaderboard.Add(lapTime);
                insertPosition = leaderboard.Count - 1;
            }
            else
            {
                leaderboard.Insert(insertPosition, lapTime);
            }

            // Trim leaderboard to configured size
            while (leaderboard.Count > _configuration.LeaderboardSize)
            {
                leaderboard.RemoveAt(leaderboard.Count - 1);
            }

            var position = insertPosition < _configuration.LeaderboardSize ? insertPosition + 1 : (int?)null;

            if (_configuration.PersistBestTimes)
            {
                SaveToFile();
            }

            return (isPersonalBest, position);
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
                    if (!_leaderboards.ContainsKey(trackId)) continue;

                    var sortedTimes = times.OrderBy(t => t.TotalTimeMs).ToList();
                    _leaderboards[trackId] = sortedTimes.Take(_configuration.LeaderboardSize).ToList();

                    foreach (var time in sortedTimes)
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
            var json = JsonSerializer.Serialize(_leaderboards, new JsonSerializerOptions
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
