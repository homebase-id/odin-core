using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.ClientToken.App.Follow
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.CirclesV1 + "/followers")]
    [AuthorizeValidAppExchangeGrant]
    public class AppFollowerController : FollowerControllerBase
    {
        /// <summary />
        public AppFollowerController(FollowerService fs) : base(fs)
        {
        }

        /// <summary />
        [HttpGet("IdentitiesIFollow")]
        public async Task<CursoredResult<string>> GetIdentitiesIFollow(int max, string cursor)
        {
            var result = await base.GetWhoIFollow(max, cursor);
            return result;
        }
    }
}