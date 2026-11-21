using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Membership.Connections;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Connections;

/// <summary>
/// Who this identity is connected to. Open past the owner and apps, as V1's guest route is: the service
/// requires <c>ReadConnections</c>, which a guest or anonymous visitor holds only when the owner's tenant
/// setting grants it, and a third-party viewer gets identities and public contact cards only.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Connections)]
[UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2ConnectedIdentitiesController(CircleNetworkService circleNetwork) : OdinControllerBase
{
    [HttpGet("connected")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get list of connected identities")]
    public async Task<CursoredResult<RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int count, string cursor)
    {
        var result = await circleNetwork.GetConnectedIdentitiesAsync(count, cursor, WebOdinContext);
        var ownerSide = CallerIsOwnerSideViewer;

        var results = result.Results
            .Select(p => ownerSide ? p.Redacted() : p.RedactedForExternalViewer())
            .ToList();

        if (ownerSide)
        {
            // One pass for the page: the lookups are memoised across it, and most connections have
            // nothing awaiting, so this costs nothing for them.
            await circleNetwork.PopulateAwaitingAppNamesAsync(results, WebOdinContext);
        }

        return new CursoredResult<RedactedIdentityConnectionRegistration>()
        {
            Cursor = result.Cursor,
            Results = results
        };
    }
}
