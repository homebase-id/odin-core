﻿using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Transit
{
    [ApiController]
    [Route(OwnerApiPathConstants.PeerV1 + "/inbox/processor")]
    [AuthorizeValidOwnerToken]
    public class OwnerPeerProcessController(
        PeerInboxProcessor peerInboxProcessor,
        TenantSystemStorage tenantSystemStorage
        ) : PeerProcessControllerBase(peerInboxProcessor, tenantSystemStorage);
}