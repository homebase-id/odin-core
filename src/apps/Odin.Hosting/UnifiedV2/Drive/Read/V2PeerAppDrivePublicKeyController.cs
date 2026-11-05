using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Peer.Incoming.Drive.Query;
using PeerDriveQueryService = Odin.Services.Peer.Outgoing.Drive.Query.PeerDriveQueryService;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive.Read
{
    /// <summary>
    /// Fetches a peer drive's write-only public key — the half you seal a deposit to.
    /// </summary>
    /// <remarks>
    /// The point of the key is writing to a drive you cannot read: seal to the public half and only a
    /// holder of that drive's storage key can open it, so collecting deposits costs the recipient no new
    /// grant (docs/security-model.md, Drive addressing).
    /// <para>
    /// Served to <b>a caller with Write on the target drive, or a connection its owner has reviewed</b>; anyone
    /// else is refused rather than told the drive is missing.  A connection not yet reviewed gets 403 with
    /// <c>ContactNotReviewed</c>, so the app can say the owner must review it first.
    /// </para>
    /// </remarks>
    [ApiController]
    [Route(UnifiedApiRouteConstants.PeerAppDriveBySlug)]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [ApiExplorerSettings(GroupName = "v2")]
    public class V2PeerAppDrivePublicKeyController(PeerDriveQueryService peerDriveQueryService)
        : OdinControllerBase
    {
        /// <summary>Gets the write-only public key of a peer drive named by slug.</summary>
        /// <remarks>
        /// <para>The drive lives on <paramref name="odinId"/>'s identity and is named by slug, so you need
        /// no guid constants shared with them — a hostname and two slugs is the whole address.</para>
        /// <para>400 when nothing there answers to the address; 403 when you may neither write to it nor are a
        /// connection its owner has reviewed, with <c>ContactNotReviewed</c> when they haven't reviewed you yet.</para>
        /// </remarks>
        /// <param name="odinId">The identity hosting the drive, e.g. <c>frodo.dotyou.cloud</c>.</param>
        /// <param name="appSlug">The app's slug <b>as registered on that identity</b>, e.g. <c>chat</c>.</param>
        /// <param name="driveSlug">The drive's slug within that app, e.g. <c>messages</c>.</param>
        [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
        [HttpGet("public-key")]
        public async Task<DrivePublicKeyResponse> GetDriveWriteOnlyPublicKey([FromRoute] string odinId, [FromRoute] OdinSlug appSlug,
            [FromRoute] OdinSlug driveSlug)
        {
            OdinValidationUtils.AssertIsValidOdinId(odinId, out var id);

            return await peerDriveQueryService.GetRemoteDriveWriteOnlyPublicKeyAsync(id, appSlug, driveSlug,
                WebOdinContext);
        }
    }
}
