using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives.Shares;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Shares;

/// <summary>
/// A file's shares on a Selective Share drive (docs/selective-share-pr3-plan.md, section 3): one per circle, with what
/// its members may do, an expiry and the file key locked with the circle's key, which the server never opens.
/// </summary>
/// <remarks>
/// The owner console, or the owner's app that owns the drive and the circle (<see cref="DriveShareService"/>).
/// </remarks>
[ApiController]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DriveSharesController(DriveShareService shares) : OdinControllerBase
{
    /// <summary>The file's shares, withdrawn ones (no bits) included, without their locked file keys.</summary>
    [HttpGet(UnifiedApiRouteConstants.SharesByFileId)]
    [SwaggerOperation(Tags = [SwaggerInfo.FileShares], Summary = "List a file's shares")]
    public async Task<List<ShareRow>> GetShares([FromRoute] Guid driveId, [FromRoute] Guid fileId)
    {
        return await shares.GetSharesAsync(driveId, fileId, WebOdinContext);
    }

    /// <summary>
    /// Writes up to 100 shares, all or none: new ones, or new bits, expiry and locked file key for existing ones.  A file
    /// shared while Owner becomes Connected; each file's modified moves, its version tag stays.
    /// </summary>
    [HttpPut(UnifiedApiRouteConstants.SharesByDriveId)]
    [SwaggerOperation(Tags = [SwaggerInfo.FileShares], Summary = "Write shares, all or none")]
    public async Task<IActionResult> WriteShares([FromRoute] Guid driveId, [FromBody] WriteSharesRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        await shares.WriteSharesAsync(driveId, request.Shares, WebOdinContext);
        return Ok();
    }

    /// <summary>Withdraws a share: its row stays with no bits and no locked file key, so members' sync sees it go.</summary>
    [HttpDelete(UnifiedApiRouteConstants.SharesByFileId + "/{circleId:guid}")]
    [SwaggerOperation(Tags = [SwaggerInfo.FileShares], Summary = "Withdraw a share")]
    public async Task<IActionResult> WithdrawShare([FromRoute] Guid driveId, [FromRoute] Guid fileId, [FromRoute] Guid circleId)
    {
        await shares.WithdrawShareAsync(driveId, fileId, circleId, WebOdinContext);
        return Ok();
    }
}

public class WriteSharesRequest
{
    public List<ShareRequest> Shares { get; init; }
}
