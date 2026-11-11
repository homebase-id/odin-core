using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Drives.Management;
using Odin.Services.Security.ZeroKnowledge;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Security;

/// <summary>
/// The owner's zero-knowledge keys and recovery in the browser: V2 of <c>OwnerZeroKnowledgeController</c>, same services,
/// owner console only. Every request and response body goes through the shared secret, as on V1.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.SecurityZk)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2ZeroKnowledgeController(
    ZeroKnowledgeKeyService zeroKnowledgeKeyService,
    ZeroKnowledgeRecoveryService recoveryService,
    OwnerAuthenticationService ownerAuthenticationService,
    IAppRegistrationService appRegistrationService,
    ZkDriveConversionService driveConversionService) : OdinControllerBase
{
    [HttpGet]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<ZkStatusResponse> GetStatus()
    {
        return await recoveryService.GetStatusAsync(WebOdinContext);
    }

    [HttpPost("keys")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> CreateKeys([FromBody] ZkKeysRequest request)
    {
        await zeroKnowledgeKeyService.CreateAsync(request, WebOdinContext);
        return Ok();
    }

    [HttpPost("password")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> SavePassword([FromBody] ZkPasswordRequest request)
    {
        await recoveryService.SavePasswordAsync(request, WebOdinContext);
        return Ok();
    }

    [HttpPost("phrase-handover")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<ZkPhraseHandoverResponse> HandOverPhrase([FromBody] ZkPhraseHandoverRequest request)
    {
        return await recoveryService.HandOverAsync(request, WebOdinContext);
    }

    [HttpPost("phrase-takeover")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> TakeOverPhrase([FromBody] ZkPhraseTakeoverRequest request)
    {
        await recoveryService.TakeOverAsync(request, WebOdinContext);
        return Ok();
    }

    [HttpPost("phrase-reentry")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> ReenterPhrase([FromBody] ZkPhraseCopiesRequest request)
    {
        await recoveryService.ReenterAsync(request, WebOdinContext);
        return Ok();
    }

    [HttpGet("session-key")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<ZkSessionKeyResponse> GetSessionKey()
    {
        var key = await ownerAuthenticationService.GetZkSessionKeyAsync(WebOdinContext);
        return new ZkSessionKeyResponse { SessionKey64 = key.ToBase64() };
    }

    [HttpGet("app-keys")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<List<ZkAppKeysResponse>> GetAppKeys()
    {
        return await appRegistrationService.GetZkAppKeysAsync(WebOdinContext);
    }

    [HttpPost("app-keys")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> SaveAppKeys([FromBody] ZkAppKeysRequest request)
    {
        await appRegistrationService.SaveZkAppKeysAsync(request, WebOdinContext);
        return Ok();
    }

    [HttpGet("drives/{driveId:guid}/key-for-conversion")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<ZkDriveKeyResponse> GetDriveKeyForConversion(Guid driveId)
    {
        return new ZkDriveKeyResponse { StorageKey64 = await driveConversionService.GetDriveKeyForConversionAsync(driveId, WebOdinContext) };
    }

    [HttpPost("convert-drive")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> ConvertDrive([FromBody] ZkConvertDriveKeepingKeyRequest request)
    {
        await driveConversionService.ConvertDriveKeepingKeyAsync(request, WebOdinContext);
        return Ok();
    }

    [HttpGet("missing-drives")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<List<CreateDriveRequest>> GetMissingDrives()
    {
        return await zeroKnowledgeKeyService.GetMissingZeroKnowledgeDrivesAsync(WebOdinContext);
    }

    [HttpPost("convert-shard-recovery-drive")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> ConvertShardRecoveryDrive([FromBody] ZkConvertDriveRequest request)
    {
        await zeroKnowledgeKeyService.ConvertShardRecoveryDriveAsync(request, WebOdinContext);
        return Ok();
    }
}
