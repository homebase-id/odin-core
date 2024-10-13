using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DriveQueryV1)]
    [Route(GuestApiPathConstants.DriveQueryV1)]
    [AuthorizeValidGuestOrAppToken]
    public class DriveQueryController(TenantSystemStorage tenantSystemStorage) : DriveQueryControllerBase
    {
        /// <summary>
        /// Returns modified files (their last modified property must be set).
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("modified")]
        public async Task<QueryModifiedResult> QueryModified([FromBody] QueryModifiedRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            return await base.QueryModified(request, db);
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
            var queryModifiedRequest = request.ToQueryModifiedRequest();
            var db = tenantSystemStorage.IdentityDatabase;
            return await base.QueryModified(queryModifiedRequest, db);
        }

        /// <summary>
        /// Returns files matching the query params
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("batch")]
        public async Task<QueryBatchResponse> QueryBatch([FromBody] QueryBatchRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            return await base.QueryBatch(request, db);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("batch")]
        public async Task<QueryBatchResponse> QueryBatchGet([FromQuery] GetQueryBatchRequest request)
        {
            var queryBatchRequest = request.ToQueryBatchRequest();
            var db = tenantSystemStorage.IdentityDatabase;
            return await base.QueryBatch(queryBatchRequest, db);
        }

        /// <summary>
        /// Returns multiple <see cref="QueryBatchResponse"/>s
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("batchcollection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromBody] QueryBatchCollectionRequest request)
        {
            var db = tenantSystemStorage.IdentityDatabase;
            return await base.QueryBatchCollection(request, db);
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
            }

            var request = new QueryBatchCollectionRequest(){
                Queries = sections
            };
            var db = tenantSystemStorage.IdentityDatabase;
            return await base.QueryBatchCollection(request, db);
        }
    }
}
