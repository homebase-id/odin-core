using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Membership.Circles;
using Odin.Services.Peer.Outgoing.Network;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Circles;

/// <summary>
/// Selective Share's circle keys, for the app that owns the circle (docs/selective-share-plan.md): the circle's
/// key locked with the app's key, and each member's sealed copy.  The server keeps them and opens none.
/// </summary>
/// <remarks>
/// The owner console may act on any app circle; an app only on one it owns, and nobody on an owner circle
/// (<see cref="CircleShareKeyService"/>).
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.CircleShareKey)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2CircleShareKeyController(CircleShareKeyService shareKeys) : OdinControllerBase
{
    /// <summary>The circle's key, locked with the owning app's key; empty until the app sets one.</summary>
    [HttpGet]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get a circle's share key")]
    public async Task<CircleShareKeyData> GetShareKey([FromRoute] Guid circleId)
    {
        return new CircleShareKeyData { AppKeyLockedShareKey = await shareKeys.GetShareKeyAsync(new GuidId(circleId), WebOdinContext) };
    }

    /// <summary>Sets the circle's key, once; refused (CircleShareKeyAlreadySet) when it has one.</summary>
    [HttpPut]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Set a circle's share key, once")]
    public async Task<IActionResult> SetShareKey([FromRoute] Guid circleId, [FromBody] CircleShareKeyData request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        await shareKeys.SetShareKeyAsync(new GuidId(circleId), request.AppKeyLockedShareKey, WebOdinContext);
        return Ok();
    }

    /// <summary>
    /// A developer feature: clears the circle's key and every member's copy, so a key stored by mistake can be
    /// replaced.  Owner console only.
    /// </summary>
    [HttpDelete]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Reset a circle's share key and its members' copies (developers)")]
    public async Task<IActionResult> ResetShareKey([FromRoute] Guid circleId)
    {
        await shareKeys.ResetShareKeyAsync(new GuidId(circleId), WebOdinContext);
        return Ok();
    }

    /// <summary>The members with no sealed copy of the circle's key yet, for the app to seal for.</summary>
    [HttpGet("unsealed-members")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "List a circle's members without a sealed share key")]
    public async Task<List<string>> GetUnsealedMembers([FromRoute] Guid circleId)
    {
        var members = await shareKeys.GetMembersWithoutSealedShareKeyAsync(new GuidId(circleId), WebOdinContext);
        return members.Select(m => m.DomainName).ToList();
    }

    /// <summary>Stores members' sealed copies of the circle's key, all or none.</summary>
    [HttpPut("members")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Store members' sealed share keys")]
    public async Task<IActionResult> SetSealedShareKeys([FromRoute] Guid circleId, [FromBody] SetSealedShareKeysRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        await shareKeys.SetSealedShareKeysAsync(new GuidId(circleId), request.Members, WebOdinContext);
        return Ok();
    }
}

/// <summary>
/// This identity's sealed copy of another identity's circle key, fetched from their server.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.PeerCircleShareKey)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2PeerCircleShareKeyController(PeerCircleShareKeyService peerShareKeys) : OdinControllerBase
{
    /// <summary>
    /// Your sealed copy of <paramref name="odinId"/>'s circle key; empty while they have sealed none for you.
    /// 403 when the circle doesn't grant you access.
    /// </summary>
    [HttpGet]
    [SwaggerOperation(Tags = [SwaggerInfo.NewStuff], Summary = "Get your sealed copy of a peer's circle key")]
    public async Task<SealedShareKeyResponse> GetMySealedShareKey([FromRoute] string odinId, [FromRoute] Guid circleId)
    {
        OdinValidationUtils.AssertIsValidOdinId(odinId, out var id);
        return new SealedShareKeyResponse
        {
            SealedKey = await peerShareKeys.GetMySealedShareKeyAsync(id, new GuidId(circleId), WebOdinContext,
                HttpContext.RequestAborted)
        };
    }
}

public class CircleShareKeyData
{
    public byte[] AppKeyLockedShareKey { get; set; }
}

public class SetSealedShareKeysRequest
{
    public List<SealedShareKey> Members { get; set; }
}
