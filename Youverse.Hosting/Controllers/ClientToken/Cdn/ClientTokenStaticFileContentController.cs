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
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenCdn })]
        [HttpPost("staticfile")]
        public async Task<IActionResult> GetThumbnail([FromBody] GetStaticFileRequest request)
        {
            var payload = await _staticFileContentService.GetStaticFileStream(request.Filename);
            if (null == payload)
            {
                return NotFound();
            }
            
            return new FileStreamResult(payload, "application/json");
        }
    }
}