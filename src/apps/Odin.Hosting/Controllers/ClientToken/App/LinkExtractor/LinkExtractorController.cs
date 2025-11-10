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
        public async Task<LinkMeta> ExtractLinkInfo(string url)
        {
            var meta = await linkMetaExtractor.ExtractAsync(url);
            return meta;
        }
    }
}