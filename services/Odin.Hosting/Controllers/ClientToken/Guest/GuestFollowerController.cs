using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Follow;
using Odin.Hosting.Controllers.ClientToken.Shared;

namespace Odin.Hosting.Controllers.ClientToken.Guest
{
    /// <summary />
    [ApiController]
    [Route(GuestApiPathConstants.CirclesV1 + "/followers")]
    [AuthorizeValidGuestOrAppToken]
    public class GuestFollowerController : FollowerControllerBase
    {
        private readonly OdinContextAccessor _contextAccessor;
        /// <summary />
        public GuestFollowerController(FollowerService fs, OdinContextAccessor contextAccessor) : base(fs)
        {
            _contextAccessor = contextAccessor;
        }

        /// <summary />
        [HttpGet("IdentitiesIFollow")]
        public async Task<CursoredResult<string>> GetIdentitiesIFollow(int max, string cursor)
        {
            var result = await base.GetWhoIFollow(max, cursor);
            return result;
        }
        
        /// <summary>
        /// Returns information indicating if the authenticated identity follows the current tenant
        /// </summary>
        /// <returns></returns>
        [HttpGet("FollowerConfiguration")]
        public async Task<object> GetFollowerConfig()
        {
            var follower = await base.GetFollower(_contextAccessor.GetCurrent().Caller.OdinId);
            return follower;
        }

    }
}