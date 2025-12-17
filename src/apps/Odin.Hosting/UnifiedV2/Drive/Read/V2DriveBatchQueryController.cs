using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.DrivesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveBatchQueryController(PeerOutgoingTransferService peerOutgoingTransferService, DriveManager driveManager) :
        V2DriveControllerBase(peerOutgoingTransferService)
    {
        [HttpPost("query-batch-collection")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromBody] QueryBatchCollectionRequestV2 request)
        {
            var v1Queries = new List<CollectionQueryParamSection>();

            foreach (var section in request.Queries)
            {
                section.AssertIsValid();
                var theDrive = await driveManager.GetDriveAsync(section.QueryParams.DriveId);
                var qp = section.QueryParams;
                var newSection = new CollectionQueryParamSection
                {
                    Name = section.Name,
                    QueryParams = new FileQueryParamsV1
                    {
                        FileType = qp.FileType,
                        FileState = qp.FileState,
                        DataType = qp.DataType,
                        ArchivalStatus = qp.ArchivalStatus,
                        Sender = qp.Sender,
                        GroupId = qp.GroupId,
                        UserDate = qp.UserDate,
                        ClientUniqueIdAtLeastOne = qp.ClientUniqueIdAtLeastOne,
                        TagsMatchAtLeastOne = qp.TagsMatchAtLeastOne,
                        TagsMatchAll = qp.TagsMatchAll,
                        LocalTagsMatchAtLeastOne = qp.LocalTagsMatchAtLeastOne,
                        LocalTagsMatchAll = qp.LocalTagsMatchAll,
                        GlobalTransitId = qp.GlobalTransitId,
                        TargetDrive = theDrive!.TargetDriveInfo
                    },
                    ResultOptionsRequest = section.ResultOptionsRequest
                };

                v1Queries.Add(newSection);
            }

            var fs = GetHttpFileSystemResolver().ResolveFileSystem();
            var collection = await fs.Query.GetBatchCollection(v1Queries, WebOdinContext);
            return collection;
        }
    }

    public class QueryBatchCollectionRequestV2
    {
        public List<CollectionQueryParamSectionV2> Queries { get; init; }
    }

    public class CollectionQueryParamSectionV2
    {
        public string Name { get; set; }

        public FileQueryParamsV2 QueryParams { get; set; }

        public QueryBatchResultOptionsRequest ResultOptionsRequest { get; set; }

        public void AssertIsValid()
        {
            OdinValidationUtils.AssertNotNullOrEmpty(this.Name, nameof(this.Name));
            QueryParams.AssertIsValid();
        }
    }
}