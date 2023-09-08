using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.Circles;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.OwnerToken.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.Utils;

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
    
    public async Task AcceptConnectionRequest(TestIdentity sender, IEnumerable<GuidId> circleIdsGrantedToSender)
    {
        // Accept the request
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

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

    public async Task SendConnectionRequest(TestIdentity recipient, IEnumerable<GuidId> circlesGrantedToRecipient)
    {
        if (!TestIdentities.All.TryGetValue(recipient.OdinId, out var recipientIdentity))
        {
            throw new NotImplementedException("need to add your recipient to the list of identities");
        }

        // Send the request
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

            var id = Guid.NewGuid();
            var requestHeader = new ConnectionRequestHeader()
            {
                Id = id,
                Recipient = recipient.OdinId,
                Message = "Please add me",
                ContactData = recipientIdentity.ContactData,
                CircleIds = circlesGrantedToRecipient.ToList()
            };

            var response = await svc.SendConnectionRequest(requestHeader);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
            Assert.IsTrue(response!.Content, "Failed sending the request");
        }
    }

    public async Task DisconnectFrom(TestIdentity recipient)
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var disconnectResponse = await RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret)
                .Disconnect(new OdinIdRequest() { OdinId = recipient.OdinId });
            Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
            await AssertConnectionStatus(client, ownerSharedSecret, recipient.OdinId, ConnectionStatus.None);
        }
    }

    public async Task<bool> IsConnected(TestIdentity recipient)
    {
        var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var connectionsService = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
            var existingConnectionInfo = await connectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = recipient.OdinId });
            if (existingConnectionInfo.IsSuccessStatusCode && existingConnectionInfo.Content != null && existingConnectionInfo.Content.Status == ConnectionStatus.Connected)
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
            var svc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
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
            var svc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
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
            var svc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
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
            var connectionsService = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await connectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = recipient.OdinId });

            Assert.IsTrue(apiResponse.IsSuccessStatusCode, $"Failed to get status for {recipient.OdinId}.  Status code was {apiResponse.StatusCode}");
            Assert.IsNotNull(apiResponse.Content, $"No status for {recipient.OdinId} found");
            return apiResponse.Content;
        }
    }

    private async Task AssertConnectionStatus(HttpClient client, SensitiveByteArray ownerSharedSecret, string odinId, ConnectionStatus expected)
    {
        var svc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
        var response = await svc.GetConnectionInfo(new OdinIdRequest() { OdinId = odinId });

        Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {odinId}.  Status code was {response.StatusCode}");
        Assert.IsNotNull(response.Content, $"No status for {odinId} found");
        Assert.IsTrue(response.Content.Status == expected, $"{odinId} status does not match {expected}");
    }
}