
using AssettoServer.Server;
using AssettoServer.Server.UserGroup;
using Serilog;

namespace UserGroupPlugin.Services;

public class TestUserGroup : IUserGroup
{
    public event EventHandler<IUserGroup, EventArgs> Changed;

    private List<ulong> Users { get; set; }

    public TestUserGroup()
    {
        Users = new List<ulong>();
    }

    public Task<bool> AddAsync(ulong guid)
    {
        try
        {
            if (Users.Contains(guid))
            {
                return Task.FromResult(false);
            }
            Users.Add(guid);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding user to group");
            return Task.FromResult(false);
        }
    }

    public Task<bool> ContainsAsync(ulong guid)
    {
        try
        {
            return Task.FromResult(Users.Contains(guid));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking if user is in group");
            return Task.FromResult(false);
        }
    }

    public Task<bool> RemoveUserAsync(ulong guid)
    {
        try
        {
            if (Users.Contains(guid))
            {
                Users.Remove(guid);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error removing user from group");
            return Task.FromResult(false);
        }
    }
}
