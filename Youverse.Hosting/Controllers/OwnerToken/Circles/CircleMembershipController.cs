using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Notification;

namespace Youverse.Hosting.Controllers.OwnerToken.Circles
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/membership")]
    [AuthorizeValidOwnerToken]
    public class CircleMembershipController : ControllerBase
    {
        private readonly ICircleNetworkService _circleNetwork;
        private readonly CircleMembershipService _circleMembershipService;
        private readonly CircleNetworkNotificationService _circleNetworkNotificationService;

        public CircleMembershipController(ICircleNetworkService cn, CircleNetworkNotificationService circleNetworkNotificationService, CircleMembershipService circleMembershipService)
        {
            _circleNetwork = cn;
            _circleNetworkNotificationService = circleNetworkNotificationService;
            _circleMembershipService = circleMembershipService;
        }

        [HttpPost("add")]
        public async Task<bool> AddMembers([FromBody] AddCircleMembershipRequest request)
        {
            await _circleMembershipService.AddCircleMember(request.CircleId, request.DotYouIdList.Select(id => new DotYouIdentity(id)));
            return true;
        }

        [HttpPost("remove")]
        public async Task<bool> RemoveMembers([FromBody] RemoveCircleMembershipRequest request)
        {
            await _circleMembershipService.RemoveCircleMember(request.CircleId, request.DotYouIdList.Select(id => new DotYouIdentity(id)));
            return true;
        }
        
        [HttpPost("list")]
        public async Task<IEnumerable<DotYouIdentity>> GetMembers([FromBody] ByteArrayId circleId)
        {
            var result = await _circleMembershipService.GetMembers(circleId);
            return result;
        }
    }
}