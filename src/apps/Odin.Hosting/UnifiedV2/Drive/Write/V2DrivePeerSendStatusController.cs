using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

/// <summary>
/// Whether the caller's own send to a peer, from the transient drive, is still waiting in this identity's outbox: a new
/// file or an update of a peer's file, which leaves no copy here to hold a transfer history, or a delete of one naming the
/// version it is meant for.  For a client that missed its result event (<c>peerSendResult</c>): not pending, and the
/// peer's copy shows whether it landed.
/// </summary>
[ApiController]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DrivePeerSendStatusController(PeerOutbox peerOutbox) : OdinControllerBase
{
    /// <summary>
    /// The send to <paramref name="odinId"/>'s drive <paramref name="driveId"/> of the file with global transit id
    /// <paramref name="gtid"/>, which the upload or update answered with <paramref name="sentVersionTag"/>
    /// (its <c>newVersionTag</c>), or a delete named as its <c>expectedVersionTag</c> (the two can share a tag:
    /// docs/file-access.md); without one, any of the caller's sends of the file, for a client that lost that answer.
    /// Only the caller's own: an app's sends, or the owner's own client's; it asks <c>UseTransitWrite</c>, as the send did.
    /// A client polling need not ask again before <c>nextAttempt</c>, when the outbox next takes it up, nor while
    /// <c>sending</c>: the result event comes then.
    /// </summary>
    [SwaggerOperation(Summary = "Whether your send of a file to a peer is still waiting in the outbox", Tags = [SwaggerInfo.FileWrite])]
    [HttpGet(UnifiedApiRouteConstants.PeerSendStatus)]
    public async Task<PeerSendStatus> GetSendStatus([FromRoute] string odinId, [FromRoute] Guid driveId, [FromRoute] Guid gtid,
        [FromQuery] Guid? sentVersionTag)
    {
        OdinValidationUtils.AssertIsValidOdinId(odinId, out var recipient);
        OdinValidationUtils.AssertNotEmptyGuid(driveId, "missing drive id");
        OdinValidationUtils.AssertNotEmptyGuid(gtid, "missing global transit id");
        // Part of sending to a peer, as the send itself is (docs/security-model.md)
        WebOdinContext.PermissionsContext.AssertCanUsePeer(PeerAccess.Write);

        return await peerOutbox.GetTransientSendStatusAsync(recipient, driveId, gtid, sentVersionTag, WebOdinContext);
    }
}
