using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Configuration;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken.Membership.Connections;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.Membership.Connections;

namespace Odin.Hosting.Tests.OwnerApi.Configuration.SystemInit;

public class ConfigurationTestUtilities
{
    private WebScaffold _scaffold;

    public ConfigurationTestUtilities(WebScaffold scaffold)
    {
        _scaffold = scaffold;
    }

    public void AssertAllDrivesGrantedFromCircle(CircleDefinition circleDefinition, RedactedCircleGrant actual)
    {
        foreach (var circleDriveGrant in circleDefinition.DriveGrants)
        {
            //be sure it's in the list of granted drives; use Single to be sure it's only in there once
            var result = actual.DriveGrants.SingleOrDefault(x =>
                x.PermissionedDrive.Drive == circleDriveGrant.PermissionedDrive.Drive && x.PermissionedDrive.Permission == circleDriveGrant.PermissionedDrive.Permission);
            Assert.NotNull(result);
        }
    }

    public async Task AssertIdentityIsInCircle(HttpClient client, SensitiveByteArray ownerSharedSecret, GuidId circleId, OdinId expectedIdentity)
    {
        var circleMemberSvc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
        var getCircleMemberResponse = await circleMemberSvc.GetCircleMembers(new GetCircleMembersRequest() { CircleId = circleId });
        Assert.IsTrue(getCircleMemberResponse.IsSuccessStatusCode, $"Actual status code {getCircleMemberResponse.StatusCode}");
        var members = getCircleMemberResponse.Content;
        Assert.NotNull(members);
        Assert.IsTrue(members.Any());
        Assert.IsFalse(members.SingleOrDefault(m => m == expectedIdentity).DomainName == null);
    }

    public async Task AssertConnectionStatus(HttpClient client, SensitiveByteArray ownerSharedSecret, OdinId odinId, ConnectionStatus expected)
    {
        var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
        var response = await svc.GetConnectionInfo(new OdinIdRequest() { OdinId = odinId });

        Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {odinId}.  Status code was {response.StatusCode}");
        Assert.IsNotNull(response.Content, $"No status for {odinId} found");
        Assert.IsTrue(response.Content.Status == expected, $"{odinId} status does not match {expected}");
    }

    public async Task<(TestAppContext, TestAppContext, ConnectionRequestHeader)> CreateConnectionRequestFrodoToSam()
    {
        return await CreateConnectionRequest(TestIdentities.Frodo, TestIdentities.Samwise);
    }

    public async Task<(TestAppContext, TestAppContext, ConnectionRequestHeader)> CreateConnectionRequest(TestIdentity senderIdentity, TestIdentity recipientIdentity)
    {
        Guid appId = Guid.NewGuid();
        var sender = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, senderIdentity, canReadConnections: true);
        var recipient = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, recipientIdentity, canReadConnections: true);

        var id = Guid.NewGuid();
        var requestHeader = new ConnectionRequestHeader()
        {
            Id = id,
            Recipient = recipient.Identity,
            Message = "Please add me",
            CircleIds = null,
            ContactData = sender.ContactData
        };

        //have frodo send it
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender.Identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var response = await svc.SendConnectionRequest(requestHeader);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
            Assert.IsTrue(response.Content, "Failed sending the request");
        }

        //check that sam got it
        client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(recipient.Identity, out ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);
            var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender.Identity });

            Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

            Assert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
            Assert.IsTrue(response.Content.SenderOdinId == sender.Identity);
        }

        return (sender, recipient, requestHeader);
    }
    public async Task AcceptConnectionRequest(TestAppContext sender, TestAppContext recipient)
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(recipient.Identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var header = new AcceptRequestHeader()
            {
                Sender = sender.Identity,
                CircleIds = new List<GuidId>(),
                ContactData = recipient.ContactData
            };

            var acceptResponse = await svc.AcceptConnectionRequest(header);
            Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");
            await AssertConnectionStatus(client, ownerSharedSecret, sender.Identity, ConnectionStatus.Connected);
        }
    }

    public async Task DeleteConnectionRequestsFromFrodoToSam(TestAppContext frodo, TestAppContext sam)
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var deleteResponse = await svc.DeletePendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

            var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
        }

        client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

            var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

            var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
        }
    }

    public async Task DisconnectIdentities(TestAppContext frodo, TestAppContext sam)
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret);
        {
            var frodoConnections = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var disconnectResponse = await frodoConnections.Disconnect(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
            await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Samwise.OdinId, ConnectionStatus.None);
        }

        client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out ownerSharedSecret);
        {
            var samConnections = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var disconnectResponse = await samConnections.Disconnect(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
            await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Frodo.OdinId, ConnectionStatus.None);
        }
    }

    public async Task UpdateTenantSettingsFlag(TestIdentity identity, string flag, string value)
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            var updateFlagResponse = await svc.UpdateSystemConfigFlag(new UpdateFlagRequest()
            {
                FlagName = flag,
                Value = value
            });

            Assert.IsTrue(updateFlagResponse.IsSuccessStatusCode, "system should return empty settings when first initialized");
        }
    }
}