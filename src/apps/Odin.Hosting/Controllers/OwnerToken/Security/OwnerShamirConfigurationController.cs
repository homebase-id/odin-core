using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.ShamiraPasswordRecovery;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

[ApiController]
[Route(OwnerApiPathConstants.ShamirRecoveryV1)]
[AuthorizeValidOwnerToken]
public class OwnerShamirConfigurationController(ShamirConfigurationService shamirConfigurationService)
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
    public async Task<ShardVerificationResult> VerifyRemotePlayer([FromBody] VerifyRemotePlayerShardRequest request )
    {
        var results = await shamirConfigurationService.VerifyRemotePlayer(request.OdinId, request.ShardId, WebOdinContext);
        return results;
    }
}