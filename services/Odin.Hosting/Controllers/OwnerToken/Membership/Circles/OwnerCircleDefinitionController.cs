using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Services.Membership.Circles;
using Odin.Core.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Circles;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.Circles
{
    
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesDefinitionsV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerCircleDefinitionController : CircleDefinitionControllerBase
    {
        public OwnerCircleDefinitionController(CircleMembershipService circleMembershipService, CircleNetworkService cns) : base(cns, circleMembershipService)
        {
        }
    }
}