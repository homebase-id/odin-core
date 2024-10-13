using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Refit;

namespace Odin.Hosting.Controllers.Base.Follow
{
    /// <summary />
    public abstract class FollowerControllerBase : OdinControllerBase
    {
        private readonly FollowerService _followerService;
        private readonly TenantSystemStorage _tenantSystemStorage;


        /// <summary />
        protected FollowerControllerBase(FollowerService fs, TenantSystemStorage tenantSystemStorage)
        {
            _followerService = fs;
            _tenantSystemStorage = tenantSystemStorage;
        }

        /// <summary>
        /// Gets a list of identities I follow
        /// </summary>
        [HttpGet("IdentitiesIFollow")]
        public async Task<CursoredResult<string>> GetWhoIFollow(int max, string cursor)
        {
            var result = await _followerService.GetIdentitiesIFollow(max, cursor, WebOdinContext);
            return result;
        }


        /// <summary>
        /// Gets a list of identities I follow
        /// </summary>
        protected async Task<CursoredResult<string>> GetWhoIFollowByDrive(Guid driveAlias, int max, string cursor, IdentityDatabase db)
        {
            var result = await _followerService.GetIdentitiesIFollow(driveAlias, max, cursor, WebOdinContext, db);
            return result;
        }

        /// <summary>
        /// Gets a list of identities following me
        /// </summary>
        /// <summary>
        /// Gets a list of identities following me
        /// </summary>
        /// <returns></returns>
        [HttpGet("followingme")]
        public async Task<CursoredResult<string>> GetFollowers(int max, string cursor)
        {
            var result = await _followerService.GetAllFollowers(max, cursor, WebOdinContext);
            return result;
        }

        /// <summary>
        /// Returns the details of an identity that follows you
        /// </summary>
        /// <param name="odinId"></param>
        [HttpGet("follower")]
        public async Task<FollowerDefinition> GetFollower(string odinId)
        {
            AssertIsValidOdinId(odinId, out var id);
            return await _followerService.GetFollower(id, WebOdinContext);
        }

        /// <summary>
        /// Returns the details of an identity you're following
        /// </summary>
        [HttpGet("IdentityIFollow")]
        public async Task<FollowerDefinition> GetIdentityIFollow(string odinId)
        {
            AssertIsValidOdinId(odinId, out var id);

            var result = await _followerService.GetIdentityIFollow(id, WebOdinContext);
            return result;
        }

        /// <summary>
        /// Follows an identity.  Can also be used to update the follower subscription.
        /// </summary>
        [HttpPost("follow")]
        public async Task<IActionResult> Follow([Body] FollowRequest request)
        {
            AssertIsValidOdinId(request.OdinId, out var _);
            var db = _tenantSystemStorage.IdentityDatabase;
            await _followerService.Follow(request, WebOdinContext, db);
            return NoContent();
        }

        /// <summary>
        /// Unfollows an identity
        /// </summary>
        [HttpPost("unfollow")]
        public async Task<IActionResult> Unfollow([Body] UnfollowRequest request)
        {
            AssertIsValidOdinId(request.OdinId, out var _);
            await _followerService.Unfollow(new OdinId(request.OdinId), WebOdinContext);
            return NoContent();
        }

        [HttpPost("sync-feed-history")]
        public async Task SynchronizeFeedHistory(SynchronizeFeedHistoryRequest request)
        {
            AssertIsValidOdinId(request.OdinId, out var id);
            var db = _tenantSystemStorage.IdentityDatabase;
            await _followerService.SynchronizeChannelFiles(id, WebOdinContext, db);
        }
    }
}