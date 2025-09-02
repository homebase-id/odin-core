using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.ShamiraPasswordRecovery;
using Odin.Services.ShamiraPasswordRecovery.ShardRequestApproval;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

[ApiController]
[Route(OwnerApiPathConstants.ShamirRecoveryV1)]
[AuthorizeValidOwnerToken]
public class OwnerShamirConfigurationController(ShamirConfigurationService shamirConfigurationService, ShamirRecoveryService recoveryService)
    : OdinControllerBase
{
    [HttpGet("config")]
    public async Task<DealerShardConfig> GetConfig()
    {
        return await shamirConfigurationService.GetRedactedConfig(WebOdinContext);
    }

    [HttpPost("configure-shards")]
    public async Task<IActionResult> ConfigureShards([FromBody] ConfigureShardsRequest request)
    {
        await shamirConfigurationService.ConfigureShards(request.Players, request.MinMatchingShards, WebOdinContext);
        return Ok();
    }

    [HttpPost("verify-remote-shards")]
    public async Task<RemoteShardVerificationResult> Verify()
    {
        var results = await shamirConfigurationService.VerifyRemotePlayerShards(WebOdinContext);
        return results;
    }

    [HttpPost("verify-remote-player-shard")]
    public async Task<ShardVerificationResult> VerifyRemotePlayer([FromBody] VerifyRemotePlayerShardRequest request)
    {
        var results = await shamirConfigurationService.VerifyRemotePlayer(request.OdinId, request.ShardId, WebOdinContext);
        return results;
    }

    [HttpGet("shard-request-list")]
    public async Task<List<ShardApprovalRequest>> GetShardRequestList()
    {
        return await recoveryService.GetShardRequestList(WebOdinContext);
    }

    [HttpPost("approve-shard-request")]
    public async Task<IActionResult> ApproveShardRequest([FromBody] ApproveShardRequest request)
    {
        await recoveryService.ApproveShardRequest(request.ShardId, WebOdinContext);
        return Ok();
    }

    [HttpPost("reject-shard-request")]
    public async Task<IActionResult> RejectShardRequest([FromBody] RejectShardRequest request)
    {
        await recoveryService.RejectShardRequest(request.ShardId, request.OdinId, WebOdinContext);
        return Ok();
    }
}