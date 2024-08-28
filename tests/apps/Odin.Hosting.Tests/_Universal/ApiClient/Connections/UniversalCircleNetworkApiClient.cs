using System;
using System.Collections.Generic;
using System.Net.Http;
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

namespace Odin.Hosting.Tests._Universal.ApiClient.Connections;

public class UniversalCircleNetworkApiClient(OdinId identity, IApiClientFactory factory)
{
    public async Task<ApiResponse<HttpContent>> CreateCircle(Guid id, string circleName, PermissionSetGrantRequest grant)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
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
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleDefinition>(client, ownerSharedSecret);
            var response = await svc.GetCircleDefinition(circleId);
            return response;
        }
    }

    public async Task<ApiResponse<IEnumerable<CircleDefinition>>> GetCircleDefinitions(bool includeSystemCircle)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleDefinition>(client, ownerSharedSecret);
            var response = await svc.GetCircleDefinitions(includeSystemCircle);
            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> GrantCircle(Guid circleId, TestIdentity recipient)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.AddCircle(new AddCircleMembershipRequest()
            {
                CircleId = circleId,
                OdinId = recipient.OdinId
            });

            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> RevokeCircle(Guid circleId, TestIdentity recipient)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
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
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.GetCircleMembers(new GetCircleMembersRequest() { CircleId = circleId });
            return apiResponse;
        }
    }

    public async Task<ApiResponse<RedactedIdentityConnectionRegistration>> GetConnectionInfo(OdinId recipient)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var connectionsService = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await connectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = recipient });

            Assert.IsTrue(apiResponse.IsSuccessStatusCode, $"Failed to get status for {recipient}.  Status code was {apiResponse.StatusCode}");
            Assert.IsNotNull(apiResponse.Content, $"No status for {recipient} found");
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> BlockConnection(TestIdentity recipient)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.Block(new OdinIdRequest() { OdinId = recipient.OdinId });
            return apiResponse;
        }
    }
    
    public async Task<ApiResponse<HttpContent>> DisconnectFrom(OdinId recipient)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var disconnectResponse = await RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret)
                .Disconnect(new OdinIdRequest() { OdinId = recipient });
            return disconnectResponse;
        }
    }
    
    public async Task<ApiResponse<IcrVerificationResult>> VerifyConnection(OdinId recipient)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var connectionsService = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await connectionsService.VerifyConnection(new OdinIdRequest() { OdinId = recipient });
            return apiResponse;
        }
    }
}