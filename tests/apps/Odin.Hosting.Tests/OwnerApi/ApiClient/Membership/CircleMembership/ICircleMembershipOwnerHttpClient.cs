using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Odin.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.CircleMembership
{
    public interface ICircleMembershipOwnerHttpClient
    {
        private const string RootPath = OwnerApiPathConstants.CirclesV1 + "/membership";

        [Post(RootPath + "/list")]
        Task<ApiResponse<List<CircleDomainResult>>> GetDomainsInCircle([Body] GetCircleMembersRequest request);
    }
}