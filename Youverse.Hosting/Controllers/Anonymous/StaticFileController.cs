#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Optimization.Cdn;
using Youverse.Core.Services.Tenant;

namespace Youverse.Hosting.Controllers.Anonymous
{
    [ApiController]
    [Route("/cdn")]
    public class StaticFileController : Controller
    {
        private readonly IYouAuthService _youAuthService;
        private readonly string _currentTenant;
        private readonly StaticFileContentService _staticFileContentService;


        public StaticFileController(ITenantProvider tenantProvider, IYouAuthService youAuthService, StaticFileContentService staticFileContentService)
        {
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
            _youAuthService = youAuthService;
            _staticFileContentService = staticFileContentService;
        }

        //


        /// <summary>
        /// Returns the static file's contents
        /// </summary>
        /// <returns></returns>
        [HttpGet("{filename}")]
        public async Task<IActionResult> GetStaticFile(string filename)
        {
            var (config, stream) = await _staticFileContentService.GetStaticFileStream(filename);
            if (null == stream)
            {
                return NotFound();
            }

            if (config.CrossOriginBehavior == CrossOriginBehavior.AllowAllOrigins)
            {
                this.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            }

            this.Response.Headers.Add("Cache-Control", "max-age=10, stale-while-revalidate=60");

            return new FileStreamResult(stream, config.ContentType);
        }

        //
    }
}
