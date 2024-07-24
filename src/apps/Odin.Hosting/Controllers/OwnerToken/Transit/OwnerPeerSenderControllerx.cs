using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Transit.Payload;
using Odin.Services.Base;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.PeerSenderV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerPeerDirectPayloadController(
        IPeerOutgoingTransferService peerOutgoingTransferService,
        TenantSystemStorage tenantSystemStorage,
        DriveManager driveManager)
        : PeerDirectPayloadControllerBase(peerOutgoingTransferService, tenantSystemStorage, driveManager);
}