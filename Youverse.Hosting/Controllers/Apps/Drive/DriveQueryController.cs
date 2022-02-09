using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Authentication.App;

namespace Youverse.Hosting.Controllers.Apps.Drive
{
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1 + "/query")]
    [Authorize(Policy = AppPolicies.IsAuthorizedApp, AuthenticationSchemes = AppAuthConstants.SchemeName)]
    public class DriveQueryController : ControllerBase
    {
        private readonly DotYouContext _context;
        private readonly IDriveQueryService _driveQueryService;

        public DriveQueryController(IDriveQueryService driveQueryService, DotYouContext context)
        {
            _driveQueryService = driveQueryService;
            _context = context;
        }

        [HttpGet("category")]
        public async Task<IActionResult> GetItemsByCategory(Guid categoryId, bool includeContent, int pageNumber, int pageSize)
        {
            var driveId = _context.AppContext.DriveId.GetValueOrDefault();
            var page = await _driveQueryService.GetItemsByCategory(driveId, categoryId, includeContent, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentlyCreatedItems(bool includeContent, int pageNumber, int pageSize)
        {
            var driveId = _context.AppContext.DriveId.GetValueOrDefault();
            var page = await _driveQueryService.GetRecentlyCreatedItems(driveId, includeContent, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }

        [HttpPost("rebuild")]
        public async Task<bool> Rebuild(Guid driveId)
        {
            await _driveQueryService.RebuildBackupIndex(driveId);
            return true;
        }
        
        [AllowAnonymous]
        [HttpGet("unencrypted")]
        public async Task<IActionResult> GetUnencryptedItems(Guid categoryId, bool includeContent, int pageNumber, int pageSize)
        {
            /*
             *
             *  -- HOW DO I KNOW WHICH DRIVE?
             *          the caller can specify the app and from that we'll look up the drive
             *          there will be an AnonymousAuthenticationHandler that will pick up the app from the header
             *              it will query to get the app drive and anything minimal that is needed
             * better yet, i'll add an endpoint for anonymous only??  will that work?  it means callers would
             * have to decide which to use based on if they are logged in.  i dont like that BUT it would be a single clear boundary for executing code.
             * it is better than AnonymousAuthenticationHandler which allows code to be executed deeper in the system.
             *
             * the question then becomes do i allow calls into the drivequeryservice the same way authenticated calls are made?  if so, then it's
             * pointless to have a special endpoint
             *
             * furthermore - since the drive query serivce applies filtering of data by permission, where do i apply this?
             * i guess this coul dbe added to the upload spec; wherein I state which circle and / or hosts can read the file
             *
             * this would handle the need for the profile store to specify anonymous (public), youauth identitifed, connected, or Circle (or maybe even a white list of DIs)
             *
             * ok -let's do it.
             *
             * 
             *
             * 
             * Unencrypted queries needed
             *  Get home page configuration
             *      
             *      var files = DriveQueryService.GetFilesByType(type = 9999)
             *      var config = driveStorageService.GetFile(files.firstOrDefault().FileId)
             *  Get recent blog posts
             *      var files = DriveQueryService.GetFilesByCategory(primaryCategory=1234);
             *  Get all unencrypted profile attributes
             *      var files = DriveQueryService.GetFilesByType(type=attribute) //Note: since the user has no token, it will only send back files marked unencrypted
             */
            
            /*
             * Encrypted queries needed
             *  get recent chats
             *      DriveQueryService.GetFiles(filetype=3333)
             *  get recent chats by sender
             *      DriveQueryService.GetFilesBySender("frodobaggins.me")
             *  get attributes by profile
             *      DriveQueryService.GetFilesMatchingDN(dn.startswith, P=Default Profile, filetype=5555); //where filetype of 5555 is 
             *  get all profiles
             *      DriveQueryService.GetFilesBy
             *  get all attributes
             */
            var driveId = _context.AppContext.DriveId.GetValueOrDefault();
            var page = await _driveQueryService.(driveId, categoryId, includeContent, new PageOptions(pageNumber, pageSize));
            return new JsonResult(page);
        }
    }
}