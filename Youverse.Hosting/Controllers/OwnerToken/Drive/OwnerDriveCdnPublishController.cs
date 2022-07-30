using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;
using Youverse.Core.Services.Tenant;
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
        private readonly ITenantProvider _tenantProvider;

        public OwnerDriveCdnPublishController(IDriveQueryService driveQueryService, DotYouContextAccessor contextAccessor, IDriveService driveService, ITenantProvider tenantProvider)
        {
            _driveQueryService = driveQueryService;
            _contextAccessor = contextAccessor;
            _driveService = driveService;
            _tenantProvider = tenantProvider;
        }

        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("publish")]
        public async Task<IActionResult> PublishBatch([FromBody] PublishStaticFileRequest request)
        {
            //run GetBatch for each request.QueryParams 
            // using var filestream = System
            foreach (var section in request.Sections)
            {
                var qp = section.QueryParams;
                var driveId = await _driveService.GetDriveIdByAlias(qp.TargetDrive, true);

                var options = new QueryBatchResultOptions()
                {
                    IncludeMetadataHeader = true, //read from options
                    Cursor = null,
                    MaxRecords = 10000 //TODO: consider
                };
                
                var results = await _driveQueryService.GetBatch(driveId.GetValueOrDefault(), qp, options);
                
                results.SearchResults
            }
            //filter: only files where
            // + PayloadIsEncrypted = false
            // + RequiredSecurityGroup = Anonymous

            //create json static file

            //store on disk @ /youverse.hosting/client/cdn/${identity}/${request.Filename}

            string folder = _tenantProvider.GetCurrentTenant()!.Name;


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