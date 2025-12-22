using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Drives;
using Odin.Services.Util;
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
            OdinValidationUtils.AssertNotNull(request, "request");
            OdinValidationUtils.AssertNotNull(request.QueryParams, "QueryParams");
            OdinValidationUtils.AssertNotNull(request.ResultOptionsRequest, "ResultOptionsRequest");
            
            var fs = GetHttpFileSystemResolver().ResolveFileSystem();
            
            var batch = await fs.Query.GetBatch(driveId, 
                request.QueryParams,
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