using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.Security.ZeroKnowledge;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Drive;

/// <summary>
/// The V2 twin of V1's <c>drive/mgmt</c> (<see cref="OwnerDriveManagementController"/>), with its request and response
/// shapes. Owner console only, as V1 is: an app is the owner acting but holds no master key, and must not be able to
/// create, reconfigure, delete or hand itself a drive.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.DriveManagement)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DriveManagementController(
    DriveManager driveManager,
    DriveDeletionService driveDeletionService,
    IAppRegistrationService appRegistrationService,
    ZeroKnowledgeKeyService zeroKnowledgeKeyService) : OdinControllerBase
{
    [HttpPost]
    public async Task<PagedResult<OwnerClientDriveData>> GetDrives([FromBody] GetDrivesRequest request)
    {
        var drives = await driveManager.GetDrivesAsync(new PageOptions(request.PageNumber, request.PageSize), WebOdinContext);
        return new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages,
            drives.Results.Select(OwnerClientDriveData.From).ToList());
    }

    [HttpGet("type")]
    public async Task<PagedResult<OwnerClientDriveData>> GetDrivesByType([FromQuery] GetDrivesByTypeRequest request)
    {
        var drives = await driveManager.GetDrivesAsync(request.DriveType, new PageOptions(request.PageNumber, request.PageSize),
            WebOdinContext);
        return new PagedResult<OwnerClientDriveData>(drives.Request, drives.TotalPages,
            drives.Results.Select(OwnerClientDriveData.From).ToList());
    }

    [HttpPost("create")]
    public async Task<bool> CreateDrive([FromBody] CreateDriveRequest request)
    {
        var _ = request.SharingModel.IsZeroKnowledgeDrive()
            ? await zeroKnowledgeKeyService.CreateZeroKnowledgeDriveAsync(request, WebOdinContext)
            : await driveManager.CreateDriveAsync(request, WebOdinContext);
        return true;
    }

    [HttpPost("update-metadata")]
    public async Task<bool> UpdateDriveMetadata([FromBody] UpdateDriveDefinitionRequest request)
    {
        await driveManager.UpdateMetadataAsync(request.TargetDrive.Alias, request.Metadata, WebOdinContext);
        return true;
    }

    [HttpPost("update-attributes")]
    public async Task<bool> UpdateDriveAttributes([FromBody] UpdateDriveDefinitionRequest request)
    {
        await driveManager.UpdateAttributesAsync(request.TargetDrive.Alias, request.Attributes, WebOdinContext);
        return true;
    }

    [HttpPost("set-drive-read-mode")]
    public async Task<IActionResult> SetDriveReadMode([FromBody] UpdateDriveReadModeRequest request)
    {
        await driveManager.SetDriveReadModeAsync(request.TargetDrive.Alias, request.AllowAnonymousReads, WebOdinContext);
        return Ok();
    }

    [HttpPost("set-allow-subscriptions")]
    public async Task<IActionResult> SetDriveAllowSubscriptions([FromBody] UpdateDriveAllowSubscriptionsRequest request)
    {
        await driveManager.SetDriveAllowSubscriptionsAsync(request.TargetDrive.Alias, request.AllowSubscriptions, WebOdinContext);
        return Ok();
    }

    [HttpPost("set-allow-cdn")]
    public async Task<IActionResult> SetDriveAllowCdn([FromBody] UpdateDriveAllowCdnRequest request)
    {
        await driveManager.SetDriveAllowCdnAsync(request.TargetDrive.Alias, request.AllowCdn, WebOdinContext);
        return Ok();
    }

    [HttpPost("set-archive-drive")]
    public async Task<IActionResult> SetArchiveDriveFlag([FromBody] UpdateDriveArchiveFlag request)
    {
        await driveManager.SetArchiveDriveFlagAsync(request.TargetDrive.Alias, request.Archived, WebOdinContext);
        return Ok();
    }

    /// <summary>Accepted, not done: the files go in the background (V1's <c>empty</c>).</summary>
    [HttpPost("empty")]
    public async Task<IActionResult> EmptyDrive([FromBody] TargetDriveRequest request)
    {
        await driveDeletionService.EmptyDriveAsync(ValidDriveId(request), WebOdinContext);
        return Accepted();
    }

    /// <summary>Accepted, not done: the drive is gone at once, its files go in the background (V1's <c>delete</c>).</summary>
    [HttpPost("delete")]
    public async Task<IActionResult> DeleteDrive([FromBody] TargetDriveRequest request)
    {
        await driveDeletionService.DeleteDriveAsync(ValidDriveId(request), WebOdinContext);
        return Accepted();
    }

    [HttpGet("purges")]
    public async Task<List<DrivePurgeStatus>> GetPurges()
    {
        return await driveDeletionService.GetPurgesAsync(WebOdinContext);
    }

    [HttpPost("purges/retry")]
    public async Task<IActionResult> RetryPurge([FromBody] TargetDriveRequest request)
    {
        await driveDeletionService.RetryPurgeAsync(ValidDriveId(request), WebOdinContext);
        return Accepted();
    }

    /// <summary>The app is looked up here because DriveManager cannot resolve the app registry; see V1's remarks.</summary>
    [HttpPost("set-owner")]
    public async Task<IActionResult> SetDriveOwningApp([FromBody] SetDriveOwningAppRequest request)
    {
        await AssertValidOwningAppRequestAsync(request);
        await driveManager.SetDriveOwningAppAsync(request.TargetDrive.Alias, request.AppId,
            request.DriveSlug, request.DriveTypeSlug, WebOdinContext);
        return Ok();
    }

    [HttpPost("reassign-owner")]
    public async Task<IActionResult> ReassignDriveOwningApp([FromBody] SetDriveOwningAppRequest request)
    {
        await AssertValidOwningAppRequestAsync(request);
        await driveManager.ReassignDriveOwningAppAsync(request.TargetDrive.Alias, request.AppId,
            request.DriveSlug, request.DriveTypeSlug, WebOdinContext);
        return Ok();
    }

    private static Guid ValidDriveId(TargetDriveRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);
        return request.TargetDrive.Alias;
    }

    private async Task AssertValidOwningAppRequestAsync(SetDriveOwningAppRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertIsValidTargetDriveValue(request.TargetDrive);
        OdinValidationUtils.AssertNotEmptyGuid(request.AppId, nameof(request.AppId));

        await appRegistrationService.GetRegisteredAppOrThrowAsync(request.AppId, WebOdinContext);
    }
}
