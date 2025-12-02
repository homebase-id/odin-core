using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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
            var driveId = request.QueryParams.TargetDrive.Alias;
            var batch = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetBatch(driveId, request.QueryParams,
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
            var batch = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetSmartBatch(driveId, request.QueryParams,
                request.ResultOptionsRequest.ToQueryBatchResultOptions(), WebOdinContext);
            return QueryBatchResponse.FromResult(batch);
        }

        [HttpGet("smart-batch")]
        public async Task<QueryBatchResponse> QuerySmartBatchGet([FromQuery] GetQueryBatchRequest request)
        {
            var queryBatchRequest = request.ToQueryBatchRequest();

            return await QuerySmartBatch(queryBatchRequest);
        }

        [HttpPost("batchcollection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromBody] QueryBatchCollectionRequest request)
        {
            var collection = await GetHttpFileSystemResolver().ResolveFileSystem().Query.GetBatchCollection(request, WebOdinContext);
            return collection;
        }

        [HttpGet("batchcollection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromQuery] GetCollectionQueryParamSection[] queries)
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
                Queries = sections
            };

            return await QueryBatchCollection(request);
        }
    }
}