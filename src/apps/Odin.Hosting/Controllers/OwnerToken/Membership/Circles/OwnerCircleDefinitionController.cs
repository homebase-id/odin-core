using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Circles;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.Circles
{
    
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesDefinitionsV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerCircleDefinitionController : CircleDefinitionControllerBase
    {
        public OwnerCircleDefinitionController(
            CircleMembershipService circleMembershipService,
            CircleNetworkService cns) : base(cns, circleMembershipService)
        {
        }
    }
}