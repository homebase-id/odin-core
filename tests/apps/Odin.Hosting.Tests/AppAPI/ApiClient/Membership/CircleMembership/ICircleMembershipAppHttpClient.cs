using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Membership.CircleMembership
{
    public interface ICircleMembershipAppHttpClient
    {
        private const string RootPath = AppApiPathConstantsV1.CirclesV1 + "/membership";

        [Post(RootPath + "/list")]
        Task<ApiResponse<List<CircleDomainResult>>> GetDomainsInCircle([Body] GetCircleMembersRequest request);
    }
}