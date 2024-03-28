using Microsoft.AspNetCore.Mvc;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Drive.Specialized;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive.Specialized
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DriveQuerySpecializedClientUniqueId)]
    [Route(GuestApiPathConstants.DriveQuerySpecializedClientUniqueId)]
    [AuthorizeValidGuestOrAppToken]

    public class ClientTokenDriveQueryByUniqueIdController(
        FileSystemResolver fileSystemResolver,
        IPeerOutgoingTransferService peerOutgoingTransferService)
        : DriveQueryByUniqueIdControllerBase(fileSystemResolver, peerOutgoingTransferService);
}
