using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.PeerSenderV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerPeerSenderController : PeerSenderControllerBase
    {
        public OwnerPeerSenderController(
            PeerOutgoingTransferService peerOutgoingTransferService,
            TenantSystemStorage tenantSystemStorage) : base(peerOutgoingTransferService, tenantSystemStorage)
        {
        }
    }
}