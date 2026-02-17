using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.OpenSlotFilters;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Shared.Network.Packets.Outgoing.Handshake;
using Serilog;

namespace SwimGatePlugin;

public class SwimGateFilter : OpenSlotFilterBase
{
    private readonly SwimGateConfiguration _config;
    private readonly EntryCarManager _entryCarManager;
    private readonly SwimApiClient _apiClient;

    public SwimGateFilter(SwimGateConfiguration config, EntryCarManager entryCarManager, SwimApiClient apiClient)
    {
        _config = config;
        _entryCarManager = entryCarManager;
        _apiClient = apiClient;
    }

    public override async Task<AuthFailedResponse?> ShouldAcceptConnectionAsync(ACTcpClient client, HandshakeRequest request)
    {
        var (success, roles) = await _apiClient.GetRolesAsync(request.Guid);

        if (!success)
        {
            if (_config.FailClosed)
            {
                Log.Warning("SwimGate: API unreachable, rejecting {SteamId} (FailClosed=true)", request.Guid);
                return new AuthFailedResponse("Role check unavailable. Please try again later.");
            }

            Log.Information("SwimGate: API unreachable, allowing {SteamId} (FailClosed=false)", request.Guid);
            return await base.ShouldAcceptConnectionAsync(client, request);
        }

        var hasAllowedRole = roles.Any(r => _config.AllowedRoles.Contains(r, StringComparer.OrdinalIgnoreCase));

        if (_config.ReservedSlots == 0)
        {
            // Lock mode: only allowed roles can join
            if (!hasAllowedRole)
            {
                Log.Information("SwimGate: Rejected {SteamId} in lock mode (no matching role)", request.Guid);
                return new AuthFailedResponse(_config.RejectMessage);
            }
        }
        else
        {
            // Reserve mode: last N slots reserved for allowed roles
            var entryCars = _entryCarManager.EntryCars;
            int availableSlots = 0;
            for (int i = 0; i < entryCars.Length; i++)
            {
                if (entryCars[i].AiMode != AiMode.Fixed && entryCars[i].Client == null)
                {
                    availableSlots++;
                }
            }

            if (availableSlots <= _config.ReservedSlots && !hasAllowedRole)
            {
                Log.Information("SwimGate: Rejected {SteamId} in reserve mode ({AvailableSlots} slots left, {ReservedSlots} reserved)", request.Guid, availableSlots, _config.ReservedSlots);
                return new AuthFailedResponse(_config.RejectMessage);
            }
        }

        return await base.ShouldAcceptConnectionAsync(client, request);
    }
}
