using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.DataSubscription.Follower;

namespace Youverse.Hosting.Controllers.ClientToken.App.Follow
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/followers")]
    [AuthorizeValidAppExchangeGrant]
    public class AppFollowerController : ControllerBase
    {
        private readonly FollowerService _followerService;

        /// <summary />
        public AppFollowerController(FollowerService fs)
        {
            _followerService = fs;
        }

        /// <summary />
        [HttpGet("IdentitiesIFollow")]
        public async Task<CursoredResult<string>> GetIdentitiesIFollow(string cursor)
        {
            var result = await _followerService.GetIdentitiesIFollow(cursor);
            return result;
        }
    }
}