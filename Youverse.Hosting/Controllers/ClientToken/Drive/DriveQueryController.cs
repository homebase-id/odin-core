using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
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
        private readonly IDriveService _driveService;

        public DriveQueryController(IDriveQueryService driveQueryService, DotYouContextAccessor contextAccessor, IDriveService driveService)
        {
            _driveQueryService = driveQueryService;
            _contextAccessor = contextAccessor;
            _driveService = driveService;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("recent")]
        public async Task<IActionResult> QueryModified([FromBody] QueryModifiedRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await _driveQueryService.GetModified(driveId, request.QueryParams, request.ResultOptions);
            return new JsonResult(batch);
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("batch")]
        public async Task<IActionResult> QueryBatch([FromBody] QueryBatchRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await _driveQueryService.GetBatch(driveId, request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions());
            
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