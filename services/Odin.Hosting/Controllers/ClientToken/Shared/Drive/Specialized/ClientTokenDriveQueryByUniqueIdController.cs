﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Base;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Transfer;
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

    public class ClientTokenDriveQueryByUniqueIdController : DriveQueryByUniqueIdControllerBase
    {
        private readonly ILogger<ClientTokenDriveQueryByUniqueIdController> _logger;

        public ClientTokenDriveQueryByUniqueIdController(
            ILogger<ClientTokenDriveQueryByUniqueIdController> logger,
            FileSystemResolver fileSystemResolver,
            IPeerTransferService peerTransferService) :
            base(logger, fileSystemResolver, peerTransferService)
        {
            _logger = logger;
        }
        
    }
}
