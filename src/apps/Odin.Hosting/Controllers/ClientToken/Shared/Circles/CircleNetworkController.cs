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
    [Route(AppApiPathConstants.CirclesV1 + "/connections")]
    [Route(GuestApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidGuestOrAppToken]
    public class CircleNetworkController(CircleNetworkService cn) : OdinControllerBase
    {
        /// <summary>
        /// Gets a list of connected identities
        /// </summary>
        /// <returns></returns>
        [HttpGet("connected")]
        public async Task<CursoredResult<RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int count, string cursor,
            bool omitContactData = false)
        {
            Int64.TryParse(cursor, out long c);
            var result = await cn.GetConnectedIdentitiesAsync(count, c, WebOdinContext);
            return new CursoredResult<RedactedIdentityConnectionRegistration>()
            {
                Cursor = result.Cursor,
                Results = result.Results.Select(p => p.Redacted(omitContactData)).ToList()
            };
        }
    }
}