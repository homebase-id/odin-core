using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.DataSubscription.Follower;

namespace Youverse.Hosting.Controllers.Base
{
    /// <summary />
    public class FollowerControllerBase : ControllerBase
    {
        private readonly FollowerService _followerService;

        /// <summary />
        protected FollowerControllerBase(FollowerService fs)
        {
            _followerService = fs;
        }

        /// <summary>
        /// Gets a list of identities I follow
        /// </summary>
        protected async Task<CursoredResult<string>> GetWhoIFollow(int max, string cursor)
        {
            var result = await _followerService.GetIdentitiesIFollow(max, cursor);
            return result;
        }


        /// <summary>
        /// Gets a list of identities I follow
        /// </summary>
        protected async Task<CursoredResult<string>> GetWhoIFollowByDrive(Guid driveAlias, int max, string cursor)
        {
            var result = await _followerService.GetIdentitiesIFollow(driveAlias, max, cursor);
            return result;
        }


        /// <summary>
        /// Gets a list of identities following me
        /// </summary>
        protected async Task<CursoredResult<string>> GetFollowers(int max, string cursor)
        {
            var result = await _followerService.GetAllFollowers(max, cursor);
            return result;
        }

        /// <summary>
        /// Returns the details of an identity that follows you
        /// </summary>
        /// <param name="odinId"></param>
        protected async Task<FollowerDefinition> GetFollower(string odinId)
        {
            return await _followerService.GetFollower(new OdinId(odinId));
        }

        /// <summary>
        /// Returns the details of an identity you're following
        /// </summary>
        protected async Task<FollowerDefinition> GetIdentityIFollow(string odinId)
        {
            return await _followerService.GetIdentityIFollow(new OdinId(odinId));
        }

        /// <summary>
        /// Follows an identity.  Can also be used to update the follower subscription.
        /// </summary>
        protected async Task<IActionResult> Follow([Body] FollowRequest request)
        {
            await _followerService.Follow(request);
            return NoContent();
        }

        /// <summary>
        /// Unfollows an identity
        /// </summary>
        protected async Task<IActionResult> Unfollow([Body] UnfollowRequest request)
        {
            await _followerService.Unfollow(new OdinId(request.OdinId));
            return NoContent();
        }
    }
}