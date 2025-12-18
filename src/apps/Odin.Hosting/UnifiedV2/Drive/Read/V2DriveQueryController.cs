using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.FilesRoot)]
    [UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2DriveQueryController : OdinControllerBase
    {
        [HttpPost("query-batch")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<QueryBatchResponse> QueryBatch([FromRoute] Guid driveId, [FromBody] QueryBatchRequestV2 request)
        {
            var fs = GetHttpFileSystemResolver().ResolveFileSystem();
            var v2 = request.QueryParams;
            
            var qpv1 = new FileQueryParamsV1
            {
                TargetDrive = null,
                FileType = null,
                FileState = null,
                DataType = null,
                ArchivalStatus = null,
                Sender = null,
                GroupId = null,
                UserDate = null,
                ClientUniqueIdAtLeastOne = v2.ClientUniqueIdAtLeastOne,
                TagsMatchAtLeastOne = v2.TagsMatchAtLeastOne,
                TagsMatchAll = v2.TagsMatchAll,
                LocalTagsMatchAtLeastOne = v2.LocalTagsMatchAtLeastOne,
                LocalTagsMatchAll = v2.LocalTagsMatchAll,
                GlobalTransitId = v2.GlobalTransitId
            };
            
            var batch = await fs.Query.GetBatch(driveId, 
                qpv1,
                request.ResultOptionsRequest.ToQueryBatchResultOptions(), 
                WebOdinContext);
            
            return QueryBatchResponse.FromResult(batch);
        }

        [HttpPost("query-smart-batch")]
        [SwaggerOperation(Tags = [SwaggerInfo.FileQuery])]
        public async Task<QueryBatchResponse> QuerySmartBatch([FromRoute] Guid driveId, [FromBody] QueryBatchRequestV2 request)
        {
            var fs = GetHttpFileSystemResolver().ResolveFileSystem();

            var batch = await fs.Query.GetSmartBatch(driveId,
                request.QueryParams,
                request.ResultOptionsRequest.ToQueryBatchResultOptions(),
                WebOdinContext);

            return QueryBatchResponse.FromResult(batch);
        }
    }
}