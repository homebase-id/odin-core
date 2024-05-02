using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives;
using Swashbuckle.AspNetCore.Annotations;
using System.Collections.Generic;
using Odin.Services.Authentication.Owner;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveQueryV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveQueryController(TenantSystemStorage tenantSystemStorage) : DriveQueryControllerBase
    {
        /// <summary>
        /// Returns modified files (their last modified property must be set).
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("modified")]
        public async Task<QueryModifiedResult> QueryModified([FromBody] QueryModifiedRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return await base.QueryModified(request, cn);
        }

        /// <summary>
        /// Returns modified files (their last modified property must be set).
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpGet("modified")]
        public async Task<QueryModifiedResult> QueryModifiedGet([FromQuery] GetQueryModifiedRequest request)
        {
            var queryModifiedRequest = request.ToQueryModifiedRequest();
            using var cn = tenantSystemStorage.CreateConnection();
            return await base.QueryModified(queryModifiedRequest, cn);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("batch")]
        public async Task<QueryBatchResponse> QueryBatch([FromBody] QueryBatchRequest request)
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return await base.QueryBatch(request, cn);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpGet("batch")]
        public async Task<QueryBatchResponse> QueryBatchGet([FromQuery] GetQueryBatchRequest request)
        {
            var queryBatchRequest = request.ToQueryBatchRequest();
            using var cn = tenantSystemStorage.CreateConnection();
            return await base.QueryBatch(queryBatchRequest, cn);
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
            using var cn = tenantSystemStorage.CreateConnection();
            return await base.QueryBatchCollection(request, cn);
        }

        /// <summary>
        /// Returns multiple <see cref="QueryBatchResponse"/>s
        /// </summary>
        /// <param name="queries"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("batchcollection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromQuery] GetCollectionQueryParamSection[] queries)
        {
            var sections = new List<CollectionQueryParamSection>();
            foreach(var query in queries)
            {
                var section = query.ToCollectionQueryParamSection();
                section.AssertIsValid();
                sections.Add(section);
            };

            var request = new QueryBatchCollectionRequest(){
                Queries = sections
            };
            using var cn = tenantSystemStorage.CreateConnection();
            return await base.QueryBatchCollection(request, cn);
        }
    }
}
