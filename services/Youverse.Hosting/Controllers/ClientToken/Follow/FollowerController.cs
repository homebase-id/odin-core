using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Follower;
using Youverse.Hosting.Controllers.Anonymous;

namespace Youverse.Hosting.Controllers.ClientToken.Follow
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/followers")]
    [Route(YouAuthApiPathConstants.CirclesV1 + "/followers")]
    [AuthorizeValidExchangeGrant]
    public class FollowerController : ControllerBase
    {
        private readonly FollowerService _followerService;

        /// <summary />
        public FollowerController(FollowerService fs)
        {
            _followerService = fs;
        }

        /// <summary />
        [HttpGet("list")]
        public async Task<CursoredResult<string>> GetFollowers()
        {
            // var result = await _followerService.GetFollowers()
            return null;
        }
        
        /// <summary />
        [HttpGet("list")]
        public async Task<CursoredResult<string>> GetFollowingMe()
        {
            // var result = await _followerService.GetFollowers()
            return null;
        }
    }
}