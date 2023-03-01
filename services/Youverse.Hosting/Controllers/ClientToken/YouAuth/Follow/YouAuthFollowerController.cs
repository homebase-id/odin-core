using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
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
        /// <summary />
        public YouAuthFollowerController(FollowerService fs) : base(fs)
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