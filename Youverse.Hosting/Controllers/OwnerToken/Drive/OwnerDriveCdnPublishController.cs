using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing.Constraints;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Tenant;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    [ApiController]
    [Route(OwnerApiPathConstants.DriveQueryV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveCdnPublishController : ControllerBase
    {
        private readonly CdnPublisher _cdnPublisher;

        public OwnerDriveCdnPublishController(CdnPublisher cdnPublisher)
        {
            _cdnPublisher = cdnPublisher;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("publish")]
        public async Task<IActionResult> PublishBatch([FromBody] PublishStaticFileRequest request)
        {
            _cdnPublisher.Publish(request.Filename, request.Sections);
            return Ok();
        }
    }

    public class PublishStaticFileRequest
    {
        public string Filename { get; set; }
        
        public IEnumerable<QueryParamSection> Sections { get; set; }

        // public QueryBatchResultOptions ResultOptions { get; set; }
    }

    
}