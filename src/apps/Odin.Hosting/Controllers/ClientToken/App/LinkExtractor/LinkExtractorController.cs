using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.LinkMetaExtractor;

namespace Odin.Hosting.Controllers.ClientToken.App.LinkExtractor
{
    [ApiController]
    [Route(AppApiPathConstantsV1.UtilsV1 + "/links")]
    [AuthorizeValidAppToken]
    public class LinkExtractorController(ILinkMetaExtractor linkMetaExtractor) : OdinControllerBase
    {
        [HttpGet("extract")]
        public async Task<ActionResult<LinkMeta>> ExtractLinkInfo(string url)
        {
            // "Nothing to preview" is a status, not an empty body: a null here would go out as a 204 that
            // clients then try to deserialize (#1754).
            var meta = await linkMetaExtractor.ExtractAsync(url);
            return meta == null ? NotFound() : meta;
        }
    }
}