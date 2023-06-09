﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Drives;
using Odin.Hosting.Controllers.Base;
using Swashbuckle.AspNetCore.Annotations;
using System.Collections.Generic;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveQueryV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveQueryController : DriveQueryControllerBase
    {
        /// <summary>
        /// Returns modified files (their last modified property must be set).
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
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
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpGet("modified")]
        public async Task<QueryModifiedResult> QueryModifiedGet([FromQuery] GetQueryModifiedRequest request)
        {
            var queryModifiedRequest = request.toQueryModifiedRequest();
            return await base.QueryModified(queryModifiedRequest);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("batch")]
        public new async Task<QueryBatchResponse> QueryBatch([FromBody] QueryBatchRequest request)
        {
            return await base.QueryBatch(request);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
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
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
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
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpGet("batchcollection")]
        public async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromQuery] GetCollectionQueryParamSection[] queries)
        {
            var sections = new List<CollectionQueryParamSection>();
            foreach(var query in queries)
            {
                var section = query.toCollectionQueryParamSection();
                section.AssertIsValid();
                sections.Add(section);
            };

            var request = new QueryBatchCollectionRequest(){
                Queries = sections
            };
            return await base.QueryBatchCollection(request);
        }
    }
}
