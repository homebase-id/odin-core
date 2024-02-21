using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Hosting.Controllers.Base.Follow;

namespace Odin.Hosting.Controllers.OwnerToken.Follow
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.FollowersV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerFollowerController : FollowerControllerBase
    {
        /// <summary />
        public OwnerFollowerController(FollowerService fs) : base(fs)
        {
        }
        
    }
}