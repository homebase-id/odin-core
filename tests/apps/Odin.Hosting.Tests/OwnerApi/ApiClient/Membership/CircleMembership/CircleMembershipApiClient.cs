using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
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
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

            var request = new CreateCircleRequest()
            {
                Id = Guid.NewGuid(),
                Name = circleName,
                Description = $"Description for {circleName}",
                DriveGrants = grant.Drives,
                Permissions = grant.PermissionSet
            };

            var createCircleResponse = await svc.CreateCircleDefinition(request);
            ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

            var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
            ClassicAssert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

            var definitionList = getCircleDefinitionsResponse.Content;
            ClassicAssert.IsNotNull(definitionList);

            var circle = definitionList.Single(c => c.Id == request.Id);

            foreach (var dgr in request.DriveGrants ?? new List<DriveGrantRequest>())
            {
                ClassicAssert.IsNotNull(circle.DriveGrants.SingleOrDefault(d => d == dgr));
            }

            // Ensure Circle has the keys matching the request.  so it's ok if the request was null
            foreach (var k in request.Permissions?.Keys ?? new List<int>())
            {
                ClassicAssert.IsTrue(circle.Permissions.HasKey(k));
            }

            ClassicAssert.AreEqual(request.Name, circle.Name);
            ClassicAssert.AreEqual(request.Description, circle.Description);
            ClassicAssert.IsTrue(request.Permissions == circle.Permissions);

            return circle;
        }
    }

    public async Task<ApiResponse<bool>> CreateCircleRaw(string circleName, PermissionSetGrantRequest grant)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

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

    public async Task<ApiResponse<CircleDefinition>> GetCircleDefinition(GuidId circleId)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);
            var response = await svc.GetCircleDefinition(circleId);
            return response;
        }
    }

    public async Task<ApiResponse<IEnumerable<CircleDefinition>>> GetCircleDefinitions(bool includeSystemCircle)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);
            var response = await svc.GetCircleDefinitions(includeSystemCircle);
            return response;
        }
    }

    public async Task<CircleDefinition> CreateCircle(string name, TargetDrive drive, DrivePermission drivePermission, params int[] permissionKeys)
    {
        return await this.CreateCircle(name, new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = drive,
                        Permission = drivePermission
                    }
                }
            },
            PermissionSet = new PermissionSet(permissionKeys)
        });
    }
}