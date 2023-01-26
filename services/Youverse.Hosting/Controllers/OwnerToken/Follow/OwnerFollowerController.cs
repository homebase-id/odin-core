﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Follower;

namespace Youverse.Hosting.Controllers.OwnerToken.Follow
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.CirclesV1 + "/followers")]
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
            var result = await _followerService.GetIdentitiesIFollow(cursor);
            return result;
        }

        /// <summary>
        /// Gets a list of identities following me
        /// </summary>
        /// <returns></returns>
        [HttpGet("followingme")]
        public async Task<CursoredResult<string>> GetFollowingMe(string cursor)
        {
            var result = await _followerService.GetFollowers(cursor);
            return result;
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
            await _followerService.Unfollow(request.DotYouId);
            return Ok();
        }
    }
}