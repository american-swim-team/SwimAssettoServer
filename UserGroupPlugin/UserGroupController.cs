using AssettoServer.Server.UserGroup;
using DotNext;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Threading.Tasks;

namespace UserGroupPlugin;


[ApiController]
[Route("usergroups")]
public class UserGroupController : ControllerBase
{
    private readonly UserGroupManager _userGroupManager;

    public UserGroupController(UserGroupManager userGroupManager)
    {
        _userGroupManager = userGroupManager;
    }

    [HttpGet("CheckUserInGroup/{group}/{guid}")]
    public async Task<IActionResult> CheckUserInGroup(string group, ulong guid)
    {
        try
        {
            var groupObject = _userGroupManager.Resolve(group);
            if (groupObject is null)
            {
                return NotFound(new { Message = "Group not found" });
            }

            if (await groupObject.ContainsAsync(guid) == false)
            {
                return Ok(new { IsInGroup = false });
            }

            return Ok(new { IsInGroup = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking user in group");
            return StatusCode(500, new { Message = $"error: {ex.Message}" });
        }
    }

    [HttpPost("AddUserToGroup/{group}/{guid}")]
    public async Task<IActionResult> AddUserToGroup([FromRoute] string group, [FromRoute] ulong guid)
    {
        var groupObject = _userGroupManager.Resolve(group);
        if (groupObject is null)
        {
            return NotFound("group not found");
        }

        bool added = await groupObject.AddAsync(guid);

        return Ok(new { Added = added });
    }
}
