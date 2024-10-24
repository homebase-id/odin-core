﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Circles
{
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/connections")]
    [Route(GuestApiPathConstants.CirclesV1 + "/connections")]
    [AuthorizeValidGuestOrAppToken]
    public class CircleNetworkController : OdinControllerBase
    {
        private readonly CircleNetworkService _circleNetwork;
        private readonly TenantSystemStorage _tenantSystemStorage;

        public CircleNetworkController(CircleNetworkService cn, TenantSystemStorage tenantSystemStorage)
        {
            _circleNetwork = cn;
            _tenantSystemStorage = tenantSystemStorage;
        }

        /// <summary>
        /// Gets a list of connected identities
        /// </summary>
        /// <returns></returns>
        [HttpGet("connected")]
        public async Task<CursoredResult<long, RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int count, long cursor,
            bool omitContactData = false)
        {
            var result = await _circleNetwork.GetConnectedIdentities(count, cursor, WebOdinContext);
            return new CursoredResult<long, RedactedIdentityConnectionRegistration>()
            {
                Cursor = result.Cursor,
                Results = result.Results.Select(p => p.Redacted(omitContactData)).ToList()
            };
        }
    }
}