using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Membership.Connections.t;

public class UniversalCircleNetworkApiClient(OdinId targetIdentity, IApiClientFactory factory)
{
    private readonly OdinId _identity = targetIdentity;
    private readonly IApiClientFactory _factory = factory;

    public async Task<ApiResponse<bool>> CreateCircle(Guid id, string circleName, PermissionSetGrantRequest grant)
    {
        var client = _factory.CreateHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleDefinition>(client, ownerSharedSecret);

            var request = new CreateCircleRequest()
            {
                Id = id,
                Name = circleName,
                Description = $"Description for {circleName}",
                DriveGrants = grant.Drives,
                Permissions = grant.PermissionSet
            };

            var createCircleResponse = await svc.CreateCircleDefinition(request);
            return createCircleResponse;
        }
    }

    public async Task<ApiResponse<CircleDefinition>> GetCircleDefinition(GuidId circleId)
    {
        var client = _factory.CreateHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleDefinition>(client, ownerSharedSecret);
            var response = await svc.GetCircleDefinition(circleId);
            return response;
        }
    }

    public async Task<ApiResponse<IEnumerable<CircleDefinition>>> GetCircleDefinitions(bool includeSystemCircle)
    {
        var client = _factory.CreateHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleDefinition>(client, ownerSharedSecret);
            var response = await svc.GetCircleDefinitions(includeSystemCircle);
            return response;
        }
    }

    public async Task<ApiResponse<bool>> DisconnectFrom(TestIdentity recipient)
    {
        var client = _factory.CreateHttpClient(_identity, out var ownerSharedSecret);
        {
            var disconnectResponse = await RefitCreator.RestServiceFor<IRefitUniversalOwnerCircleNetworkConnections>(client, ownerSharedSecret)
                .Disconnect(new OdinIdRequest() { OdinId = recipient.OdinId });
            return disconnectResponse;
        }
    }


    public async Task<ApiResponse<bool>> GrantCircle(Guid circleId, TestIdentity recipient)
    {
        var client = _factory.CreateHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.AddCircle(new AddCircleMembershipRequest()
            {
                CircleId = circleId,
                OdinId = recipient.OdinId
            });
            return apiResponse;
        }
    }

    public async Task<ApiResponse<bool>> RevokeCircle(Guid circleId, TestIdentity recipient)
    {
        var client = _factory.CreateHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.RevokeCircle(new RevokeCircleMembershipRequest()
            {
                CircleId = circleId,
                OdinId = recipient.OdinId
            });

            return apiResponse;
        }
    }

    public async Task<ApiResponse<IEnumerable<OdinId>>> GetCircleMembers(Guid circleId)
    {
        var client = _factory.CreateHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.GetCircleMembers(new GetCircleMembersRequest() { CircleId = circleId });
            return apiResponse;
        }
    }

    public async Task<ApiResponse<RedactedIdentityConnectionRegistration>> GetConnectionInfo(OdinId odinId)
    {
        var client = _factory.CreateHttpClient(_identity, out var ownerSharedSecret);
        {
            var connectionsService = RefitCreator.RestServiceFor<IRefitUniversalOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await connectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = odinId });

            Assert.IsTrue(apiResponse.IsSuccessStatusCode, $"Failed to get status for {odinId}.  Status code was {apiResponse.StatusCode}");
            Assert.IsNotNull(apiResponse.Content, $"No status for {odinId} found");
            return apiResponse;
        }
    }

    public async Task<ApiResponse<bool>> BlockConnection(TestIdentity recipient)
    {
        var client = _factory.CreateHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.Block(new OdinIdRequest() { OdinId = recipient.OdinId });
            return apiResponse;
        }
    }

    public async Task<ApiResponse<IcrVerificationResult>> VerifyConnection(OdinId recipient)
    {
        var client = _factory.CreateHttpClient(_identity, out var ownerSharedSecret);
        {
            var connectionsService = RefitCreator.RestServiceFor<IRefitUniversalOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await connectionsService.VerifyConnection(new OdinIdRequest() { OdinId = recipient });
            return apiResponse;
        }
    }
}