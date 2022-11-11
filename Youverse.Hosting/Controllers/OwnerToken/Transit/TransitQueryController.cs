using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Identity;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Controllers.OwnerToken.Transit
{
    [ApiController]
    [Route(OwnerApiPathConstants.TransitQueryV1)]
    [AuthorizeValidOwnerToken]
    public class TransitQueryController : ControllerBase
    {
        private readonly TransitQueryService _transitQueryService;

        public TransitQueryController(TransitQueryService transitQueryService)
        {
            _transitQueryService = transitQueryService;
        }

        // [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        // [HttpPost("modified")]
        // public async Task<QueryModifiedResult> GetModified([FromBody] QueryModifiedRequest request)
        // {
        //     var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
        //     var batch = await _driveQueryService.GetModified(driveId, request.QueryParams, request.ResultOptions);
        //     return batch;
        // }

        [SwaggerOperation(Tags = new[] { ControllerConstants.TransitQuery })]
        [HttpPost("batch")]
        public async Task<QueryBatchResponse> QueryBatch([FromBody] TransitQueryBatchRequest request)
        {
            var batch = await _transitQueryService.GetBatch((DotYouIdentity)request.DotYouId, request);
            
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