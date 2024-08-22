﻿using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.Connections
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidOwnerToken]
    public class OwnerCircleNetworkController(CircleNetworkService cn, TenantSystemStorage tenantSystemStorage, CircleNetworkRequestService requestService)
        : CircleNetworkControllerBase(cn, tenantSystemStorage, requestService);
}