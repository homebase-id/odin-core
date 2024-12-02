using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing;
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
        ILogger<ClientTokenDriveQueryByUniqueIdController> logger,
        PeerOutgoingTransferService peerOutgoingTransferService)
        : DriveQueryByUniqueIdControllerBase(peerOutgoingTransferService)
    {
        private readonly ILogger<ClientTokenDriveQueryByUniqueIdController> _logger = logger;
    }
}