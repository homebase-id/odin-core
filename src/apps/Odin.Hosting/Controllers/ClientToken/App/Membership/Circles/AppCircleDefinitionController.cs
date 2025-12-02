using Microsoft.AspNetCore.Mvc;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Circles;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Membership.Circles
{
    [ApiController]
    [Route(AppApiPathConstantsV1.CirclesDefinitionsV1)]
    [AuthorizeValidAppToken]
    public class AppCircleDefinitionController : CircleDefinitionControllerBase
    {
        public AppCircleDefinitionController(
            CircleMembershipService circleMembershipService,
            CircleNetworkService cns) : base(cns, circleMembershipService)
        {
        }
    }
}