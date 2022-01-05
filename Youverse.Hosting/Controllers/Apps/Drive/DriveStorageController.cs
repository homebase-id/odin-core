using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Authentication.App;


namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route("/api/apps/v1/drive")]
    [Authorize(Policy = AppPolicies.IsAuthorizedApp, AuthenticationSchemes = AppAuthConstants.SchemeName)]
    public class DriveStorageController : ControllerBase
    {
        private readonly IDriveService _driveService;
        private readonly IDriveQueryService _queryService;
        private readonly DotYouContext _context;

        public DriveStorageController(DotYouContext context, IDriveService driveService, IDriveQueryService queryService)
        {
            _context = context;
            _driveService = driveService;
            _queryService = queryService;
        }
    }
}