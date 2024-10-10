using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Drive.Specialized;

namespace Odin.Hosting.Controllers.OwnerToken.Drive.Specialized
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.DriveQuerySpecializedClientUniqueId)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveQueryByUniqueIdController(
        ILogger<OwnerDriveQueryByUniqueIdController> logger,
        PeerOutgoingTransferService peerOutgoingTransferService,
        TenantSystemStorage tenantSystemStorage)
        : DriveQueryByUniqueIdControllerBase(peerOutgoingTransferService, tenantSystemStorage)
    {
        private readonly ILogger<OwnerDriveQueryByUniqueIdController> _logger = logger;
    }
}