using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Owner.CircleMembership;

[Obsolete]
public class CircleNetworkRequestsApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public CircleNetworkRequestsApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task<ApiResponse<HttpContent>> AcceptConnectionRequest(OdinId sender, IEnumerable<GuidId> circleIdsGrantedToSender = null)
    {
        // Accept the request
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var header = new AcceptRequestHeader()
            {
                Sender = sender,
                CircleIds = circleIdsGrantedToSender,
                ContactData = _identity.ContactData
            };

            var acceptResponse = await svc.AcceptConnectionRequest(header);
            return acceptResponse;
        }
    }

    public async Task<ApiResponse<ConnectionRequestResponse>> GetIncomingRequestFrom(OdinId sender)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender });

            return response;
        }
    }

    public async Task<ApiResponse<ConnectionRequestResponse>> GetOutgoingSentRequestTo(OdinId recipient)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var response = await svc.GetSentRequest(new OdinIdRequest() { OdinId = recipient });

            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteConnectionRequestFrom(OdinId sender)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var deleteResponse = await svc.DeletePendingRequest(new OdinIdRequest() { OdinId = sender });
            return deleteResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> DeleteSentRequestTo(OdinId recipient)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = recipient });
            return deleteResponse;
        }
    }

    public async Task<ApiResponse<HttpContent>> SendConnectionRequest(OdinId recipient, IEnumerable<GuidId> circlesGrantedToRecipient = null)
    {
        // Send the request
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var id = Guid.NewGuid();
            var requestHeader = new ConnectionRequestHeader()
            {
                Id = id,
                Recipient = recipient,
                Message = "Please add me",
                ContactData = new ContactRequestData()
                {
                    Name = "Test Test"
                },
                CircleIds = circlesGrantedToRecipient?.ToList()
            };

            var response = await svc.SendConnectionRequest(requestHeader);
            return response;
        }
    }

    public async Task<ApiResponse<HttpContent>> DisconnectFrom(OdinId recipient)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var disconnectResponse = await RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret)
                .Disconnect(new OdinIdRequest() { OdinId = recipient });
            return disconnectResponse;
        }
    }


    public async Task<ApiResponse<RedactedIdentityConnectionRegistration>> GetConnectionInfo(OdinId recipient)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var connectionsService = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await connectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = recipient });
            return apiResponse;
        }
    }
}