using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.DataSubscription.Follower;
using Odin.Hosting.Controllers.Base.Follow;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Follow
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.FollowersV1)]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerFollowerController : FollowerControllerBase
    {
        /// <summary />
        public OwnerFollowerController(FollowerService fs) : base(fs)
        {
        }
        
    }
}