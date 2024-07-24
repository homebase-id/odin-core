using Microsoft.AspNetCore.Mvc;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Transit.Payload;
using Odin.Services.Base;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.Controllers.ClientToken.App.Transit
{
    [ApiController]
    [Route(AppApiPathConstants.PeerSenderV1)]
    [AuthorizeValidAppToken]
    public class AppPeerDirectPayloadController(
        IPeerOutgoingTransferService peerOutgoingTransferService,
        TenantSystemStorage tenantSystemStorage,
        DriveManager driveManager) :
        PeerDirectPayloadControllerBase(peerOutgoingTransferService, tenantSystemStorage, driveManager);
}