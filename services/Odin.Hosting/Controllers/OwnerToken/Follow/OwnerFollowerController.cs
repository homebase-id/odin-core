using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.OwnerToken.Follow
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

        /// <summary>
        /// Gets a list of identities I follow
        /// </summary>
        [HttpGet("IdentitiesIFollow")]
        public async Task<CursoredResult<string>> GetIdentitiesIFollow(int max, string cursor)
        {
            return await base.GetWhoIFollow(max, cursor);
        }

        /// <summary>
        /// Gets a list of identities following me
        /// </summary>
        /// <returns></returns>
        [HttpGet("followingme")]
        public async Task<CursoredResult<string>> GetIdentitiesFollowingMe(int max, string cursor)
        {
            return await base.GetFollowers(max, cursor);
        }

        /// <summary>
        /// Returns the details of an identity that follows you
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        [HttpGet("follower")]
        public new async Task<FollowerDefinition> GetFollower(string odinId)
        {
            return await base.GetFollower(new OdinId(odinId));
        }

        /// <summary>
        /// Returns the details of an identity you're following
        /// </summary>
        [HttpGet("IdentityIFollow")]
        public new async Task<FollowerDefinition> GetIdentityIFollow(string odinId)
        {
            return await base.GetIdentityIFollow(new OdinId(odinId));
        }

        /// <summary>
        /// Follows an identity.  Can also be used to update the follower
        /// subscription.
        /// </summary>
        [HttpPost("follow")]
        public new async Task<IActionResult> Follow([Body] FollowRequest request)
        {
            return await base.Follow(request);
        }

        /// <summary>
        /// Unfollows an identity
        /// </summary>
        [HttpPost("unfollow")]
        public new async Task<IActionResult> Unfollow([Body] UnfollowRequest request)
        {
            return await base.Unfollow(request);
        }
    }
}