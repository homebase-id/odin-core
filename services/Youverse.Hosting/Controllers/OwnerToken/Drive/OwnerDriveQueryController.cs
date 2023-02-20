using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveQueryV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveQueryController : DriveQueryControllerBase
    {
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("modified")]
        public async Task<QueryModifiedResult> QueryModified([FromBody] QueryModifiedRequest request)
        {
            return await base.QueryModified(request);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("batch")]
        public async Task<QueryBatchResponse> QueryBatch([FromBody] QueryBatchRequest request)
        {
            return await base.QueryBatch(request);
        }

        /// <summary>
        /// Returns multiple <see cref="QueryBatchResponse"/>s
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("batchcollection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromBody] QueryBatchCollectionRequest request)
        {
            return await base.QueryBatchCollection(request);
        }
    }
}