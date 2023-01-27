using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Follower;
using Youverse.Hosting.Controllers.Anonymous;

namespace Youverse.Hosting.Controllers.ClientToken.YouAuth.Follow
{
    /// <summary />
    [ApiController]
    [Route(YouAuthApiPathConstants.CirclesV1 + "/followers")]
    [AuthorizeValidExchangeGrant]
    public class YouAuthFollowerController : ControllerBase
    {
        private readonly FollowerService _followerService;

        /// <summary />
        public YouAuthFollowerController(FollowerService fs)
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