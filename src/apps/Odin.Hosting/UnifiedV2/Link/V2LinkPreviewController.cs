using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.LinkMetaExtractor;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Link;

[ApiController]
[Route(UnifiedApiRouteConstants.Links)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2LinkPreviewController(ILinkMetaExtractor linkMetaExtractor) : OdinControllerBase
{
    [HttpGet("extract")]
    [SwaggerOperation(Tags = [SwaggerInfo.Links])]
    public async Task<ActionResult<LinkMeta>> ExtractLinkInfo(string url)
    {
        // "Nothing to preview" is a status, not an empty body: a null here would go out as a 204 that
        // clients then try to deserialize (#1754).
        var meta = await linkMetaExtractor.ExtractAsync(url);
        return meta == null ? NotFound() : meta;
    }
}
