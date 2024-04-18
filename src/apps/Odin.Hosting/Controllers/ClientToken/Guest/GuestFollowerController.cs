using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Services.DataSubscription.Follower;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Follow;
using Odin.Hosting.Controllers.ClientToken.Shared;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.Guest
{
    /// <summary />
    [ApiController]
    [Route(GuestApiPathConstants.FollowersV1)]
    [AuthorizeValidGuestOrAppToken]
    public class GuestFollowerController : FollowerControllerBase
    {
        /// <summary />
        public GuestFollowerController(FollowerService fs) : base(fs)
        {
        }

        /// <summary />

        
        /// <summary>
        /// Returns information indicating if the authenticated identity follows the current tenant
        /// </summary>
        /// <returns></returns>
        [HttpGet("FollowerConfiguration")]
        public async Task<object> GetFollowerConfig()
        {
            var follower = await base.GetFollower(WebOdinContext.Caller.OdinId);
            return follower;
        }

    }
}