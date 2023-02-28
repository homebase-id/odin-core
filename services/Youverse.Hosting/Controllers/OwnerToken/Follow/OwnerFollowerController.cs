using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.DataSubscription.Follower;

namespace Youverse.Hosting.Controllers.OwnerToken.Follow
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.FollowersV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerFollowerController : ControllerBase
    {
        private readonly FollowerService _followerService;

        /// <summary />
        public OwnerFollowerController(FollowerService fs)
        {
            _followerService = fs;
        }

        /// <summary>
        /// Gets a list of identities I follow
        /// </summary>
        /// <param name="cursor"></param>
        /// <returns></returns>
        [HttpGet("IdentitiesIFollow")]
        public async Task<CursoredResult<string>> GetIdentitiesIFollow(string cursor)
        {
            var (result, nextCursor) = await _followerService.GetIdentitiesIFollow(cursor);
            // TODO: You need to do something with the cursor here
            return result;
        }

        /// <summary>
        /// Gets a list of identities following me
        /// </summary>
        /// <returns></returns>
        [HttpGet("followingme")]
        public async Task<CursoredResult<string>> GetIdentitiesFollowingMe(string cursor)
        {
            var (result, nextCursor) = await _followerService.GetFollowers(cursor);
            // TODO: You need to do something with the cursor here
            return result;
        }

        /// <summary>
        /// Returns the details of an identity that follows you
        /// </summary>
        /// <param name="odinId"></param>
        /// <returns></returns>
        [HttpGet("follower")]
        public async Task<FollowerDefinition> GetFollower(string odinId)
        {
            return await _followerService.GetFollower(new OdinId(odinId));
        }
        
        /// <summary>
        /// Returns the details of an identity you're following
        /// </summary>
        [HttpGet("IdentityIFollow")]
        public async Task<FollowerDefinition> GetIdentityIFollow(string odinId)
        {
            return await _followerService.GetIdentityIFollow(new OdinId(odinId));
        }

        /// <summary>
        /// Follows an identity.  Can also be used to update the follower
        /// subscription.
        /// </summary>
        [HttpPost("follow")]
        public async Task<IActionResult> Follow([Body] FollowRequest request)
        {
            await _followerService.Follow(request);
            return Ok();
        }

        /// <summary>
        /// Unfollows an identity
        /// </summary>
        [HttpPost("unfollow")]
        public async Task<IActionResult> Unfollow([Body] UnfollowRequest request)
        {
            await _followerService.Unfollow(new OdinId(request.OdinId));
            return Ok();
        }
    }
}