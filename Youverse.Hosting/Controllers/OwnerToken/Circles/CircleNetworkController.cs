using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Notification;
using NotImplementedException = System.NotImplementedException;

namespace Youverse.Hosting.Controllers.OwnerToken.Circles
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidOwnerToken]
    public class CircleNetworkController : ControllerBase
    {
        private readonly ICircleNetworkService _circleNetwork;
        private readonly CircleNetworkNotificationService _circleNetworkNotificationService;

        public CircleNetworkController(ICircleNetworkService cn, CircleNetworkNotificationService circleNetworkNotificationService)
        {
            _circleNetwork = cn;
            _circleNetworkNotificationService = circleNetworkNotificationService;
        }

        [HttpPost("unblock")]
        public async Task<bool> Unblock([FromBody] DotYouIdRequest request)
        {
            var result = await _circleNetwork.Unblock((DotYouIdentity)request.DotYouId);
            return result;
        }

        [HttpPost("block")]
        public async Task<bool> Block([FromBody] DotYouIdRequest request)
        {
            var result = await _circleNetwork.Block((DotYouIdentity)request.DotYouId);
            return result;
        }

        [HttpPost("disconnect")]
        public async Task<bool> Disconnect([FromBody] DotYouIdRequest request)
        {
            var result = await _circleNetwork.Disconnect((DotYouIdentity)request.DotYouId);
            return result;
        }

        //[HttpPost("notify")]
        // public async Task<IActionResult> NotifyConnections(CircleNetworkNotification notification)
        // {
        //     await _circleNetworkNotificationService.NotifyConnections(notification);
        //     return Ok();
        // }

        [HttpPost("status")]
        public async Task<RedactedIdentityConnectionRegistration> GetConnectionInfo([FromBody] DotYouIdRequest request, bool omitContactData = true)
        {
            var result = await _circleNetwork.GetIdentityConnectionRegistration((DotYouIdentity)request.DotYouId);
            return result?.Redacted(omitContactData);
        }

        [HttpPost("connected")]
        public async Task<PagedResult<RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int pageNumber, int pageSize, bool omitContactData = true)
        {
            var result = await _circleNetwork.GetConnectedIdentities(new PageOptions(pageNumber, pageSize));
            return RedactIcr(result, omitContactData);
        }

        [HttpPost("blocked")]
        public async Task<PagedResult<RedactedIdentityConnectionRegistration>> GetBlockedProfiles(int pageNumber, int pageSize, bool omitContactData = true)
        {
            var result = await _circleNetwork.GetBlockedProfiles(new PageOptions(pageNumber, pageSize));
            return RedactIcr(result, omitContactData);
        }

        [HttpPost("circles/list")]
        public async Task<IEnumerable<DotYouIdentity>> GetCircleMembers([FromBody] GetCircleMembersRequest request)
        {
            var result = await _circleNetwork.GetCircleMembers(request.CircleId);
            return result;
        }

        [HttpPost("circles/add")]
        public async Task<bool> AddCircle([FromBody] AddCircleMembershipRequest request)
        {
            await _circleNetwork.GrantCircle(request.CircleId, new DotYouIdentity(request.DotYouId));
            return true;
        }

        [HttpPost("circles/revoke")]
        public async Task<bool> RevokeCircle([FromBody] RevokeCircleMembershipRequest request)
        {
            await _circleNetwork.RevokeCircleAccess(request.CircleId, new DotYouIdentity(request.DotYouId));
            return true;
        }

        private PagedResult<RedactedIdentityConnectionRegistration> RedactIcr(PagedResult<IdentityConnectionRegistration> page, bool omitContactData)
        {
            return new PagedResult<RedactedIdentityConnectionRegistration>()
            {
                Request = page.Request,
                TotalPages = page.TotalPages,
                Results = page.Results.Select(c => c.Redacted(omitContactData)).ToList()
            };
        }
    }
}