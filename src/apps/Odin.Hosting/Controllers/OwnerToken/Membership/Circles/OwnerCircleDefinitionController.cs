using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.Base.Membership.Circles;
using Odin.Services.Base;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.OwnerToken.Membership.Circles
{
    
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesDefinitionsV1)]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerCircleDefinitionController : CircleDefinitionControllerBase
    {
        private readonly CircleNetworkService _cns;

        public OwnerCircleDefinitionController(
            CircleMembershipService circleMembershipService,
            CircleNetworkService cns) : base(cns, circleMembershipService)
        {
            _cns = cns;
        }

        /// <summary>
        /// Hands a circle that belongs to no app to one that does exist.
        /// </summary>
        /// <remarks>
        /// Owner console only, which is why it is here and not on
        /// <see cref="CircleDefinitionControllerBase"/> where the app controller would inherit it.  An
        /// app reaching this could hand itself a circle, so the route simply does not exist for apps;
        /// the service re-checks rather than relying on that.
        /// <para>
        /// One way.  A circle that already names an app is refused, not moved.
        /// </para>
        /// </remarks>
        [HttpPost("set-owner")]
        public async Task<bool> SetCircleOwningApp([FromBody] SetCircleOwningAppRequest request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertNotEmptyGuid(request.CircleId, nameof(request.CircleId));
            OdinValidationUtils.AssertNotEmptyGuid(request.AppId, nameof(request.AppId));

            await _cns.SetCircleOwningAppAsync(new GuidId(request.CircleId), request.AppId, WebOdinContext);
            return true;
        }

        /// <summary>
        /// Moves a circle from the app that owns it to another.  The escape hatch, not the ordinary
        /// path -- see <see cref="SetCircleOwningApp"/>, which refuses an already-owned circle.
        /// </summary>
        /// <remarks>
        /// Master key required, so this is the owner console and nothing else: an app is the owner
        /// acting, and no app should be able to move a circle to itself.
        /// <para>
        /// Pending enrollments queued against the circle are re-pointed at the new app in the same
        /// transaction, and the count comes back so a caller can say what moved.  Without that they
        /// would be claimed by nobody -- the processing pass filters on the copy each entry carries.
        /// </para>
        /// </remarks>
        [HttpPost("reassign-owner")]
        public async Task<ReassignCircleOwningAppResult> ReassignCircleOwningApp(
            [FromBody] SetCircleOwningAppRequest request)
        {
            OdinValidationUtils.AssertNotNull(request, nameof(request));
            OdinValidationUtils.AssertNotEmptyGuid(request.CircleId, nameof(request.CircleId));
            OdinValidationUtils.AssertNotEmptyGuid(request.AppId, nameof(request.AppId));

            var repointed = await _cns.ReassignCircleOwningAppAsync(new GuidId(request.CircleId), request.AppId,
                WebOdinContext);

            return new ReassignCircleOwningAppResult { EnrollmentsRepointed = repointed };
        }
    }

    public class ReassignCircleOwningAppResult
    {
        /// <summary>Pending enrollments re-pointed at the new app.  Zero is normal.</summary>
        public int EnrollmentsRepointed { get; set; }
    }

    public class SetCircleOwningAppRequest
    {
        /// <summary>The circle to adopt.  Must currently belong to no app.</summary>
        public Guid CircleId { get; set; }

        /// <summary>The app to hand it to.  Must name a registered app.</summary>
        public Guid AppId { get; set; }
    }
}
