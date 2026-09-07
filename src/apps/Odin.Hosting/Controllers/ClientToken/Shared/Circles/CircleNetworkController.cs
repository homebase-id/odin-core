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
        /// <remarks>
        /// Mounted on both the app route and the guest route, and a guest reaches it holding nothing but
        /// <c>ReadConnections</c> -- which the tenant setting can grant to anonymous viewers.  Permission to
        /// see who the owner is connected to is not permission to see what the owner thinks of them, so a
        /// third-party viewer gets identities and public contact cards only.
        /// </remarks>
        [HttpGet("connected")]
        public async Task<CursoredResult<RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int count, string cursor,
            bool omitContactData = false)
        {
            var result = await cn.GetConnectedIdentitiesAsync(count, cursor, WebOdinContext);
            var ownerSide = CallerIsOwnerSideViewer;

            return new CursoredResult<RedactedIdentityConnectionRegistration>()
            {
                Cursor = result.Cursor,
                Results = result.Results
                    .Select(p => ownerSide ? p.Redacted(omitContactData) : p.RedactedForExternalViewer())
                    .ToList()
            };
        }
    }
}