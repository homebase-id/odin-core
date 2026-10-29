using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Drives.Management;
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
    OwnerAuthenticationService ownerAuthenticationService,
    IAppRegistrationService appRegistrationService,
    ZkDriveConversionService driveConversionService) : OdinControllerBase
{
    /// <summary>
    /// What the browser needs after login: its locked master key and salt (null until it makes them),
    /// where the recovery phrase is, its public key and whether the password copy exists.  Whether a drive is converted is on the
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
    public async Task<IActionResult> TakeOverPhrase([FromBody] ZkPhraseTakeoverRequest request)
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

    /// <summary>
    /// Every app's zero-knowledge keys: its key and the drive keys locked with it, for the consent page and the login
    /// backfill (docs/zero-knowledge-drive-api-plan.md, step 3).
    /// </summary>
    [HttpGet("app-keys")]
    public async Task<List<ZkAppKeysResponse>> GetAppKeys()
    {
        return await appRegistrationService.GetZkAppKeysAsync(WebOdinContext);
    }

    /// <summary>What the browser locked for an app: its key if it had none, and copies of drive keys.</summary>
    [HttpPost("app-keys")]
    public async Task<IActionResult> SaveAppKeys([FromBody] ZkAppKeysRequest request)
    {
        await appRegistrationService.SaveZkAppKeysAsync(request, WebOdinContext);
        return Ok();
    }

    /// <summary>The key of a drive about to be converted, for the browser to re-lock: the server's last look at it.</summary>
    [HttpGet("drives/{driveId:guid}/key-for-conversion")]
    public async Task<ZkDriveKeyResponse> GetDriveKeyForConversion(Guid driveId)
    {
        return new ZkDriveKeyResponse { StorageKey64 = await driveConversionService.GetDriveKeyForConversionAsync(driveId, WebOdinContext) };
    }

    /// <summary>Converts a drive to zero knowledge while it keeps its key.</summary>
    [HttpPost("convert-drive")]
    public async Task<IActionResult> ConvertDrive([FromBody] ZkConvertDriveKeepingKeyRequest request)
    {
        await driveConversionService.ConvertDriveKeepingKeyAsync(request, WebOdinContext);
        return Ok();
    }

    /// <summary>
    /// The built-in Private or SelectiveShare drives the identity doesn't have yet, for the browser to create at a
    /// password login with its own keys (POST drive/mgmt/create, with each request's model and the keys).
    /// </summary>
    [HttpGet("missing-drives")]
    public async Task<List<CreateDriveRequest>> GetMissingDrives()
    {
        return await zeroKnowledgeKeyService.GetMissingZeroKnowledgeDrivesAsync(WebOdinContext);
    }

    /// <summary>Gives the Shard Recovery drive new keys made in the browser.</summary>
    [HttpPost("convert-shard-recovery-drive")]
    public async Task<IActionResult> ConvertShardRecoveryDrive([FromBody] ZkConvertDriveRequest request)
    {
        await zeroKnowledgeKeyService.ConvertShardRecoveryDriveAsync(request, WebOdinContext);
        return Ok();
    }
}
