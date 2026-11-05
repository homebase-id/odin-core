using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Write
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.ByFileId)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveUpdateFileController(
        ILogger<V2DriveUpdateFileController> logger,
        DriveManager driveManager,
        FileSystemResolver fileSystemResolver)
        : V1DriveUploadControllerBase(logger, driveManager, fileSystemResolver)
    {
        /// <summary>
        /// Updates a file using multipart form data
        /// </summary>
        /// <returns></returns>
        [SwaggerOperation(
            Summary = "Updates an existing file by the identity's file id",
            Description = "",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [Consumes("multipart/form-data")]
        [DisableFormValueModelBinding]
        [HttpPatch]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        [Route(UnifiedApiRouteConstants.ByFileId)]
        public async Task<UpdateFileResult> UpdateByFileId()
        {
            var driveId = Guid.Parse(RouteData.Values["driveId"]!.ToString()!);
            OdinValidationUtils.AssertNotEmptyGuid(driveId, "missing drive id");
            
            var fileId = Guid.Parse(RouteData.Values["fileId"]!.ToString()!);
            OdinValidationUtils.AssertNotEmptyGuid(fileId, "missing file id");
            
            var v1Result = await ReceiveFileUpdateV2(driveId, fileId);
            return new UpdateFileResult
            {
                FileId = v1Result.File.FileId,
                DriveId = v1Result.File.TargetDrive.Alias.Value,
                GlobalTransitId = v1Result.GlobalTransitId,
                RecipientStatus = v1Result.RecipientStatus,
                NewVersionTag = v1Result.NewVersionTag,
                Updated = v1Result.Updated,
                Payloads = v1Result.Payloads
            };
        }
        
        /// <summary>
        /// Updates a file using multipart form data
        /// </summary>
        /// <returns></returns>
        [SwaggerOperation(
            Summary = "Updates an existing file by the client's unique id (as set by the client which created the file)",
            Description = "",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [Consumes("multipart/form-data")]
        [DisableFormValueModelBinding]
        [HttpPatch]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        [Route(UnifiedApiRouteConstants.ByUniqueId)]
        public async Task<UpdateFileResult> UpdateByUniqueId()
        {
            var driveId = Guid.Parse(RouteData.Values["driveId"]!.ToString()!);
            OdinValidationUtils.AssertNotEmptyGuid(driveId, "missing drive id");
            
            var uniqueId = Guid.Parse(RouteData.Values["uid"]!.ToString()!);
            OdinValidationUtils.AssertNotEmptyGuid(uniqueId, "missing uid");
            
            var v1Result = await ReceiveFileUpdateByUniqueIdV2(driveId, uniqueId);
            return new UpdateFileResult
            {
                FileId = v1Result.File.FileId,
                DriveId = v1Result.File.TargetDrive.Alias.Value,
                GlobalTransitId = v1Result.GlobalTransitId,
                RecipientStatus = v1Result.RecipientStatus,
                NewVersionTag = v1Result.NewVersionTag,
                Updated = v1Result.Updated,
                Payloads = v1Result.Payloads
            };
        }

        /// <summary>
        /// Updates a file on another identity, named by its global transit id: there is no local file. The twin of V1's
        /// <c>PATCH drive/files/update</c> with locale Peer, so the same callers reach it: owner, app or guest. The send
        /// itself needs <c>UseTransitWrite</c> (<c>PermissionContext.AssertCanUsePeer</c>).
        /// </summary>
        /// <remarks>
        /// The path states the recipient, the drive's alias, the file and that this is a peer update, so the
        /// instructions' <c>recipients</c>, <c>locale</c> and <c>file</c> are ignored. The update goes through the outbox,
        /// so RecipientStatus says enqueued, not delivered.
        /// </remarks>
        [SwaggerOperation(
            Summary = "Updates a file on another identity by its global transit id",
            Description = "",
            Tags = [SwaggerInfo.FileWrite]
        )]
        [Consumes("multipart/form-data")]
        [DisableFormValueModelBinding]
        [HttpPatch]
        [NoSharedSecretOnRequest]
        [NoSharedSecretOnResponse]
        [Route(UnifiedApiRouteConstants.PeerByGtid)]
        public async Task<UpdateFileResult> UpdateOverPeerByGtid()
        {
            AssertIsValidOdinId(RouteData.Values["odinId"]?.ToString(), out var recipient);

            var driveId = Guid.Parse(RouteData.Values["driveId"]!.ToString()!);
            OdinValidationUtils.AssertNotEmptyGuid(driveId, "missing drive id");

            var gtid = Guid.Parse(RouteData.Values["gtid"]!.ToString()!);
            OdinValidationUtils.AssertNotEmptyGuid(gtid, "missing global transit id");

            var v1Result = await ReceivePeerFileUpdateV2(recipient, driveId, gtid);
            return new UpdateFileResult
            {
                FileId = Guid.Empty, // the recipient's file id is the recipient's; only the global transit id is shared
                DriveId = driveId,
                GlobalTransitId = gtid, // the writer's own value is one it made up for the transient copy
                RecipientStatus = v1Result.RecipientStatus,
                NewVersionTag = v1Result.NewVersionTag,
                Updated = null,
                Payloads = v1Result.Payloads
            };
        }
    }
}