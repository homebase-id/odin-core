using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.DataSubscription;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.DataSubscription.ReceivingHost;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Follow;
using Refit;

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

        /// <summary />
        [HttpGet("IdentitiesIFollow")]
        public async Task<CursoredResult<string>> GetIdentitiesIFollow(int max, string cursor)
        {
            var result = await base.GetWhoIFollow(max, cursor);
            return result;
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
        
        [HttpPost("sync-feed-history")]
        public new async Task<IActionResult> SynchronizeFeedHistory([Body] SynchronizeFeedHistoryRequest request)
        {
            await base.SynchronizeFeedHistory(request);
            return Ok();
        }
    }
}