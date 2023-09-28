using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Circles;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.Circles
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesDefinitionsV1)]
    [AuthorizeValidAppToken]
    public class AppCircleDefinitionController : CircleDefinitionControllerBase
    {
        public AppCircleDefinitionController(CircleMembershipService circleMembershipService, CircleNetworkService cns) : base(cns, circleMembershipService)
        {
        }
    }
}