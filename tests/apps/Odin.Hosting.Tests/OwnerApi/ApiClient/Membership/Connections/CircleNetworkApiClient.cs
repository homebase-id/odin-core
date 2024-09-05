using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.ClientToken.App.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Connections;

public class CircleNetworkApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public CircleNetworkApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task AcceptConnectionRequest(TestIdentity sender, IEnumerable<GuidId> circleIdsGrantedToSender = null)
    {
        // Accept the request
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var header = new AcceptRequestHeader()
            {
                Sender = sender.OdinId,
                CircleIds = circleIdsGrantedToSender,
                ContactData = _identity.ContactData
            };

            var acceptResponse = await svc.AcceptConnectionRequest(header);
            Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");
        }
    }


    public async Task<ConnectionRequestResponse> GetIncomingRequestFrom(TestIdentity sender)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender.OdinId });

            return response.Content;
        }
    }

    public async Task<ConnectionRequestResponse> GetOutgoingSentRequestTo(TestIdentity recipient)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var response = await svc.GetSentRequest(new OdinIdRequest() { OdinId = recipient.OdinId });

            return response.Content;
        }
    }

    public async Task DeleteConnectionRequestFrom(TestIdentity sender)
    {
        if (!TestIdentities.All.TryGetValue(sender.OdinId, out var senderIdentity))
        {
            throw new NotImplementedException("need to add your recipient to the list of identities");
        }

        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var deleteResponse = await svc.DeletePendingRequest(new OdinIdRequest() { OdinId = sender.OdinId });
            Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

            var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender.OdinId });
            Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sender.OdinId} still exists");
        }
    }

    public async Task DeleteSentRequestTo(TestIdentity recipient)
    {
        if (!TestIdentities.All.TryGetValue(recipient.OdinId, out var senderIdentity))
        {
            throw new NotImplementedException("need to add your recipient to the list of identities");
        }


        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = recipient.OdinId });
            Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

            var getResponse = await svc.GetSentRequest(new OdinIdRequest() { OdinId = recipient.OdinId });
            Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {recipient.OdinId} still exists");
        }
    }

    public async Task SendConnectionRequestTo(TestIdentity recipient, IEnumerable<GuidId> circlesGrantedToRecipient = null)
    {
        if (!TestIdentities.All.TryGetValue(recipient.OdinId, out var recipientIdentity))
        {
            throw new NotImplementedException("need to add your recipient to the list of identities");
        }

        var response = await this.SendConnectionRequestRaw(recipient, circlesGrantedToRecipient);

        Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
        Assert.IsTrue(response!.Content, "Failed sending the request");
    }

    public async Task<ApiResponse<bool>> SendConnectionRequestRaw(TestIdentity recipient, IEnumerable<GuidId> circlesGrantedToRecipient = null)
    {
        if (!TestIdentities.All.TryGetValue(recipient.OdinId, out var recipientIdentity))
        {
            throw new NotImplementedException("need to add your recipient to the list of identities");
        }

        // Send the request
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var id = Guid.NewGuid();
            var requestHeader = new ConnectionRequestHeader()
            {
                Id = id,
                Recipient = recipient.OdinId,
                Message = "Please add me",
                ContactData = recipientIdentity.ContactData,
                CircleIds = circlesGrantedToRecipient?.ToList()
            };

            var response = await svc.SendConnectionRequest(requestHeader);
            return response;
        }
    }

    public async Task DisconnectFrom(TestIdentity recipient)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var disconnectResponse = await RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret)
                .Disconnect(new OdinIdRequest() { OdinId = recipient.OdinId });
            Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
            await AssertConnectionStatus(client, ownerSharedSecret, recipient.OdinId, ConnectionStatus.None);
        }
    }

    public async Task<bool> IsConnected(TestIdentity recipient)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var connectionsService = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var existingConnectionInfo = await connectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = recipient.OdinId });
            if (existingConnectionInfo.IsSuccessStatusCode && existingConnectionInfo.Content != null &&
                existingConnectionInfo.Content.Status == ConnectionStatus.Connected)
            {
                return true;
            }
        }

        return false;
    }

    public async Task GrantCircle(Guid circleId, TestIdentity recipient)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.AddCircle(new AddCircleMembershipRequest()
            {
                CircleId = circleId,
                OdinId = recipient.OdinId
            });

            Assert.IsTrue(apiResponse.IsSuccessStatusCode, $"Actual status code {apiResponse.StatusCode}");
        }
    }

    public async Task RevokeCircle(Guid circleId, TestIdentity recipient)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.RevokeCircle(new RevokeCircleMembershipRequest()
            {
                CircleId = circleId,
                OdinId = recipient.OdinId
            });

            Assert.IsTrue(apiResponse.IsSuccessStatusCode, $"Actual status code {apiResponse.StatusCode}");
        }
    }

    public async Task<object> GetCircleMembers(Guid circleId)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.GetCircleMembers(new GetCircleMembersRequest() { CircleId = circleId });
            Assert.IsTrue(apiResponse.IsSuccessStatusCode, $"Actual status code {apiResponse.StatusCode}");
            var members = apiResponse.Content;
            Assert.NotNull(members);
            return members;
        }
    }

    public async Task<RedactedIdentityConnectionRegistration> GetConnectionInfo(TestIdentity recipient)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var connectionsService = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await connectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = recipient.OdinId });

            Assert.IsTrue(apiResponse.IsSuccessStatusCode, $"Failed to get status for {recipient.OdinId}.  Status code was {apiResponse.StatusCode}");
            Assert.IsNotNull(apiResponse.Content, $"No status for {recipient.OdinId} found");
            return apiResponse.Content;
        }
    }

    public async Task<bool> BlockConnection(TestIdentity recipient)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var apiResponse = await svc.Block(new OdinIdRequest() { OdinId = recipient.OdinId });
            return apiResponse.Content;
        }
    }

    private async Task AssertConnectionStatus(HttpClient client, SensitiveByteArray ownerSharedSecret, OdinId odinId, ConnectionStatus expected)
    {
        var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
        var response = await svc.GetConnectionInfo(new OdinIdRequest() { OdinId = odinId });

        Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {odinId}.  Status code was {response.StatusCode}");
        Assert.IsNotNull(response.Content, $"No status for {odinId} found");
        Assert.IsTrue(response.Content.Status == expected, $"{odinId} status does not match {expected}");
    }
}