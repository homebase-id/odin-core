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
    public async Task<LinkMeta> ExtractLinkInfo(string url)
    {
        var meta = await linkMetaExtractor.ExtractAsync(url);
        return meta;
    }
}
