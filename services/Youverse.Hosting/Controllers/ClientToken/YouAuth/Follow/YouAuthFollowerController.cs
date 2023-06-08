using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.ClientToken.YouAuth.Follow
{
    /// <summary />
    [ApiController]
    [Route(YouAuthApiPathConstants.CirclesV1 + "/followers")]
    [AuthorizeValidExchangeGrant]
    public class YouAuthFollowerController : FollowerControllerBase
    {
        private readonly OdinContextAccessor _contextAccessor;
        /// <summary />
        public YouAuthFollowerController(FollowerService fs, OdinContextAccessor contextAccessor) : base(fs)
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