using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.OwnerToken.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Membership.CircleMembership
{
    public interface ICircleMembershipAppHttpClient
    {
        private const string RootPath = AppApiPathConstants.CirclesV1 + "/membership";

        [Post(RootPath + "/list")]
        Task<ApiResponse<List<CircleDomainResult>>> GetDomainsInCircle([Body] GetCircleMembersRequest request);
    }
}