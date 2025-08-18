using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.ShamiraPasswordRecovery;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

/// <summary>
/// Security information for the current user
/// </summary>
[ApiController]
[Route(OwnerApiPathConstants.ShamirRecoveryV1)]
[AuthorizeValidOwnerToken]
public class OwnerShamirRecoveryController(ShamiraRecoveryService recoveryService, ShamiraVerificationService verificationService)
    : OdinControllerBase
{
    [HttpPost("create")]
    public async Task<IActionResult> CreateShards([FromBody] CreateShardRequest request)
    {
        await recoveryService.UpdateShards(request.Players,
            request.TotalShards, request.MinMatchingShards, WebOdinContext);

        return Ok();
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify()
    {
        var results = await verificationService.VerifyRemotePlayerShards(WebOdinContext);
        return Ok(results);
    }
}