using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Services.Authentication.Owner;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

/// <summary>
/// The owner's zero-knowledge keys (docs/zero-knowledge-keys-plan.md).  The browser makes and opens
/// every key; these endpoints only store and return its locked copies.
/// </summary>
[ApiController]
[Route(OwnerApiPathConstants.SecurityV1 + "/zk")]
[AuthorizeValidOwnerToken]
[ApiExplorerSettings(GroupName = "owner-v1")]
public class OwnerZeroKnowledgeController(ZeroKnowledgeKeyService zeroKnowledgeKeyService) : OdinControllerBase
{
    /// <summary>
    /// What the browser needs after login: its locked master key and salt (null until it makes them).
    /// Whether a drive is converted is on the drive (<see cref="OwnerClientDriveData"/>).
    /// </summary>
    [HttpGet]
    public async Task<ZkStatusResponse> GetStatus()
    {
        return await zeroKnowledgeKeyService.GetStatusAsync(WebOdinContext);
    }

    /// <summary>Saves the keys the browser made at the owner's first login.  Refused once they exist.</summary>
    [HttpPost("keys")]
    public async Task<IActionResult> CreateKeys([FromBody] ZkKeysRequest request)
    {
        await zeroKnowledgeKeyService.CreateAsync(request, WebOdinContext);
        return Ok();
    }

    /// <summary>Gives the Shard Recovery drive new keys made in the browser.</summary>
    [HttpPost("convert-shard-recovery-drive")]
    public async Task<IActionResult> ConvertShardRecoveryDrive([FromBody] ZkConvertDriveRequest request)
    {
        await zeroKnowledgeKeyService.ConvertShardRecoveryDriveAsync(request, WebOdinContext);
        return Ok();
    }
}
