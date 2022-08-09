using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing.Constraints;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Optimization.Cdn;

namespace Youverse.Hosting.Controllers.OwnerToken.Cdn
{
    [ApiController]
    [Route(OwnerApiPathConstants.CdnV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerStaticFileContentController : ControllerBase
    {
        private readonly StaticFileContentService _staticFileContentService;

        public OwnerStaticFileContentController(StaticFileContentService staticFileContentService)
        {
            _staticFileContentService = staticFileContentService;
        }

        /// <summary>
        /// Creates a static file which contents match the query params.  Accessible to the public
        /// as it will only contain un-encrypted content targeted at Anonymous users
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCdn })]
        [HttpPost("publish")]
        public async Task<StaticFilePublishResult> PublishBatch([FromBody] PublishStaticFileRequest request)
        {
            var publishResult = await _staticFileContentService.Publish(request.Filename, request.Config, request.Sections);
            return publishResult;
        }
        //
        // /// <summary>
        // /// Returns the static file's contents
        // /// </summary>
        // /// <returns></returns>
        // [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerCdn })]
        // [HttpGet("files/{filename}")]
        // public async Task<IActionResult> GetStaticFile(string filename)
        // {
        //     var (config, stream) = await _staticFileContentService.GetStaticFileStream(filename);
        //     if (null == stream)
        //     {
        //         return NotFound();
        //     }
        //
        //     if (config.CrossOriginBehaviour == CrossOriginBehaviour.AllowAllOrigins)
        //     {
        //         this.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        //     }
        //
        //     return new FileStreamResult(stream, "application/json");
        // }
    }
}