using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Network;

namespace Odin.Hosting.Controllers.PeerIncoming.Membership;

/// <summary>
/// A connected identity's own sealed copy of one of this identity's circle keys (Selective Share).  Only while
/// the circle grants the caller, and only the caller's copy (<see cref="CircleShareKeyService"/>).
/// </summary>
[ApiController]
[Route(PeerApiPathConstants.CirclesV1)]
[Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCapiAuthScheme)]
public class PeerCircleShareKeyController(CircleShareKeyService shareKeys) : OdinControllerBase
{
    /// <summary>404 while the circle's app has sealed no copy for the caller.</summary>
    [HttpGet("{circleId:guid}/my-share-key")]
    public async Task<ActionResult<SealedShareKeyResponse>> GetMySealedShareKey([FromRoute] Guid circleId)
    {
        var key = await shareKeys.GetCallersSealedShareKeyAsync(new GuidId(circleId), WebOdinContext);
        return key == null ? NotFound() : new SealedShareKeyResponse { SealedKey = key };
    }
}
