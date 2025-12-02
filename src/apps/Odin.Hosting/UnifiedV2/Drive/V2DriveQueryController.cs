using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.UnifiedV2.Drive
{
    [ApiController]
    [Route(UnifiedApiRouteConstants.Query)]
    [UnifiedV2Authorize]
    public class V2DriveQueryController : OdinControllerBase
    {
        [HttpPost("batch")]
        public async Task<QueryBatchResponse> QueryBatch([FromBody] QueryBatchRequest request)
        {
            var fs = GetHttpFileSystemResolver().ResolveFileSystem(request.FileSystemType);
            var driveId = request.QueryParams.TargetDrive.Alias;
            var batch = await fs.Query.GetBatch(driveId, request.QueryParams,
                request.ResultOptionsRequest.ToQueryBatchResultOptions(), WebOdinContext);
            return QueryBatchResponse.FromResult(batch);
        }

        [HttpGet("batch")]
        public async Task<QueryBatchResponse> QueryBatchGet([FromQuery] GetQueryBatchRequest request)
        {
            var queryBatchRequest = request.ToQueryBatchRequest();
            return await QueryBatch(queryBatchRequest);
        }

        [HttpPost("smart-batch")]
        public async Task<QueryBatchResponse> QuerySmartBatch([FromBody] QueryBatchRequest request)
        {
            var driveId = request.QueryParams.TargetDrive.Alias;
            var fs = GetHttpFileSystemResolver().ResolveFileSystem(request.FileSystemType);

            var batch = await fs.Query.GetSmartBatch(driveId,
                request.QueryParams,
                request.ResultOptionsRequest.ToQueryBatchResultOptions(),
                WebOdinContext);

            return QueryBatchResponse.FromResult(batch);
        }

        [HttpGet("smart-batch")]
        public async Task<QueryBatchResponse> QuerySmartBatchGet([FromQuery] GetQueryBatchRequest request)
        {
            var queryBatchRequest = request.ToQueryBatchRequest();
            return await QuerySmartBatch(queryBatchRequest);
        }

        [HttpPost("batch-collection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromBody] QueryBatchCollectionRequest request)
        {
            var fs = GetHttpFileSystemResolver().ResolveFileSystem(request.FileSystemType);
            var collection = await fs.Query.GetBatchCollection(request, WebOdinContext);
            return collection;
        }

        [HttpGet("batch-collection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollectionGet([FromQuery] GetCollectionQueryParamSection[] queries,
            [FromQuery] FileSystemType fileSystemType = FileSystemType.Standard)
        {
            var sections = new List<CollectionQueryParamSection>();
            foreach (var query in queries)
            {
                var section = query.ToCollectionQueryParamSection();
                section.AssertIsValid();
                sections.Add(section);
            }

            var request = new QueryBatchCollectionRequest()
            {
                FileSystemType = fileSystemType,
                Queries = sections
            };

            return await QueryBatchCollection(request);
        }
    }
}