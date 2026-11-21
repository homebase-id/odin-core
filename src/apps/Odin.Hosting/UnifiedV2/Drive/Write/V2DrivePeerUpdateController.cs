using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

/// <summary>
/// Updates a file on another identity's drive, named by its global transit id: staged on this identity's server and
/// sent there.  A member of a Selective Share circle edits a file shared with them this way, keeping its file key
/// (docs/selective-share-pr3-plan.md, section 7).
/// </summary>
[ApiController]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DrivePeerUpdateController(
    ILogger<V2DrivePeerUpdateController> logger,
    DriveManager driveManager,
    FileSystemResolver fileSystemResolver)
    : V1DriveUploadControllerBase(logger, driveManager, fileSystemResolver)
{
    /// <summary>
    /// Updates the file with global transit id <paramref name="gtid"/> on <paramref name="odinId"/>'s drive
    /// <paramref name="driveId"/>, as multipart form data, as the update by file id takes it.  The route names the
    /// recipient and the file, whatever the instruction set says.
    /// </summary>
    [SwaggerOperation(Summary = "Updates a file on a peer's drive by its global transit id", Tags = [SwaggerInfo.FileWrite])]
    [Consumes("multipart/form-data")]
    [DisableFormValueModelBinding]
    [HttpPatch(UnifiedApiRouteConstants.PeerByGtid)]
    [NoSharedSecretOnRequest]
    [NoSharedSecretOnResponse]
    public async Task<UpdateFileResult> UpdateByGlobalTransitId([FromRoute] string odinId, [FromRoute] Guid driveId,
        [FromRoute] Guid gtid)
    {
        OdinValidationUtils.AssertNotEmptyGuid(driveId, "missing drive id");
        OdinValidationUtils.AssertNotEmptyGuid(gtid, "missing global transit id");
        var result = await ReceivePeerFileUpdateV2(new OdinId(odinId), driveId, gtid);
        return UpdateFileResult.ForPeerFile(result, gtid);
    }
}
