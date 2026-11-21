using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Security.Health;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Security;

/// <summary>
/// The Shamir deal and friends' shard requests: V2 of <c>SecurityConfigurationController</c>, same services, owner
/// console only. Several of these services assert only <c>IsOwner</c>, which an app passes, so the policy is what
/// keeps an app out here.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.SecurityRecovery)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2SecurityConfigurationController(
    ShamirConfigurationService shamirConfigurationService,
    ShamirReadinessCheckerService readinessCheckerService,
    ShamirRecoveryService recoveryService,
    OwnerSecurityHealthService securityHealthService) : OdinControllerBase
{
    [HttpGet("config")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<DealerShardConfig> GetConfig()
    {
        return await shamirConfigurationService.GetRedactedConfig(WebOdinContext);
    }

    /// <summary>404 when this host has no automated recovery.</summary>
    [HttpGet("automated-recovery-players")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public IActionResult GetAutomatedRecoveryPlayers()
    {
        var players = shamirConfigurationService.GetAutomatedRecoveryPlayers();
        return players == null ? NotFound() : Ok(players);
    }

    [HttpPost("configure-shards")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> ConfigureShards([FromBody] ConfigureShardsRequest request)
    {
        await shamirConfigurationService.ConfigureShards(request, WebOdinContext);
        return Ok();
    }

    [HttpPost("verify-remote-shards")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<RemoteShardVerificationResult> Verify()
    {
        WebOdinContext.Caller.AssertHasMasterKey();
        return await readinessCheckerService.VerifyRemotePlayerShards(WebOdinContext);
    }

    [HttpPost("verify-remote-player-shard")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<ShardVerificationResult> VerifyRemotePlayer([FromBody] VerifyRemotePlayerShardRequest request)
    {
        return await readinessCheckerService.VerifyRemotePlayerShard(request.OdinId, request.ShardId, WebOdinContext);
    }

    [HttpPost("verify-remote-player-readiness")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<RemotePlayerReadinessResult> VerifyRemotePlayerReadiness([FromBody] VerifyRemotePlayerReadinessRequest request)
    {
        return await readinessCheckerService.VerifyRemotePlayerReadiness(request.OdinId, WebOdinContext);
    }

    [HttpGet("shard-request-list")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<List<ShardApprovalRequest>> GetShardRequestList()
    {
        return await recoveryService.GetShardRequestList(WebOdinContext);
    }

    [HttpPost("approve-shard-request")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> ApproveShardRequest([FromBody] ApproveShardRequest request)
    {
        await recoveryService.ApproveShardRequest(request.ShardId, request.OdinId, request.Index, request.SealedShard, WebOdinContext);
        return Ok();
    }

    [HttpPost("reject-shard-request")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> RejectShardRequest([FromBody] RejectShardRequest request)
    {
        await recoveryService.RejectShardRequest(request.ShardId, request.OdinId, WebOdinContext);
        return Ok();
    }

    [HttpPost("notify-user")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> NotifyUser()
    {
        await securityHealthService.NotifyUser(WebOdinContext);
        return Ok();
    }
}
