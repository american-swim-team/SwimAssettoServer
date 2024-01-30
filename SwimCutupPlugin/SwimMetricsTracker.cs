using AssettoServer.Network.Tcp;
using System.Text;
using System.Text.Json;
using Serilog;
using Qmmands.Delegates;

namespace SwimCutupPlugin;

public class SwimMetricsTracker : IDisposable
{

    // stuff
    private readonly SwimCutupPlugin _plugin;
    public ACTcpClient _client;

    // metrics
    public long _startTime;
    private int _count = 0;
    public string _car = "";
    
    private double _avgSpeed;
    private double _topSpeed;
    private double _totalSpeed;

    private int _totalCollisions = 0;
    private long _lastCollisionTime = 0;
    private double _totalDistance = 0;
    private long _currentHighscore = 0;

    public SwimMetricsTracker(SwimCutupPlugin plugin, ACTcpClient client, String trackName)
    {
        _plugin = plugin;
        _client = client;
        _car = client.EntryCar.Model;
        _startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var highscore_request = new StringContent(JsonSerializer.Serialize(new { steamid = _client.Guid, track = _plugin._trackName, car = _car }), Encoding.UTF8, "application/json");
        var highscore_response = _plugin.http.PostAsync(_plugin.config.Server + "/fetch_cutup_score", highscore_request).Result.Content.ReadAsStringAsync().Result;
        var highscore =  JsonSerializer.Deserialize<Dictionary<string, long>>(highscore_response);
        if (_currentHighscore == 0) {
            _currentHighscore = highscore["data"];
        }

        Log.Information("SwimCutupPlugin: SwimMetricsTracker: {client}, {trackName}, {car}, {highscore}", _client.Guid, trackName, _car, _currentHighscore); 
    }

    public void OnCollision(long score) { // send score to API and reset
        if (_lastCollisionTime > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 3000 || score > 9999999) { // 6 second grace period after collision
            _lastCollisionTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return;
        }
        Log.Information("SwimCutupPlugin: OnCollision: {score}", score);
        _totalCollisions++; // increment collision counter
        _lastCollisionTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); // set last collision time
        if (score < _currentHighscore) { // if score is lower than current highscore, don't send it to API
            return;
        }
        _currentHighscore = score; // set new highscore

        var data = new StringContent(JsonSerializer.Serialize(new {
            steamid = _client.Guid,
            track = _plugin._trackName,
            car = _car,
            score = score,
            }), Encoding.UTF8, "application/json"); 

        Log.Information("SwimCutupPlugin: current highscore: {current_highscore}, payload: {payload}, data {data}", _currentHighscore, score, data.ReadAsStringAsync().Result);

        var new_highscore_response = _plugin.http.PostAsync(_plugin.config.Server + "/insert_cutup_score", data).Result.Content.ReadAsStringAsync().Result;
        var status =  JsonSerializer.Deserialize<Dictionary<string, string>>(new_highscore_response);
        if (status["status"] == "ERROR") {
            Log.Information("SwimCutupPlugin: Error updating highscore: {status}", status["message"]);
        }

        _client.SendPacket(new Packets.SwimCutupMsg { MsgType = 1, Payload = _currentHighscore });
    }

    public void OnHighscoreRequest() { // fetch highscore and send it to player
        _client.SendPacket(new Packets.SwimCutupMsg { MsgType = 1, Payload = _currentHighscore });
    }

    public void UpdateDriverStats(System.Numerics.Vector3 _speed) {
        double speed = _speed.Length() * 3.6f;
        if (speed < 20) {
            return;
        }
        if (speed > _topSpeed) {
            _topSpeed = speed;
        }
        double distance = speed / 3600;
        _totalDistance += distance;
        _totalSpeed += speed;
        _count += 1;
        _avgSpeed = _totalSpeed / _count;
    }

    public void Dispose()
    {
        // send stats to API
        var data = new StringContent(JsonSerializer.Serialize(new {
            steamid = _client.Guid,
            track = _plugin._trackName,
            time = (int) Math.Floor((double) (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _startTime) / 1000), // time in seconds since start of session
            avgspeed = (int) Math.Round(_avgSpeed, 0),
            collisions = _totalCollisions,
            distance = (float) Math.Round(_totalDistance, 2),
            }), Encoding.UTF8, "application/json");

        Log.Information("SwimCutupPlugin: Dispose: {data}", data.ReadAsStringAsync().Result);

        _plugin.http.PostAsync(_plugin.config.Server + "/update_driver_stats", data);
        GC.SuppressFinalize(this);
    }
}