using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Services.Base;
using Odin.Services.Membership.Connections.Verification;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.Connections
{
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerCircleNetworkController(CircleNetworkService cn, CircleNetworkVerificationService verificationService)
        : CircleNetworkControllerBase(cn, verificationService)
    {
        private readonly CircleNetworkService _cn = cn;

        //
        // Owner console only, so declared here rather than on CircleNetworkControllerBase --
        // AppCircleNetworkController inherits that base, and both of these require the master key an
        // app does not have. On the base they would be routes that exist and can never succeed, which
        // is the thing an API should not do: the service would refuse an app anyway, but a 403 on a
        // published route reads as "you lack permission" rather than "this was never yours to call".
        //

        /// <summary>
        /// Per circle owned by an app, the connections that could be added to it but are not in it.
        /// </summary>
        /// <remarks>
        /// Feeds the offer on the app's own page.  A circle assigned to an app does not reach back
        /// over contacts the owner already reviewed -- a review is a moment, not a standing rule --
        /// so this is the backlog that would otherwise be invisible.
        /// </remarks>
        [HttpGet("circles/enrollment-candidates")]
        public async Task<IEnumerable<CircleEnrollmentCandidates>> GetEnrollmentCandidates([FromQuery] Guid appId)
        {
            OdinValidationUtils.AssertNotEmptyGuid(appId, nameof(appId));
            return await _cn.GetEnrollmentCandidatesForAppAsync(appId, WebOdinContext);
        }

        /// <summary>
        /// Adds several identities to one circle in a single call.
        /// </summary>
        /// <remarks>
        /// Server-side rather than a client loop so that fourteen contacts is one round trip, and so
        /// one contact who stopped qualifying does not abort the rest.  Eligibility is re-checked per
        /// identity: the list comes from a view that may be seconds old.
        /// </remarks>
        [HttpPost("circles/add-many")]
        public async Task<EnrollmentResult> GrantCircleToMany([FromBody] AddManyCircleMembershipRequest request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertNotEmptyGuid(request.CircleId, nameof(request.CircleId));

            var odinIds = (request.OdinIds ?? []).Select(id => new OdinId(id)).ToList();
            return await _cn.EnrollManyInCircleAsync(new GuidId(request.CircleId), odinIds, WebOdinContext);
        }
    }
}
