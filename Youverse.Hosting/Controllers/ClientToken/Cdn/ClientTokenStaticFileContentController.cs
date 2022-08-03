using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Optimization.Cdn;
using Youverse.Hosting.Controllers.Anonymous;

namespace Youverse.Hosting.Controllers.ClientToken.Cdn
{
    [ApiController]
    [AuthorizeValidExchangeGrant]
    [Route(YouAuthApiPathConstants.CdnV1)]
    public class ClientTokenStaticFileContentController : ControllerBase
    {
        private readonly StaticFileContentService _staticFileContentService;

        public ClientTokenStaticFileContentController(StaticFileContentService staticFileContentService)
        {
            _staticFileContentService = staticFileContentService;
        }

        /// <summary>
        /// Returns the static file's contents
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenCdn })]
        [HttpGet("staticfile")]
        public async Task<IActionResult> GetStaticFile([FromQuery] string filename)
        {
            var payload = await _staticFileContentService.GetStaticFileStream(filename);
            if (null == payload)
            {
                return NotFound();
            }
            
            return new FileStreamResult(payload, "application/json");
        }
    }
}