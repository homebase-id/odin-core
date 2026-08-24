using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Circles
{
    [ApiController]
    [Route(AppApiPathConstantsV1.CirclesV1 + "/connections")]
    [Route(GuestApiPathConstantsV1.CirclesV1 + "/connections")]
    [AuthorizeValidGuestOrAppToken]
    public class CircleNetworkController(CircleNetworkService cn) : OdinControllerBase
    {
        /// <summary>
        /// Gets a list of connected identities
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// This controller is mounted on both the app route and the guest route, so the shape is chosen
        /// by who is asking, not by which path they came in on.  App callers are the owner's own clients
        /// and get the full record; a guest -- including an anonymous viewer, where the tenant setting
        /// permits it -- gets identities and their public contact cards only.  The setting decides
        /// whether a third party sees the list at all; this decides what is in it.
        /// </remarks>
        [HttpGet("connected")]
        public async Task<CursoredResult<RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int count, string cursor,
            bool omitContactData = false)
        {
            var result = await cn.GetConnectedIdentitiesAsync(count, cursor, WebOdinContext);

            var isOwnerViewer = WebOdinContext.Caller.IsOwner;

            return new CursoredResult<RedactedIdentityConnectionRegistration>()
            {
                Cursor = result.Cursor,
                Results = result.Results
                    .Select(p => isOwnerViewer ? p.Redacted(omitContactData) : p.RedactedForThirdParty())
                    .ToList()
            };
        }
    }
}