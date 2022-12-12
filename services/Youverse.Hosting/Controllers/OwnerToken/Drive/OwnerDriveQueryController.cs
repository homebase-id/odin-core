using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
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
        private readonly IDriveService _driveService;

        public OwnerDriveQueryController(IDriveQueryService driveQueryService, DotYouContextAccessor contextAccessor, IDriveService driveService)
        {
            _driveQueryService = driveQueryService;
            _contextAccessor = contextAccessor;
            _driveService = driveService;
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
            var batch = await _driveQueryService.GetBatch(driveId, request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions());

            var response = new QueryBatchResponse()
            {
                IncludeMetadataHeader = batch.IncludeMetadataHeader,
                CursorState = batch.Cursor.ToState(),
                SearchResults = batch.SearchResults
            };

            return response;
        }
    }
}