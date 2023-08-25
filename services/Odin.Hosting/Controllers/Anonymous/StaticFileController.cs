#nullable enable

using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Optimization.Cdn;
using Odin.Core.Services.Tenant;
using Odin.Hosting.Controllers.Home;

namespace Odin.Hosting.Controllers.Anonymous
{
    [ApiController]
    public class StaticFileController : Controller
    {
        private readonly HomeAuthenticatorService _homeAuthenticatorService;
        private readonly string _currentTenant;
        private readonly StaticFileContentService _staticFileContentService;


        public StaticFileController(ITenantProvider tenantProvider, HomeAuthenticatorService homeAuthenticatorService, StaticFileContentService staticFileContentService)
        {
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
            _homeAuthenticatorService = homeAuthenticatorService;
            _staticFileContentService = staticFileContentService;
        }

        //

        /// <summary>
        /// Returns the static file's contents
        /// </summary>
        /// <returns></returns>
        [HttpGet("cdn/{filename}")]
        public async Task<IActionResult> GetStaticFile(string filename)
        {
            var (config, stream) = await _staticFileContentService.GetStaticFileStream(filename);
            return SendStream(config, stream);
        }

        /// <summary>
        /// Returns the public profile image
        /// </summary>
        [HttpGet("pub/image")]
        public async Task<IActionResult> GetPublicImage()
        {
            var (config, stream) = await _staticFileContentService.GetStaticFileStream(StaticFileConstants.ProfileImageFileName);
            return SendStream(config, stream);
        }

        /// <summary>
        /// Returns the public profile data
        /// </summary>
        [HttpGet("pub/profile")]
        public async Task<IActionResult> GetPublicProfileData()
        {
            var (config, stream) = await _staticFileContentService.GetStaticFileStream(StaticFileConstants.PublicProfileCardFileName);
            return SendStream(config, stream);
        }

        private IActionResult SendStream(StaticFileConfiguration config, Stream? stream)
        {
            if (null == stream || stream == Stream.Null)
            {
                return NotFound();
            }

            if (config.CrossOriginBehavior == CrossOriginBehavior.AllowAllOrigins)
            {
                if (this.Response.Headers.ContainsKey("Access-Control-Allow-Origin"))
                {
                    this.Response.Headers.Remove("Access-Control-Allow-Origin");
                }

                this.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            }

            // TODO: set cache control to 1 year; Needs last modified date on the response headers
            // this.Response.Headers.Add("Cache-Control", "max-age=31536000");
            this.Response.Headers.Add("Cache-Control", "max-age=120");

            return new FileStreamResult(stream, config.ContentType);
        }
        //
    }
}
