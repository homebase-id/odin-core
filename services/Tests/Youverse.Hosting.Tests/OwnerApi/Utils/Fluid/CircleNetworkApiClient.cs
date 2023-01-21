using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Tests.OwnerApi.Circle;

namespace Youverse.Hosting.Tests.OwnerApi.Utils.Fluid;

public class CircleNetworkApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public CircleNetworkApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task<CircleDefinition> CreateCircle(string circleName, PermissionSetGrantRequest grant)
    {
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
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

            foreach (var dgr in request.DriveGrants)
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

    public async Task AcceptConnectionRequest(TestIdentity sender, IEnumerable<GuidId> circleIdsGrantedToSender)
    {
        // Accept the request
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

            var header = new AcceptRequestHeader()
            {
                Sender = sender.DotYouId,
                CircleIds = circleIdsGrantedToSender,
                ContactData = _identity.ContactData
            };

            var acceptResponse = await svc.AcceptConnectionRequest(header);
            Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");
        }
    }

    public async Task SendConnectionRequest(TestIdentity recipient, IEnumerable<GuidId> circlesGrantedToRecipient)
    {
        if (!TestIdentities.All.TryGetValue(recipient.DotYouId, out var recipientIdentity))
        {
            throw new NotImplementedException("need to add your recipient to the list of identities");
        }

        // Send the request
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

            var id = Guid.NewGuid();
            var requestHeader = new ConnectionRequestHeader()
            {
                Id = id,
                Recipient = recipient.DotYouId,
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
        using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var disconnectResponse = await RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret)
                .Disconnect(new DotYouIdRequest() { DotYouId = recipient.DotYouId });
            Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
            await AssertConnectionStatus(client, ownerSharedSecret, recipient.DotYouId, ConnectionStatus.None);
        }
    }

    public async Task<bool> IsConnected(TestIdentity recipient)
    {
        using (var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var connectionsService = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
            var existingConnectionInfo = await connectionsService.GetConnectionInfo(new DotYouIdRequest() { DotYouId = recipient.DotYouId });
            if (existingConnectionInfo.IsSuccessStatusCode && existingConnectionInfo.Content != null && existingConnectionInfo.Content.Status == ConnectionStatus.Connected)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<RedactedIdentityConnectionRegistration> GetConnectionInfo(TestIdentity recipient)
    {
        using (var client = this._ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
        {
            var connectionsService = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
            var apiResponse = await connectionsService.GetConnectionInfo(new DotYouIdRequest() { DotYouId = recipient.DotYouId });

            Assert.IsTrue(apiResponse.IsSuccessStatusCode, $"Failed to get status for {recipient.DotYouId}.  Status code was {apiResponse.StatusCode}");
            Assert.IsNotNull(apiResponse.Content, $"No status for {recipient.DotYouId} found");
            return apiResponse.Content;
        }
    }

    private async Task AssertConnectionStatus(HttpClient client, SensitiveByteArray ownerSharedSecret, string dotYouId, ConnectionStatus expected)
    {
        var svc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
        var response = await svc.GetConnectionInfo(new DotYouIdRequest() { DotYouId = dotYouId });

        Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {dotYouId}.  Status code was {response.StatusCode}");
        Assert.IsNotNull(response.Content, $"No status for {dotYouId} found");
        Assert.IsTrue(response.Content.Status == expected, $"{dotYouId} status does not match {expected}");
    }
}