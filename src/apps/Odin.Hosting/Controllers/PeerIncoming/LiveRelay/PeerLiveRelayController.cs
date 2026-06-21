using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Controllers.Base;
using Odin.Services.LiveRelay;
using Odin.Services.Peer;

namespace Odin.Hosting.Controllers.PeerIncoming.LiveRelay
{
    /// <summary>
    /// Peer perimeter (hop 2 ingress): receives an opaque live data point from another identity's
    /// server. Authenticated by the mutual-TLS peer cert; the data is retained (ephemerally) and
    /// pushed to the matching app's connected sockets.
    /// </summary>
    [ApiController]
    [Route(PeerApiPathConstants.LiveRelayV1)]
    [Authorize(Policy = PeerPerimeterPolicies.IsInOdinNetwork, AuthenticationSchemes = PeerAuthConstants.TransitCapiAuthScheme)]
    [ApiExplorerSettings(GroupName = "peer-v1")]
    public class PeerLiveRelayController(PeerLiveRelayReceiverService receiverService) : OdinControllerBase
    {
        [HttpPost("relay")]
        public async Task<PeerTransferResponse> Relay([FromBody] LiveRelayPeerEnvelope envelope)
        {
            return await receiverService.ReceiveAsync(envelope, WebOdinContext);
        }
    }
}
