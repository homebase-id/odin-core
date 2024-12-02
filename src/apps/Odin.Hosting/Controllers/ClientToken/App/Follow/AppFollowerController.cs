using Microsoft.AspNetCore.Mvc;
using Odin.Services.DataSubscription.Follower;
using Odin.Hosting.Controllers.Base.Follow;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Follow
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.FollowersV1)]
    [AuthorizeValidAppToken]
    public class AppFollowerController : FollowerControllerBase
    {
        /// <summary />
        public AppFollowerController(FollowerService fs) : base(fs)
        {
        }
        
    }
}