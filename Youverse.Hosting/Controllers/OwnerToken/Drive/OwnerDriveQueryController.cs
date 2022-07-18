using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;
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

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("recent")]
        public async Task<IActionResult> GetRecent([FromBody] QueryModifiedRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await _driveQueryService.GetRecent(driveId, request.QueryParams, request.ResultOptions);
            return new JsonResult(batch);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.Drive })]
        [HttpPost("batch")]
        public async Task<IActionResult> QueryBatch([FromBody] QueryBatchRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await _driveQueryService.GetBatch(driveId, request.QueryParams, request.ResultOptions.ToQueryBatchResultOptions());

            var response = new QueryBatchResponse()
            {
                IncludeMetadataHeader = batch.IncludeMetadataHeader,
                CursorState = batch.Cursor.ToState(),
                SearchResults = batch.SearchResults
            };

            return new JsonResult(response);
        }
    }
}