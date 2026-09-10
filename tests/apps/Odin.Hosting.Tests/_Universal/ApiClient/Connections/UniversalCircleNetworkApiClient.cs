using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers.OwnerToken.Membership.Circles;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Connections;

public class UniversalCircleNetworkApiClient(OdinId identity, IApiClientFactory factory)
{
    /// <param name="appId">
    /// Owning app. Null makes an owner circle, which is what most tests want; set it to exercise the
    /// behaviour that keys off circle ownership, such as which app may complete a pending enrollment.
    /// </param>
    public async Task<ApiResponse<HttpContent>> CreateCircle(Guid id, string circleName, PermissionSetGrantRequest grant,
        Guid? appId = null)
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
                Permissions = grant.PermissionSet,
                AppId = appId
            };

            var createCircleResponse = await svc.CreateCircleDefinition(request);
            return createCircleResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> ReassignCircleOwningApp(Guid circleId, Guid appId)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleDefinition>(client, ownerSharedSecret);
            return await svc.ReassignCircleOwningApp(new SetCircleOwningAppRequest
            {
                CircleId = circleId,
                AppId = appId
            });
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

    public async Task<ApiResponse<HttpContent>> UpdateCircleDefinition(CircleDefinition definition)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleDefinition>(client, ownerSharedSecret);
            var response = await svc.UpdateCircleDefinition(definition);
            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteCircleDefinition(GuidId circleId)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleDefinition>(client, ownerSharedSecret);
            var response = await svc.DeleteCircleDefinition(circleId);
            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> EnableCircleDefinition(GuidId circleId)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleDefinition>(client, ownerSharedSecret);
            var response = await svc.EnableCircleDefinition(circleId);
            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> DisableCircleDefinition(GuidId circleId)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleDefinition>(client, ownerSharedSecret);
            var response = await svc.DisableCircleDefinition(circleId);
            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> GrantCircle(Guid circleId, OdinId odinId)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.AddCircle(new AddCircleMembershipRequest()
            {
                CircleId = circleId,
                OdinId = odinId
            });

            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> RevokeCircle(Guid circleId, OdinId odinId)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.RevokeCircle(new RevokeCircleMembershipRequest()
            {
                CircleId = circleId,
                OdinId = odinId
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

            ClassicAssert.IsTrue(apiResponse.IsSuccessStatusCode, $"Failed to get status for {recipient}.  Status code was {apiResponse.StatusCode}");
            ClassicAssert.IsNotNull(apiResponse.Content, $"No status for {recipient} found");
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> BlockConnection(OdinId odinId)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.Block(new OdinIdRequest() { OdinId = odinId });
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> UnblockConnection(OdinId odinId)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.Unblock(new OdinIdRequest() { OdinId = odinId });
            return apiResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> DisconnectFrom(OdinId recipient, bool notifyRemote = false)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var disconnectResponse = await RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret)
                .Disconnect(new OdinIdRequest() { OdinId = recipient }, notifyRemote);
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
    
    public async Task<ApiResponse<HttpContent>> MarkReviewed(OdinId recipient, IEnumerable<GuidId> circleIds = null)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var connectionsService = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            return await connectionsService.MarkReviewed(new MarkConnectionReviewedRequest
            {
                OdinId = recipient,
                CircleIds = circleIds ?? []
            });
        }
    }

    public async Task<ApiResponse<HttpContent>> ClearReview(OdinId recipient)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var connectionsService = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            return await connectionsService.ClearReview(new OdinIdRequest() { OdinId = recipient });
        }
    }

    public async Task<ApiResponse<IcrVerificationResult>> ConfirmConnection(OdinId recipient)
    {
        var client = factory.CreateHttpClient(identity, out var ownerSharedSecret);
        {
            var connectionsService = RefitCreator.RestServiceFor<IRefitUniversalCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await connectionsService.ConfirmConnection(new OdinIdRequest() { OdinId = recipient });
            return apiResponse;
        }
    }
}