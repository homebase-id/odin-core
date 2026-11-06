using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer;

namespace Odin.Hosting.Controllers.PeerIncoming.Membership;

/// <summary>
/// This identity's own lists, for a connected identity asking server to server: who it is connected to and who it
/// follows. The caller is evaluated as the connection it is, so ReadConnections and ReadWhoIFollow come from this
/// owner's settings (AllConnectedIdentitiesCanView...) or a circle; anything not shared with connections is a 403.
/// </summary>
/// <remarks>
/// One hop only: this answers about this identity and never asks its own connections on the caller's behalf.
/// Domains only, so none of the owner's judgments about a connection leave.
/// </remarks>
[ApiController]
[Route(PeerApiPathConstants.NetworkV1)]
[Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCapiAuthScheme)]
public class PeerNetworkListsController(CircleNetworkService circleNetwork, FollowerService followerService)
    : OdinControllerBase
{
    [HttpGet("connected")]
    public async Task<CursoredResult<string>> GetConnected(int count, string cursor)
    {
        var page = await circleNetwork.GetConnectedIdentitiesAsync(count, cursor, WebOdinContext);
        return new CursoredResult<string>
        {
            Cursor = page.Cursor,
            Results = page.Results.Select(icr => icr.OdinId.DomainName).ToList()
        };
    }

    [HttpGet("identities-i-follow")]
    public async Task<CursoredResult<string>> GetIdentitiesIFollow(int count, string cursor)
    {
        return await followerService.GetIdentitiesIFollowAsync(count, cursor, WebOdinContext);
    }
}
