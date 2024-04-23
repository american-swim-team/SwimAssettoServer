using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

using SwimHubPlugin;

namespace AssettoServer.Server.UserGroup;

public class SwimUserGroupProvider : IUserGroupProvider
{
    private readonly Dictionary<string, SwimUserGroup> _userGroups = new();

    public SwimUserGroupProvider(SwimHubConfiguration configuration)
    {
        // Reach out to HTTP API to get user group(s)
        foreach ((string name, int role) in configuration.UserGroups)
        {
            _userGroups.Add(name, new SwimUserGroup(name, role));
        }
    }

    public IUserGroup? Resolve(string name)
    {
        // reach out to HTTP API to get user group
        return _userGroups.TryGetValue(name, out var group) ? group : null;
    }
}
