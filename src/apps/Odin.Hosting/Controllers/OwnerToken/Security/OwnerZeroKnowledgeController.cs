using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Services.Authentication.Owner;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

/// <summary>
/// The owner's zero-knowledge keys (docs/zero-knowledge-keys-plan.md) and recovery in the browser
/// (docs/zero-knowledge-recovery-plan.md).  The browser makes and opens every key; these endpoints
/// only store and return its locked copies.
/// </summary>
[ApiController]
[Route(OwnerApiPathConstants.SecurityV1 + "/zk")]
[AuthorizeValidOwnerToken]
[ApiExplorerSettings(GroupName = "owner-v1")]
public class OwnerZeroKnowledgeController(
    ZeroKnowledgeKeyService zeroKnowledgeKeyService,
    ZeroKnowledgeRecoveryService recoveryService,
    OwnerAuthenticationService ownerAuthenticationService) : OdinControllerBase
{
    /// <summary>
    /// What the browser needs after login: its locked master key and salt (null until it makes them),
    /// the password copy, and where the recovery phrase is.  Whether a drive is converted is on the
    /// drive (<see cref="OwnerClientDriveData"/>).
    /// </summary>
    [HttpGet]
    public async Task<ZkStatusResponse> GetStatus()
    {
        return await recoveryService.GetStatusAsync(WebOdinContext);
    }

    /// <summary>Saves the keys the browser made at the owner's first login.  Refused once they exist.</summary>
    [HttpPost("keys")]
    public async Task<IActionResult> CreateKeys([FromBody] ZkKeysRequest request)
    {
        await zeroKnowledgeKeyService.CreateAsync(request, WebOdinContext);
        return Ok();
    }

    /// <summary>Writes the password copy; only with proof of the password.</summary>
    [HttpPost("password")]
    public async Task<IActionResult> SavePassword([FromBody] ZkPasswordRequest request)
    {
        await recoveryService.SavePasswordAsync(request, WebOdinContext);
        return Ok();
    }

    /// <summary>The recovery phrase, once, for the takeover; only with proof of the password.</summary>
    [HttpPost("phrase-handover")]
    public async Task<ZkPhraseHandoverResponse> HandOverPhrase([FromBody] ZkPhraseHandoverRequest request)
    {
        return await recoveryService.HandOverAsync(request, WebOdinContext);
    }

    /// <summary>The browser's phrase copies; the server then forgets its own.</summary>
    [HttpPost("phrase-takeover")]
    public async Task<IActionResult> TakeOverPhrase([FromBody] ZkPhraseCopiesRequest request)
    {
        await recoveryService.TakeOverAsync(request, WebOdinContext);
        return Ok();
    }

    /// <summary>The phrase typed in again after a reset forgot its copies.</summary>
    [HttpPost("phrase-reentry")]
    public async Task<IActionResult> ReenterPhrase([FromBody] ZkPhraseCopiesRequest request)
    {
        await recoveryService.ReenterAsync(request, WebOdinContext);
        return Ok();
    }

    /// <summary>The session's half of the browser's stored zero-knowledge master key.</summary>
    [HttpGet("session-key")]
    public async Task<ZkSessionKeyResponse> GetSessionKey()
    {
        var key = await ownerAuthenticationService.GetZkSessionKeyAsync(WebOdinContext);
        return new ZkSessionKeyResponse { SessionKey64 = key.ToBase64() };
    }

    /// <summary>Gives the Shard Recovery drive new keys made in the browser.</summary>
    [HttpPost("convert-shard-recovery-drive")]
    public async Task<IActionResult> ConvertShardRecoveryDrive([FromBody] ZkConvertDriveRequest request)
    {
        await zeroKnowledgeKeyService.ConvertShardRecoveryDriveAsync(request, WebOdinContext);
        return Ok();
    }
}
