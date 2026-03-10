using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.DataSubscription.Follower;
using Odin.Hosting.Controllers.Base.Follow;
using Odin.Services.Base;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Hosting.Controllers.OwnerToken.Follow
{
    /// <summary />
    [ApiController]
    [Route(OwnerApiPathConstants.FollowersV1)]
    [AuthorizeValidOwnerToken]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerFollowerController : FollowerControllerBase
    {
        private readonly IdentityDatabase _db;

        /// <summary />
        public OwnerFollowerController(FollowerService fs, IdentityDatabase db) : base(fs)
        {
            _db = db;
        }

        /// <summary>
        /// Testing endpoint: Returns all subscription records for the current identity (what this identity is subscribed to).
        /// </summary>
        [HttpGet("my-subscriptions")]
        [AuthorizeValidOwnerToken]
        public async Task<List<MySubscriptionsRecord>> GetMySubscriptions()
        {
            return await _db.MySubscriptionsCached.GetAllAsync();
        }

        /// <summary>
        /// Testing endpoint: Returns all subscriber records for the current identity (who is subscribed to this identity's drives).
        /// </summary>
        [HttpGet("my-subscribers")]
        [AuthorizeValidOwnerToken]
        public async Task<List<MySubscribersRecord>> GetMySubscribers()
        {
            return await _db.MySubscribersCached.GetAllAsync();
        }

    }
}