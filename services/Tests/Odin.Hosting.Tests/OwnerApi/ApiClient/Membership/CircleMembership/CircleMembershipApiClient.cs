using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Services.Membership.Circles;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.CircleMembership;

public class CircleMembershipApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public CircleMembershipApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }
    
    
    public async Task<CircleDefinition> CreateCircle(string circleName, PermissionSetGrantRequest grant)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

            var request = new CreateCircleRequest()
            {
                Id = Guid.NewGuid(),
                Name = circleName,
                Description = $"Description for {circleName}",
                DriveGrants = grant.Drives,
                Permissions = grant.PermissionSet
            };

            var createCircleResponse = await svc.CreateCircleDefinition(request);
            Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

            var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
            Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

            var definitionList = getCircleDefinitionsResponse.Content;
            Assert.IsNotNull(definitionList);

            var circle = definitionList.Single(c => c.Id == request.Id);

            foreach (var dgr in request.DriveGrants ?? new List<DriveGrantRequest>())
            {
                Assert.IsNotNull(circle.DriveGrants.SingleOrDefault(d => d == dgr));
            }

            // Ensure Circle has the keys matching the request.  so it's ok if the request was null
            foreach (var k in request.Permissions?.Keys ?? new List<int>())
            {
                Assert.IsTrue(circle.Permissions.HasKey(k));
            }

            Assert.AreEqual(request.Name, circle.Name);
            Assert.AreEqual(request.Description, circle.Description);
            Assert.IsTrue(request.Permissions == circle.Permissions);

            return circle;
        }
    }

    public async Task<ApiResponse<bool>> CreateCircleRaw(string circleName, PermissionSetGrantRequest grant)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

            var request = new CreateCircleRequest()
            {
                Id = Guid.NewGuid(),
                Name = circleName,
                Description = $"Description for {circleName}",
                DriveGrants = grant.Drives,
                Permissions = grant.PermissionSet
            };

            var createCircleResponse = await svc.CreateCircleDefinition(request);
            return createCircleResponse;
        }
    }

    public async Task<ApiResponse<List<CircleDomainResult>>> GetDomainsInCircle(Guid circleId)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ICircleMembershipOwnerHttpClient>(client, ownerSharedSecret);

            var response = await svc.GetDomainsInCircle(new GetCircleMembersRequest()
            {
                CircleId = circleId
            });
            
            return response;
        }
    }
}