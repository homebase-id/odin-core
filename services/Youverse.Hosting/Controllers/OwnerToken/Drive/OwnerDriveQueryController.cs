using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.FileSystem.Comment;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.ClientToken.Drive;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveQueryV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveQueryController : ControllerBase
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveQueryService _driveQueryService;
        private readonly CommentFileQueryService _commentFileQueryService;
        private readonly IDriveStorageService _driveStorageService;

        public OwnerDriveQueryController(IDriveQueryService driveQueryService, DotYouContextAccessor contextAccessor, IDriveStorageService driveStorageService, CommentFileQueryService commentFileQueryService)
        {
            _driveQueryService = driveQueryService;
            _contextAccessor = contextAccessor;
            _driveStorageService = driveStorageService;
            _commentFileQueryService = commentFileQueryService;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("modified")]
        public async Task<QueryModifiedResult> GetModified([FromBody] QueryModifiedRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await _driveQueryService.GetModified(driveId, request.QueryParams, request.ResultOptions);
            return batch;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("batch")]
        public async Task<QueryBatchResponse> QueryBatch([FromBody] QueryBatchRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);

            QueryBatchResult batch = null;
            if (request.QueryParams.UseReactionDriveHack)
            {
                batch = await _commentFileQueryService.GetBatch(driveId, request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions());
            }
            else
            {
                batch = await _driveQueryService.GetBatch(driveId, request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions());
            }

            return QueryBatchResponse.FromResult(batch);
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
            var collection = await _driveQueryService.GetBatchCollection(request);
            return collection;
        }
    }
}