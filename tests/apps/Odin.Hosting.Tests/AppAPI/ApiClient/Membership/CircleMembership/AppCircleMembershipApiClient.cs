using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.ClientToken.App.Membership.Connections;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;
using Odin.Hosting.Tests.AppAPI.ApiClient.Follower;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.CircleMembership;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Membership.CircleMembership;

public class AppCircleMembershipApiClient : AppApiClientBase
{
    private readonly AppClientToken _token;

    public AppCircleMembershipApiClient(OwnerApiTestUtils ownerApiTestUtils, AppClientToken token) : base(ownerApiTestUtils)
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