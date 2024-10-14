﻿using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Base;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Transit
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.PeerSenderV1)]
    [Route(GuestApiPathConstants.PeerSenderV1)]
    [AuthorizeValidGuestOrAppToken]
    public class ClientTokenPeerSenderController(PeerOutgoingTransferService peerOutgoingTransferService, TenantSystemStorage tenantSystemStorage) :
        PeerSenderControllerBase(peerOutgoingTransferService, tenantSystemStorage);
}