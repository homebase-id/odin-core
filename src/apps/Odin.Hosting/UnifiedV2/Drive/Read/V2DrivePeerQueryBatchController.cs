using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Peer.Outgoing.Drive.Query;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    /// <summary>
    /// Queries files on a drive hosted by another (peer) identity — the V2 "over peer" equivalent of
    /// <c>POST /api/apps/v1/transit/query/batch</c>. Used by clients that mount a drive owned by a
    /// different identity (e.g. a collaborative community drive) and need to catch up on its files.
    /// </summary>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerByDriveId)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DrivePeerQueryBatchController(PeerDriveQueryService peerDriveQueryService) : OdinControllerBase
    {
        [HttpPost("query-batch")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<QueryBatchResponse> QueryBatch(
            [FromRoute] string odinId,
            [FromRoute] Guid driveId,
            [FromBody] QueryBatchRequestV2 request)
        {
            AssertIsValidOdinId(odinId, out var id);
            OdinValidationUtils.AssertNotNull(request, "request");
            OdinValidationUtils.AssertNotNull(request.QueryParams, "QueryParams");
            OdinValidationUtils.AssertNotNull(request.ResultOptionsRequest, "ResultOptionsRequest");

            var fst = GetHttpFileSystemResolver().GetFileSystemType();

            // PeerDriveQueryService talks to the remote perimeter using the V1 wire shape
            // (FileQueryParamsV1, which carries the TargetDrive). The V2 route supplies the
            // drive by alias; the remote resolves the drive by alias, matching the existing
            // peer "exists" endpoints.
            var v1Request = new QueryBatchRequest
            {
                QueryParams = ToV1QueryParams(request.QueryParams, driveId),
                ResultOptionsRequest = request.ResultOptionsRequest,
                FileSystemType = fst
            };

            var batch = await peerDriveQueryService.GetBatchAsync(id, v1Request, fst, WebOdinContext);
            return QueryBatchResponse.FromResult(batch);
        }

        private static FileQueryParamsV1 ToV1QueryParams(FileQueryParams p, Guid driveId)
        {
            return new FileQueryParamsV1
            {
                TargetDrive = new TargetDrive { Alias = driveId, Type = Guid.Empty },
                FileType = p.FileType,
                FileState = p.FileState,
                DataType = p.DataType,
                ArchivalStatus = p.ArchivalStatus,
                Sender = p.Sender,
                GroupId = p.GroupId,
                UserDate = p.UserDate,
                ClientUniqueIdAtLeastOne = p.ClientUniqueIdAtLeastOne,
                TagsMatchAtLeastOne = p.TagsMatchAtLeastOne,
                TagsMatchAll = p.TagsMatchAll,
                LocalTagsMatchAtLeastOne = p.LocalTagsMatchAtLeastOne,
                LocalTagsMatchAll = p.LocalTagsMatchAll,
                GlobalTransitId = p.GlobalTransitId
            };
        }
    }
}
