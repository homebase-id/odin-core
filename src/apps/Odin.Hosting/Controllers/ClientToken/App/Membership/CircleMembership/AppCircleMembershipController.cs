﻿using Microsoft.AspNetCore.Mvc;
using Odin.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.CircleMembership;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.CircleMembership
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/membership")]
    [AuthorizeValidAppToken]
    public class AppCircleMembershipController : CircleMembershipControllerBase
    {
        public AppCircleMembershipController(CircleMembershipService circleMembershipService, TenantSystemStorage tenantSystemStorage):base(circleMembershipService, tenantSystemStorage)
        {
        }
    }
}