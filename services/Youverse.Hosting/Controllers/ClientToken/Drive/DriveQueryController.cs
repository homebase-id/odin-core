﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Optimization.Cdn;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.Anonymous;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1 + "/query")]
    [Route(YouAuthApiPathConstants.DrivesV1 + "/query")]
    [AuthorizeValidExchangeGrant]
    public class DriveQueryController : ControllerBase
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveQueryService _driveQueryService;
        private readonly IDriveStorageService _driveStorageService;

        public DriveQueryController(IDriveQueryService driveQueryService, DotYouContextAccessor contextAccessor, IDriveStorageService driveStorageService)
        {
            _driveQueryService = driveQueryService;
            _contextAccessor = contextAccessor;
            _driveStorageService = driveStorageService;
        }

        /// <summary>
        /// Returns modified files (their last modified property must be set).
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("modified")]
        public async Task<QueryModifiedResult> QueryModified([FromBody] QueryModifiedRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await _driveQueryService.GetModified(driveId, request.QueryParams, request.ResultOptions);
            return batch;
        }

        /// <summary>
        /// Returns files matching the query params
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("batch")]
        public async Task<QueryBatchResponse> QueryBatch([FromBody] QueryBatchRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await _driveQueryService.GetBatch(driveId, request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions());

            return QueryBatchResponse.FromResult(batch);
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
            var collection = await _driveQueryService.GetBatchCollection(request);
            return collection;
        }
    }
}