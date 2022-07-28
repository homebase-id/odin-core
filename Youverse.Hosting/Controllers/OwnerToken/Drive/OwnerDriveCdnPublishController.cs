using System;
using System.Collections.Generic;
using System.Linq;
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
    public class OwnerDriveCdnPublishController : ControllerBase
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly IDriveQueryService _driveQueryService;
        private readonly IDriveService _driveService;

        public OwnerDriveCdnPublishController(IDriveQueryService driveQueryService, DotYouContextAccessor contextAccessor, IDriveService driveService)
        {
            _driveQueryService = driveQueryService;
            _contextAccessor = contextAccessor;
            _driveService = driveService;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("publish")]
        public async Task<IActionResult> PublishBatch([FromBody] PublishStaticFileRequest request)
        {
            //run GetBatch for each request.QueryParams 
            
            //filter: only files where
            // + PayloadIsEncrypted = false
            // + RequiredSecurityGroup = Anonymous

            //create json static file

            //store on disk @ /youverse.hosting/client/cdn/${identity}/${request.Filename}

            
            return null;
        }
    }

    public class PublishStaticFileRequest
    {
        public string Filename { get; set; }
        
        public string OverrideContentType { get; set; } //TODO: should we support this?
        
        public IEnumerable<QueryParamSection> Sections { get; set; }

        // public QueryBatchResultOptions ResultOptions { get; set; }
    }

    public class QueryParamSection
    {
        public string Name { get; set; }
        
        public FileQueryParams QueryParams { get; set; }
        
        //todo: need to be able to exclude/include payload
    }
}