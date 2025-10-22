using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DriveQueryV1)]
    [Route(GuestApiPathConstants.DriveQueryV1)]
    [AuthorizeValidGuestOrAppToken]
    public class DriveQueryController : DriveQueryControllerBase
    {
        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpPost("batch")]
        public new async Task<QueryBatchResponse> QueryBatch([FromBody] QueryBatchRequest request)
        {
            return await base.QueryBatch(request);
        }

        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpGet("batch")]
        public async Task<QueryBatchResponse> QueryBatchGet([FromQuery] GetQueryBatchRequest request)
        {
            var queryBatchRequest = request.ToQueryBatchRequest();
            
            return await base.QueryBatch(queryBatchRequest);
        }
        
        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpPost("smart-batch")]
        public new async Task<QueryBatchResponse> QuerySmartBatch([FromBody] QueryBatchRequest request)
        {
            return await base.QuerySmartBatch(request);
        }

        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpGet("smart-batch")]
        public async Task<QueryBatchResponse> QuerySmartBatchGet([FromQuery] GetQueryBatchRequest request)
        {
            var queryBatchRequest = request.ToQueryBatchRequest();
            
            return await base.QuerySmartBatch(queryBatchRequest);
        }
        
        /// <summary>
        /// Returns multiple <see cref="QueryBatchResponse"/>s
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpPost("batchcollection")]
        public new async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromBody] QueryBatchCollectionRequest request)
        {
            
            return await base.QueryBatchCollection(request);
        }

        /// <summary>
        /// Returns multiple <see cref="QueryBatchResponse"/>s
        /// </summary>
        /// <param name="queries"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = [ControllerConstants.ClientTokenDrive])]
        [HttpGet("batchcollection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromQuery] GetCollectionQueryParamSection[] queries)
        {
            var sections = new List<CollectionQueryParamSection>();
            foreach(var query in queries)
            {
                var section = query.ToCollectionQueryParamSection();
                section.AssertIsValid();
                sections.Add(section);
            }

            var request = new QueryBatchCollectionRequest(){
                Queries = sections
            };
            
            return await base.QueryBatchCollection(request);
        }
    }
}
