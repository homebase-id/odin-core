using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Base;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Drive.Specialized;

namespace Odin.Hosting.Controllers.OwnerToken.Drive.Specialized
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveQuerySpecializedClientUniqueId)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveQueryByUniqueIdController(
        ILogger<OwnerDriveQueryByUniqueIdController> logger,
        FileSystemResolver fileSystemResolver,
        IPeerTransferService peerTransferService)
        : DriveQueryByUniqueIdControllerBase(fileSystemResolver, peerTransferService)
    {
        private readonly ILogger<OwnerDriveQueryByUniqueIdController> _logger = logger;
    }
}