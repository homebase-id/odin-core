using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Optimization.Cdn;

namespace Odin.Hosting.Controllers.Base.Cdn
{
    public class StaticFileContentPublishControllerBase : ControllerBase
    {
        private readonly StaticFileContentService _staticFileContentService;

        public StaticFileContentPublishControllerBase(StaticFileContentService staticFileContentService)
        {
            _staticFileContentService = staticFileContentService;
        }

        /// <summary>
        /// Creates a static file which contents match the query params.  Accessible to the public
        /// as it will only contain un-encrypted content targeted at Anonymous users
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("publish")]
        public async Task<StaticFilePublishResult> PublishBatch([FromBody] PublishStaticFileRequest request)
        {
            var publishResult = await _staticFileContentService.Publish(request.Filename, request.Config, request.Sections);
            return publishResult;
        }
    }

}