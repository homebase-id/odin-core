using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;
using Odin.Hosting.Tests.AppAPI.ApiClient.Membership.CircleMembership;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Membership.Connections;

public class AppCircleNetworkRequestsApiClient : AppApiClientBase
{
    private readonly AppClientToken _token;

    public AppCircleNetworkRequestsApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
    {
        _token = token;
    }
    
    public async Task<ApiResponse<List<CircleDomainResult>>> GetDomainsInCircle(Guid circleId)
    {
        var client = CreateAppApiHttpClient(_token);
        {
            var svc = RefitCreator.RestServiceFor<ICircleMembershipAppHttpClient>(client,  _token.SharedSecret);

            var response = await svc.GetDomainsInCircle(new GetCircleMembersRequest()
            {
                CircleId = circleId
            });
            
            return response;
        }
    }
}