using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text;
using AssettoServer.Commands;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.OpenSlotFilters;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Shared.Network.Packets.Outgoing.Handshake;
using Serilog;
using Microsoft.Extensions.Logging;
using DotNext.Collections.Generic;

namespace SwimWhitelistPlugin;

public class SwimWhitelistFilter : OpenSlotFilterBase
{
    private readonly EntryCarManager _entryCarManager;
    private readonly SwimWhitelistConfiguration _config;
    private readonly HttpClient _http = new HttpClient();

    public SwimWhitelistFilter(SwimWhitelistConfiguration configuration, EntryCarManager entryCarManager)
    {
        _config = configuration;
        _entryCarManager = entryCarManager;
    }

    public override Task<AuthFailedResponse?> ShouldAcceptConnectionAsync(ACTcpClient client, HandshakeRequest request)
    {
        var rolesToCheck = new List<long>();
        var EntryCars = _entryCarManager.EntryCars;

        if ( _config.ReservedSlotsRoles != null) // Check if theres enough slots available
        {
            int totalSlots = 0;
            int inUse = 0;
            for (int i = 0; i < EntryCars.Length; i++)
            {
                if (EntryCars[i].AiMode != AiMode.Fixed)
                {
                    totalSlots++;
                    if (EntryCars[i].Client != null)
                    {
                        inUse++;
                    }
                }
            }
            Log.Information("Slots {inUse} / {totalSlots}", inUse, totalSlots);
            if (totalSlots - inUse <= _config.ReservedSlots)
            {
                rolesToCheck = _config.ReservedSlotsRoles;
            }
        }
        if (_config.ReservedCars.Any(x => x.Model == request.RequestedCar)) // Check if the slot is reserved
        {
            int totalSlots = 0;
            int inUse = 0;
            for (int i = 0; i < EntryCars.Length; i++)
            {
                if (EntryCars[i].Model == request.RequestedCar)
                {
                    totalSlots++;
                    if (EntryCars[i].Client != null)
                    {
                        inUse++;
                    }
                }   
            }
            Log.Information("Requested car {Car} has {TotalSlots} slots, {InUse} in use", request.RequestedCar, totalSlots, inUse);
            if (totalSlots - inUse <= _config.ReservedCars.First(x => x.Model == request.RequestedCar).Amount)
            {
                rolesToCheck = _config.ReservedCars.First(x => x.Model == request.RequestedCar).Roles;
            }
        }
        if (rolesToCheck.Count == 0) // If no roles are set, accept the connection
        {
            return base.ShouldAcceptConnectionAsync(client, request);
        }

        // Check roles using the API endpoint
        var payload = new 
        {
            roles = rolesToCheck,
            steamid = request.Guid
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        // Make the request, deserialize the JSON
        var response = _http.PostAsync(_config.EndpointUrl, content).Result;
        var responseContent = response.Content.ReadAsStringAsync().Result;
        var responseJson = JsonSerializer.Deserialize<Dictionary<string, string>>(responseContent);

        if (responseJson == null)
        {
            Log.Error("Failed to deserialize JSON response from {EndpointUrl}", _config.EndpointUrl);
            return Task.FromResult<AuthFailedResponse?>(new AuthFailedResponse("Failed to deserialize JSON response from the API endpoint."));
        }

        // check if responseJSON status is UNAUTHORIZED
        if (responseJson["status"] == "UNAUTHORIZED")
        {
            Log.Information("User {SteamId} is not authorized", request.Guid);
            return Task.FromResult<AuthFailedResponse?>(new AuthFailedResponse("This slot is whitelisted, make sure you have the appropriate roles on discord."));
        }

        Log.Information("User {SteamId} is authorized / slot doesn't require authorization", request.Guid);
        return base.ShouldAcceptConnectionAsync(client, request);
    }
}
