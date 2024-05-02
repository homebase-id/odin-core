using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.DataSubscription;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.DataSubscription.ReceivingHost;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Follow;
using Odin.Services.Base;
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
        public AppFollowerController(FollowerService fs, TenantSystemStorage tenantSystemStorage) : base(fs, tenantSystemStorage)
        {
        }
        
    }
}