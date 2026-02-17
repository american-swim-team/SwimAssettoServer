using AssettoServer.Server;
using AssettoServer.Server.UserGroup;

namespace SwimGatePlugin;

public class SwimApiUserGroupProvider : IUserGroupProvider
{
    private readonly SwimApiClient _apiClient;
    private readonly SwimGateConfiguration _config;

    public SwimApiUserGroupProvider(SwimApiClient apiClient, SwimGateConfiguration config)
    {
        _apiClient = apiClient;
        _config = config;
    }

    public IUserGroup? Resolve(string name)
    {
        return new SwimApiUserGroup(_apiClient, _config, name);
    }
}

public class SwimApiUserGroup : IUserGroup
{
    private readonly SwimApiClient _apiClient;
    private readonly SwimGateConfiguration _config;
    private readonly string _roleName;

    public SwimApiUserGroup(SwimApiClient apiClient, SwimGateConfiguration config, string roleName)
    {
        _apiClient = apiClient;
        _config = config;
        _roleName = roleName;
    }

    public async Task<bool> ContainsAsync(ulong guid)
    {
        var (success, roles) = await _apiClient.GetRolesAsync(guid);
        if (!success)
            return !_config.FailClosed;
        return roles.Contains(_roleName, StringComparer.OrdinalIgnoreCase);
    }

    public Task<bool> AddAsync(ulong guid)
    {
        throw new NotSupportedException("Roles are managed in Keycloak, not locally.");
    }

    public event EventHandler<IUserGroup, EventArgs>? Changed;
}
