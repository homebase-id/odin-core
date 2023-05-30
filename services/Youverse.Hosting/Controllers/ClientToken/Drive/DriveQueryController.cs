using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Optimization.Cdn;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1 + "/query")]
    [Route(YouAuthApiPathConstants.DrivesV1 + "/query")]
    [AuthorizeValidExchangeGrant]
    public class DriveQueryController : DriveQueryControllerBase
    {
        /// <summary>
        /// Returns modified files (their last modified property must be set).
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("modified")]
        public new async Task<QueryModifiedResult> QueryModified([FromBody] QueryModifiedRequest request)
        {
            return await base.QueryModified(request);
        }

        /// <summary>
        /// Returns modified files (their last modified property must be set).
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("modified")]
        public async Task<QueryModifiedResult> QueryModifiedGet([FromQuery] GetQueryModifiedRequest request)
        {
            var queryModifiedRequest = request.toQueryModifiedRequest();
            return await base.QueryModified(queryModifiedRequest);
        }

        /// <summary>
        /// Returns files matching the query params
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("batch")]
        public new async Task<QueryBatchResponse> QueryBatch([FromBody] QueryBatchRequest request)
        {
            return await base.QueryBatch(request);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("batch")]
        public async Task<QueryBatchResponse> QueryBatchGet([FromQuery] GetQueryBatchRequest request)
        {
            var queryBatchRequest = request.toQueryBatchRequest();
            return await base.QueryBatch(queryBatchRequest);
        }

        /// <summary>
        /// Returns multiple <see cref="QueryBatchResponse"/>s
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("batchcollection")]
        public new async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromBody] QueryBatchCollectionRequest request)
        {
            return await base.QueryBatchCollection(request);
        }
    }
}
