using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.OwnerToken.Circles;
using Youverse.Hosting.Tests.OwnerApi.Circle;

namespace Youverse.Hosting.Tests.OwnerApi.Configuration.SystemInit;

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
        var circleMemberSvc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
        var getCircleMemberResponse = await circleMemberSvc.GetCircleMembers(new GetCircleMembersRequest() { CircleId = circleId });
        Assert.IsTrue(getCircleMemberResponse.IsSuccessStatusCode, $"Actual status code {getCircleMemberResponse.StatusCode}");
        var members = getCircleMemberResponse.Content;
        Assert.NotNull(members);
        Assert.IsTrue(members.Any());
        Assert.IsFalse(members.SingleOrDefault(m => m == expectedIdentity).Id == null);
    }

    public async Task AssertConnectionStatus(HttpClient client, SensitiveByteArray ownerSharedSecret, string odinId, ConnectionStatus expected)
    {
        var svc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
        var response = await svc.GetConnectionInfo(new OdinIdRequest() { OdinId = odinId });

        Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {odinId}.  Status code was {response.StatusCode}");
        Assert.IsNotNull(response.Content, $"No status for {odinId} found");
        Assert.IsTrue(response.Content.Status == expected, $"{odinId} status does not match {expected}");
    }

    public async Task<(TestAppContext, TestAppContext, ConnectionRequestHeader)> CreateConnectionRequestFrodoToSam()
    {
        Guid appId = Guid.NewGuid();
        var sender = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, TestIdentities.Frodo, canReadConnections: true);
        var recipient = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canReadConnections: true);

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
        using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender.Identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

            var response = await svc.SendConnectionRequest(requestHeader);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
            Assert.IsTrue(response.Content, "Failed sending the request");
        }

        //check that sam got it
        using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(recipient.Identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);
            var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender.Identity });

            Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

            Assert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
            Assert.IsTrue(response.Content.SenderOdinId == sender.Identity);
        }

        return (sender, recipient, requestHeader);
    }

    public async Task AcceptConnectionRequest(TestAppContext sender, TestAppContext recipient)
    {
        using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(recipient.Identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

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
        using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

            var deleteResponse = await svc.DeletePendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

            var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
        }

        using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

            var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

            var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
        }
    }

    public async Task DisconnectIdentities(TestAppContext frodo, TestAppContext sam)
    {
        using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret))
        {
            var frodoConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
            var disconnectResponse = await frodoConnections.Disconnect(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
            await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Samwise.OdinId, ConnectionStatus.None);
        }

        using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret))
        {
            var samConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
            var disconnectResponse = await samConnections.Disconnect(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
            await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Frodo.OdinId, ConnectionStatus.None);
        }
    }

    public async Task UpdateSystemConfigFlag(TestIdentity identity, string flag, string value)
    {
        using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
        {
            var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);
            var updateFlagResponse = await svc.UpdateSystemConfigFlag(new UpdateFlagRequest()
            {
                FlagName = flag,
                Value = value
            });

            Assert.IsTrue(updateFlagResponse.IsSuccessStatusCode, "system should return empty settings when first initialized");
        }
    }
}