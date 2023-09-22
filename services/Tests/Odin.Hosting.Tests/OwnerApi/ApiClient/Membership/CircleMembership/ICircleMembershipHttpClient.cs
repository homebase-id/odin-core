using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.CircleMembership
{
    public interface ICircleMembershipHttpClient
    {
        private const string RootPath = OwnerApiPathConstants.CirclesV1 + "/membership";

        [Post(RootPath + "/list")]
        Task<ApiResponse<List<CircleDomainResult>>> GetDomainsInCircle([Body] GetCircleMembersRequest request);
    }
}