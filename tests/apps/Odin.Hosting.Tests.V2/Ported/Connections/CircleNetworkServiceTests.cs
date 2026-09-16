using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>OwnerApi/Membership/Connections/CircleNetworkServiceTests</c>. The owner-console
/// connection-request lifecycle end to end: send / read / delete a request on both sides, accept one
/// and verify the circle grants and circle membership it produces on each identity, grant and revoke
/// a circle after the fact, block / unblock / disconnect, and confirm that accepting puts both
/// identities into the confirmed-connections system circle.
/// </summary>
/// <remarks>
/// The endpoints under test are V1 only and several tests assert a refusal (404 / 400), so every
/// call goes through the V1 Refit interfaces via <see cref="OwnerSession.RefitFor{T}"/> — the same
/// interfaces the original used — rather than <c>owner.Admin</c>, which is arrange-only and throws.
/// <para>
/// <c>SetupTestSampleApp</c> is reproduced locally as <see cref="SetupTestSampleAppAsync"/>: create
/// the app drive, register the app with full permission on it (plus <c>ReadConnections</c> /
/// <c>ReadConnectionRequests</c> when asked, and always <c>UseTransitWrite</c>). The original also
/// registered an app *client* and carried its token on <c>TestAppContext</c>; no test in this
/// fixture ever used the token — every call is made as the owner — so that step is dropped. The
/// drive's metadata string differs (<c>owner.Admin.CreateDrive</c> sends empty, the original sent
/// <c>"{data:'test metadata'}"</c>); nothing reads it.
/// </para>
/// <para>
/// Carried defects, behaviour left exactly as found:
/// <list type="bullet">
/// <item><see cref="GrantCircle"/> comments "Frodo should be in 3 circles" and then checks the new
/// circle followed by <c>circleOnSamsIdentity2</c> <i>twice</i>; circle 1 is never re-checked
/// there.</item>
/// <item><see cref="CanAcceptConnectionRequest_AndAccessCirclePermissions"/> builds its first circle
/// with <c>{ ReadConnections, ReadConnections }</c> — the same permission key listed twice — as does
/// <c>circleOnSamsIdentity2</c> in the same test.</item>
/// <item><c>CreateConnectionRequestFrodoToSam</c> registers the app on <i>both</i> identities with
/// the same <c>authorizedCircles</c> list, which holds circle ids that exist only on Frodo; the
/// registration on Sam therefore authorizes circles Sam does not have.</item>
/// <item><c>CreateConnectionRequestFrodoToSam</c> returns the request header as a third tuple
/// element that every caller discards.</item>
/// <item><c>DeleteConnectionRequestsFromFrodoToSam</c> deletes Frodo's pending request on Sam, then
/// asserts that Sam has no pending request <i>from Sam</i> — it queries its own identity rather than
/// Frodo's, so the assertion is vacuous. Same shape in the sent-request half.</item>
/// </list>
/// </para>
/// <para>
/// Trailing <c>DisconnectIdentities</c> / <c>DeleteConnectionRequestsFromFrodoToSam</c> calls are
/// kept where the original had them: both make assertions of their own, so they are tests rather
/// than cleanup. <c>SetupCallerWithOwner</c> ordering is not in play — no caller matrix,
/// <c>LoginAsOwner</c> only.
/// </para>
/// </remarks>
[TestFixture]
public class CircleNetworkServiceTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task FailToSendConnectionRequestToSelf()
    {
        var frodoOwner = await LoginAsOwner(Identities.Frodo);

        Guid appId = Guid.NewGuid();
        var sender = await SetupTestSampleAppAsync(frodoOwner, TestIdentities.Frodo, appId, canReadConnections: true);

        List<GuidId> cids = new List<GuidId>();

        var id = Guid.NewGuid();

        var requestHeader = new ConnectionRequestHeader()
        {
            Id = id,
            Recipient = sender.Identity,
            Message = "Please add me",
            CircleIds = cids
        };

        var svc = sender.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

        var response = await svc.SendConnectionRequest(requestHeader);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CanSendConnectionRequestAndGetPendingRequest()
    {
        var frodoOwner = await LoginAsOwner(Identities.Frodo);
        var samOwner = await LoginAsOwner(Identities.Sam);

        Guid appId = Guid.NewGuid();
        var sender = await SetupTestSampleAppAsync(frodoOwner, TestIdentities.Frodo, appId, canReadConnections: true);
        var recipient = await SetupTestSampleAppAsync(samOwner, TestIdentities.Samwise, appId, canReadConnections: true);

        {
            var svc = sender.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var id = Guid.NewGuid();
            var requestHeader = new ConnectionRequestHeader()
            {
                Id = id,
                Recipient = recipient.Identity,
                Message = "Please add me",
                ContactData = sender.ContactData
            };

            var response = await svc.SendConnectionRequest(requestHeader);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content, Is.True, "Failed sending the request");
        }

        {
            var svc = recipient.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();
            var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender.Identity });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            Assert.That(response.Content, Is.Not.Null, $"No request found from {sender.Identity}");
            Assert.That(response.Content.SenderOdinId, Is.EqualTo(sender.Identity.DomainName));

            Assert.That(response.Content.ContactData.Name, Is.EqualTo(sender.ContactData.Name));
            Assert.That(response.Content.ContactData.ImageId, Is.EqualTo(sender.ContactData.ImageId));
        }

        await DeleteConnectionRequestsFromFrodoToSam(sender, recipient);
    }

    [Test]
    public async Task CanDeletePendingConnectionRequest()
    {
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

        {
            var svc = sam.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var deleteResponse = await svc.DeletePendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
    }

    [Test]
    public async Task CanDeleteSentConnectionRequest()
    {
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

        {
            var svc = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
    }

    [Test]
    public async Task CanGetPendingConnectionRequestList()
    {
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

        {
            var svc = sam.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var response = await svc.GetPendingRequestList(PageOptions.Default);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content, Is.Not.Null);
            Assert.That(response.Content.TotalPages, Is.GreaterThanOrEqualTo(1));
            Assert.That(response.Content.Results.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(response.Content.Results, Has.Exactly(1).Matches<PendingConnectionRequestHeader>(
                r => r.SenderOdinId == frodo.Identity), $"Could not find request from {frodo.Identity} in the results");

            Assert.That(response.Content.Results.Select(r => r.Payload), Is.All.Null);
        }

        await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
    }

    [Test]
    public async Task CanGetPendingConnectionFullDetails()
    {
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

        {
            var svc = sam.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var request = response.Content;
            Assert.That(request, Is.Not.Null);

            Assert.That(request.Recipient, Is.EqualTo(sam.Identity.DomainName));

            // UnixTimeUtc is not IComparable -- Is.GreaterThan compiles and throws at run time.
            Assert.That(request.ReceivedTimestampMilliseconds.milliseconds, Is.GreaterThan(0));
            Assert.That(request.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(request.SenderOdinId, Is.EqualTo(frodo.Identity.DomainName));
            // request.CircleIds
            // request.ContactData
        }

        await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
    }

    [Test]
    public async Task CanGetSentConnectionRequestList()
    {
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

        //Check Sam's list of sent requests
        {
            var svc = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var response = await svc.GetSentRequestList(PageOptions.Default);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content, Is.Not.Null, "No result returned");
            Assert.That(response.Content.TotalPages, Is.GreaterThanOrEqualTo(1));
            Assert.That(response.Content.Results.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(response.Content.Results, Has.Exactly(1).Matches<ConnectionRequestResponse>(
                r => r.Recipient == sam.Identity), $"Could not find request with recipient {sam.Identity} in the results");
        }

        await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
    }

    [Test]
    public async Task CanGetSentConnectionRequest()
    {
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

        {
            var svc = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var response = await svc.GetSentRequest(new OdinIdRequest() { OdinId = sam.Identity });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content, Is.Not.Null, $"No request found with recipient [{sam.Identity}]");
            Assert.That(response.Content.Recipient, Is.EqualTo(sam.Identity.DomainName));
        }

        await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
    }

    [Test]
    public async Task CanAcceptConnectionRequest_AndAccessCirclePermissions()
    {
        var frodoOwner = await LoginAsOwner(Identities.Frodo);
        var samOwner = await LoginAsOwner(Identities.Sam);

        //basically create 2 circles on frodo's identity, then give sam access
        var circleOnFrodosIdentity1 =
            await this.CreateCircleWith2Drives(frodoOwner, "frodo c1",
                new List<int>() { PermissionKeys.ReadConnections, PermissionKeys.ReadConnections });
        var circleOnFrodosIdentity2 =
            await this.CreateCircleWith2Drives(frodoOwner, "frodo c2", new List<int> { PermissionKeys.ReadCircleMembership });
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam(frodoOwner, samOwner, circleOnFrodosIdentity1, circleOnFrodosIdentity2);

        // create 2 circles on sam's identity and give frodo access
        var circleOnSamsIdentity1 = await this.CreateCircleWith2Drives(sam.Owner, "c1", new List<int>());
        var circleOnSamsIdentity2 =
            await this.CreateCircleWith2Drives(sam.Owner, "c2", new List<int> { PermissionKeys.ReadConnections, PermissionKeys.ReadConnections });

        {
            var connectionRequestService = sam.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var header = new AcceptRequestHeader()
            {
                Sender = frodo.Identity,
                CircleIds = new List<GuidId>() { circleOnSamsIdentity1.Id, circleOnSamsIdentity2.Id },
                ContactData = sam.ContactData
            };

            var acceptResponse = await connectionRequestService.AcceptConnectionRequest(header);

            Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            //
            // The pending request should be removed
            //
            var getResponse = await connectionRequestService.GetPendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            //
            // Frodo should be in Sam's contacts network.
            //
            var samsConnetions = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var getFrodoInfoResponse = await samsConnetions.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity }, omitContactData: false);

            Assert.That(getFrodoInfoResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getFrodoInfoResponse.Content, Is.Not.Null, $"No status for {frodo.Identity} found");
            Assert.That(getFrodoInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

            //
            // Validate the contact data sent by frodo was set on his ICR on sam's identity
            //
            Assert.That(getFrodoInfoResponse.Content.OriginalContactData.Name, Is.EqualTo(frodo.ContactData.Name));
            Assert.That(getFrodoInfoResponse.Content.OriginalContactData.ImageId, Is.EqualTo(frodo.ContactData.ImageId));

            var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
            var frodoAccessFromCircle1 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity1.Id);
            Assert.That(frodoAccessFromCircle1, Is.Not.Null);
            Assert.That(frodoAccessFromCircle1.PermissionSet, Is.EqualTo(circleOnSamsIdentity1.Permissions));

            AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity1, frodoAccessFromCircle1);

            var frodoAccessFromCircle2 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity2.Id);
            Assert.That(frodoAccessFromCircle2, Is.Not.Null);
            Assert.That(frodoAccessFromCircle2.PermissionSet, Is.EqualTo(circleOnSamsIdentity2.Permissions));
            AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity2, frodoAccessFromCircle2);

            //
            // Frodo should show up in the member list for each circle
            //
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity1.Id, frodo.Identity);
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity2.Id, frodo.Identity);
        }


        //
        // Now connect to Frodo to see that sam is a connection with correct access
        //
        {
            //
            // Sent request should be deleted
            //
            var svc = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();
            var getSentRequestResponse = await svc.GetSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.That(getSentRequestResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            //
            // Sam should be in Frodo's contacts network
            //
            var frodoConnections = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var getSamConnectionInfoResponse =
                await frodoConnections.GetConnectionInfo(new OdinIdRequest() { OdinId = sam.Identity }, omitContactData: false);

            Assert.That(getSamConnectionInfoResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getSamConnectionInfoResponse.Content, Is.Not.Null, $"No status for {sam.Identity} found");
            Assert.That(getSamConnectionInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

            //
            // Validate the contact data sent by sam was set on his ICR on frodo's identity
            //
            Assert.That(getSamConnectionInfoResponse.Content.OriginalContactData.Name, Is.EqualTo(sam.ContactData.Name));
            Assert.That(getSamConnectionInfoResponse.Content.OriginalContactData.ImageId, Is.EqualTo(sam.ContactData.ImageId));

            var samAccess = getSamConnectionInfoResponse.Content.AccessGrant;
            var samAccessFromCircle1 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity1.Id);
            Assert.That(samAccessFromCircle1, Is.Not.Null);
            Assert.That(samAccessFromCircle1.PermissionSet, Is.EqualTo(circleOnFrodosIdentity1.Permissions));

            AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity1, samAccessFromCircle1);

            var samAccessFromCircle2 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity2.Id);
            Assert.That(samAccessFromCircle2, Is.Not.Null);
            Assert.That(samAccessFromCircle2.PermissionSet, Is.EqualTo(circleOnFrodosIdentity2.Permissions));
            AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity2, samAccessFromCircle2);

            //
            // Sam should show up in the member list for each circle
            //
            await AssertIdentityIsInCircle(frodo.Owner, circleOnFrodosIdentity1.Id, sam.Identity);
            await AssertIdentityIsInCircle(frodo.Owner, circleOnFrodosIdentity2.Id, sam.Identity);
        }

        await DisconnectIdentities(frodo, sam);
    }

    [Test]
    public async Task GrantCircle()
    {
        var frodoOwner = await LoginAsOwner(Identities.Frodo);
        var samOwner = await LoginAsOwner(Identities.Sam);

        #region Firstly, setup connections and put into circles

        var circleOnFrodosIdentity1 = await this.CreateCircleWith2Drives(frodoOwner, "frodo c1", new List<int>());
        var circleOnFrodosIdentity2 =
            await this.CreateCircleWith2Drives(frodoOwner, "frodo c2", new List<int>() { PermissionKeys.ReadConnections });
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam(frodoOwner, samOwner, circleOnFrodosIdentity1, circleOnFrodosIdentity2);

        // create 2 circles on sam's identity and give frodo access
        var circleOnSamsIdentity1 = await this.CreateCircleWith2Drives(sam.Owner, "c1", new List<int>() { PermissionKeys.ReadCircleMembership });
        var circleOnSamsIdentity2 = await this.CreateCircleWith2Drives(sam.Owner, "c2", new List<int>());

        {
            var connectionRequestService = sam.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var header = new AcceptRequestHeader()
            {
                Sender = frodo.Identity,
                CircleIds = new List<GuidId>() { circleOnSamsIdentity1.Id, circleOnSamsIdentity2.Id },
                ContactData = sam.ContactData
            };

            var acceptResponse = await connectionRequestService.AcceptConnectionRequest(header);

            Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            //
            // The pending request should be removed
            //
            var getResponse = await connectionRequestService.GetPendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            //
            // Frodo should be in Sam's contacts network.
            //
            var samsConnetionsService = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var getFrodoInfoResponse = await samsConnetionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity });

            Assert.That(getFrodoInfoResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getFrodoInfoResponse.Content, Is.Not.Null, $"No status for {frodo.Identity} found");
            Assert.That(getFrodoInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

            var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
            var frodoAccessFromCircle1 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity1.Id);
            Assert.That(frodoAccessFromCircle1, Is.Not.Null);
            Assert.That(frodoAccessFromCircle1.PermissionSet, Is.EqualTo(circleOnSamsIdentity1.Permissions));

            AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity1, frodoAccessFromCircle1);

            var frodoAccessFromCircle2 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity2.Id);
            Assert.That(frodoAccessFromCircle2, Is.Not.Null);
            Assert.That(frodoAccessFromCircle2.PermissionSet, Is.EqualTo(circleOnSamsIdentity2.Permissions));
            AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity2, frodoAccessFromCircle2);

            //
            // Frodo should show up in the member list for each circle
            //
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity1.Id, frodo.Identity);
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity2.Id, frodo.Identity);
        }


        //now connect to Frodo to see that sam is a connection with correct access
        {
            //
            // Sent request should be deleted
            //
            var svc = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();
            var getSentRequestResponse = await svc.GetSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.That(getSentRequestResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            //
            // Sam should be in Frodo's contacts network
            //
            var frodoConnections = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var getSamConnectionInfoResponse = await frodoConnections.GetConnectionInfo(new OdinIdRequest() { OdinId = sam.Identity });

            Assert.That(getSamConnectionInfoResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getSamConnectionInfoResponse.Content, Is.Not.Null, $"No status for {sam.Identity} found");
            Assert.That(getSamConnectionInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

            var samAccess = getSamConnectionInfoResponse.Content.AccessGrant;
            var samAccessFromCircle1 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity1.Id);
            Assert.That(samAccessFromCircle1, Is.Not.Null);
            Assert.That(samAccessFromCircle1.PermissionSet, Is.EqualTo(circleOnFrodosIdentity1.Permissions));

            AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity1, samAccessFromCircle1);

            var samAccessFromCircle2 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity2.Id);
            Assert.That(samAccessFromCircle2, Is.Not.Null);
            Assert.That(samAccessFromCircle2.PermissionSet, Is.EqualTo(circleOnFrodosIdentity2.Permissions));
            AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity2, samAccessFromCircle2);

            //
            // Sam should show up in the member list for each circle
            //
            await AssertIdentityIsInCircle(frodo.Owner, circleOnFrodosIdentity1.Id, sam.Identity);
            await AssertIdentityIsInCircle(frodo.Owner, circleOnFrodosIdentity2.Id, sam.Identity);
        }

        #endregion

        //
        // Create a new circle and grant frodo access circle access
        //
        var newCircleDefinitionOnSamsIdentity =
            await this.CreateCircleWith2Drives(sam.Owner, "newly created circle", new List<int>() { PermissionKeys.ReadConnections });

        {
            //
            // Frodo should show in both original circles
            //
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity1.Id, frodo.Identity);
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity2.Id, frodo.Identity);

            //
            // Add Frodo to newCircleDefinitionOnSamsIdentity
            //
            var circleMemberSvc = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var addMemberResponse = await circleMemberSvc.AddCircle(new AddCircleMembershipRequest()
            {
                CircleId = newCircleDefinitionOnSamsIdentity.Id,
                OdinId = frodo.Identity
            });

            Assert.That(addMemberResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            //
            // Frodo should be in 3 circles
            //
            await AssertIdentityIsInCircle(sam.Owner, newCircleDefinitionOnSamsIdentity.Id, frodo.Identity);
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity2.Id, frodo.Identity);
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity2.Id, frodo.Identity);

            //
            // Get frodo's connection info to see he s been given access to the new circle's drives
            //
            var samsConnectionsService = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var getFrodoInfoResponse = await samsConnectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity });

            Assert.That(getFrodoInfoResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getFrodoInfoResponse.Content, Is.Not.Null, $"No status for {frodo.Identity} found");
            Assert.That(getFrodoInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

            // frodo should have access to the new circle
            var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
            var frodoAccessFromNewCircle = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == newCircleDefinitionOnSamsIdentity.Id);
            Assert.That(frodoAccessFromNewCircle, Is.Not.Null);
            AssertAllDrivesGrantedFromCircle(newCircleDefinitionOnSamsIdentity, frodoAccessFromNewCircle);

            // frodo should still access to circle 1
            var frodoAccessFromCircle1 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity1.Id);
            Assert.That(frodoAccessFromCircle1, Is.Not.Null);
            Assert.That(frodoAccessFromCircle1.PermissionSet, Is.EqualTo(circleOnSamsIdentity1.Permissions));
            AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity1, frodoAccessFromCircle1);

            // frodo should still access to circle 2
            var frodoAccessFromCircle2 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity2.Id);
            Assert.That(frodoAccessFromCircle2, Is.Not.Null);
            Assert.That(frodoAccessFromCircle2.PermissionSet, Is.EqualTo(circleOnSamsIdentity2.Permissions));
            AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity2, frodoAccessFromCircle2);
        }


        await DisconnectIdentities(frodo, sam);
    }

    [Test]
    public async Task RevokeCircle()
    {
        var frodoOwner = await LoginAsOwner(Identities.Frodo);
        var samOwner = await LoginAsOwner(Identities.Sam);

        #region Firstly, setup connections and put into circles

        var circleOnFrodosIdentity1 = await this.CreateCircleWith2Drives(frodoOwner, "frodo c1", new List<int>());
        var circleOnFrodosIdentity2 =
            await this.CreateCircleWith2Drives(frodoOwner, "frodo c2", new List<int>() { PermissionKeys.ReadConnections });
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam(frodoOwner, samOwner, circleOnFrodosIdentity1, circleOnFrodosIdentity2);

        // create 2 circles on sam's identity and give frodo access
        var circleOnSamsIdentity1 = await this.CreateCircleWith2Drives(sam.Owner, "c1", new List<int>() { PermissionKeys.ReadCircleMembership });
        var circleOnSamsIdentity2 = await this.CreateCircleWith2Drives(sam.Owner, "c2", new List<int>());

        {
            var connectionRequestService = sam.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var header = new AcceptRequestHeader()
            {
                Sender = frodo.Identity,
                CircleIds = new List<GuidId>() { circleOnSamsIdentity1.Id, circleOnSamsIdentity2.Id },
                ContactData = sam.ContactData
            };

            var acceptResponse = await connectionRequestService.AcceptConnectionRequest(header);

            Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            //
            // The pending request should be removed
            //
            var getResponse = await connectionRequestService.GetPendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            //
            // Frodo should be in Sam's contacts network.
            //
            var samsConnetionsService = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var getFrodoInfoResponse = await samsConnetionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity });

            Assert.That(getFrodoInfoResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getFrodoInfoResponse.Content, Is.Not.Null, $"No status for {frodo.Identity} found");
            Assert.That(getFrodoInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

            var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
            var frodoAccessFromCircle1 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity1.Id);
            Assert.That(frodoAccessFromCircle1, Is.Not.Null);
            Assert.That(frodoAccessFromCircle1.PermissionSet, Is.EqualTo(circleOnSamsIdentity1.Permissions));

            AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity1, frodoAccessFromCircle1);

            var frodoAccessFromCircle2 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity2.Id);
            Assert.That(frodoAccessFromCircle2, Is.Not.Null);
            Assert.That(frodoAccessFromCircle2.PermissionSet, Is.EqualTo(circleOnSamsIdentity2.Permissions));
            AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity2, frodoAccessFromCircle2);

            //
            // Frodo should show up in the member list for each circle
            //
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity1.Id, frodo.Identity);
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity2.Id, frodo.Identity);
        }


        //now connect to Frodo to see that sam is a connection with correct access
        {
            //
            // Sent request should be deleted
            //
            var svc = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();
            var getSentRequestResponse = await svc.GetSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.That(getSentRequestResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            //
            // Sam should be in Frodo's contacts network
            //
            var frodoConnections = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var getSamConnectionInfoResponse = await frodoConnections.GetConnectionInfo(new OdinIdRequest() { OdinId = sam.Identity });

            Assert.That(getSamConnectionInfoResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getSamConnectionInfoResponse.Content, Is.Not.Null, $"No status for {sam.Identity} found");
            Assert.That(getSamConnectionInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

            var samAccess = getSamConnectionInfoResponse.Content.AccessGrant;
            var samAccessFromCircle1 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity1.Id);
            Assert.That(samAccessFromCircle1, Is.Not.Null);
            Assert.That(samAccessFromCircle1.PermissionSet, Is.EqualTo(circleOnFrodosIdentity1.Permissions));

            AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity1, samAccessFromCircle1);

            var samAccessFromCircle2 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity2.Id);
            Assert.That(samAccessFromCircle2, Is.Not.Null);
            Assert.That(samAccessFromCircle2.PermissionSet, Is.EqualTo(circleOnFrodosIdentity2.Permissions));
            AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity2, samAccessFromCircle2);

            //
            // Sam should show up in the member list for each circle
            //
            await AssertIdentityIsInCircle(frodo.Owner, circleOnFrodosIdentity1.Id, sam.Identity);
            await AssertIdentityIsInCircle(frodo.Owner, circleOnFrodosIdentity2.Id, sam.Identity);
        }

        #endregion

        //
        // Revoke circle access
        //
        {
            var revokedCircle = circleOnSamsIdentity1;

            //
            // Frodo should show in both circles
            //
            await AssertIdentityIsInCircle(sam.Owner, revokedCircle.Id, frodo.Identity);
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity2.Id, frodo.Identity);

            //
            // Revoke circleOnSamsIdentity1 from frodo
            //
            var circleMemberSvc = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var removeMembersResponse = await circleMemberSvc.RevokeCircle(new RevokeCircleMembershipRequest()
            {
                CircleId = revokedCircle.Id,
                OdinId = frodo.Identity
            });

            Assert.That(removeMembersResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            //
            // Frodo should not be in the revoked circle
            //
            var getCircleMemberResponse = await circleMemberSvc.GetCircleMembers(new GetCircleMembersRequest() { CircleId = revokedCircle.Id });
            Assert.That(getCircleMemberResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var members = getCircleMemberResponse.Content;
            Assert.That(members, Is.Not.Null);
            Assert.That(members, Does.Not.Contain(frodo.Identity));

            //
            // Frodo should still be in the second circle
            //
            await AssertIdentityIsInCircle(sam.Owner, circleOnSamsIdentity2.Id, frodo.Identity);

            //
            // Get frodo's connection info to see he's no longer has the drives for this circle
            //
            var samsConnectionsService = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var getFrodoInfoResponse = await samsConnectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity });

            Assert.That(getFrodoInfoResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getFrodoInfoResponse.Content, Is.Not.Null, $"No status for {frodo.Identity} found");
            Assert.That(getFrodoInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

            var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
            var frodoAccessFromCircle1 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == revokedCircle.Id);
            Assert.That(frodoAccessFromCircle1, Is.Null);

            // frodo should still access to circle 2
            var frodoAccessFromCircle2 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity2.Id);
            Assert.That(frodoAccessFromCircle2, Is.Not.Null);
            Assert.That(frodoAccessFromCircle2.PermissionSet, Is.EqualTo(circleOnSamsIdentity2.Permissions));
            AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity2, frodoAccessFromCircle2);
        }


        await DisconnectIdentities(frodo, sam);
    }


    [Test]
    public async Task CanBlock()
    {
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

        {
            var svc = sam.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var header = new AcceptRequestHeader()
            {
                Sender = frodo.Identity,
                CircleIds = new List<GuidId>(),
                ContactData = sam.ContactData
            };

            var acceptResponse = await svc.AcceptConnectionRequest(header);

            Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            await AssertConnectionStatus(sam.Owner, frodo.Identity, ConnectionStatus.Connected);

            var samConnections = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var blockResponse = await samConnections.Block(new OdinIdRequest() { OdinId = frodo.Identity });

            Assert.That(blockResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(blockResponse.Content, Is.True, "failed to block");
            await AssertConnectionStatus(sam.Owner, frodo.Identity, ConnectionStatus.Blocked);

            await samConnections.Unblock(new OdinIdRequest() { OdinId = frodo.Identity });
        }

        await DisconnectIdentities(frodo, sam);
    }

    [Test]
    public async Task CanUnblock()
    {
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

        {
            var svc = sam.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var header = new AcceptRequestHeader()
            {
                Sender = frodo.Identity,
                CircleIds = new List<GuidId>(),
                ContactData = sam.ContactData
            };

            var acceptResponse = await svc.AcceptConnectionRequest(header);

            Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            await AssertConnectionStatus(sam.Owner, frodo.Identity, ConnectionStatus.Connected);

            var samConnections = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var blockResponse = await samConnections.Block(new OdinIdRequest() { OdinId = frodo.Identity });

            Assert.That(blockResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(blockResponse.Content, Is.True, "failed to block");
            await AssertConnectionStatus(sam.Owner, frodo.Identity, ConnectionStatus.Blocked);

            var unblockResponse = await samConnections.Unblock(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.That(unblockResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(unblockResponse.Content, Is.True, "failed to unblock");
            await AssertConnectionStatus(sam.Owner, frodo.Identity, ConnectionStatus.Connected);
        }

        await DisconnectIdentities(frodo, sam);
    }

    [Test]
    public async Task CanDisconnect()
    {
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

        {
            var svc = sam.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var header = new AcceptRequestHeader()
            {
                Sender = frodo.Identity,
                CircleIds = new List<GuidId>(),
                ContactData = sam.ContactData
            };

            var acceptResponse = await svc.AcceptConnectionRequest(header);
            Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            await AssertConnectionStatus(sam.Owner, frodo.Identity, ConnectionStatus.Connected);

            var samConnections = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var disconnectResponse = await samConnections.Disconnect(new OdinIdRequest() { OdinId = frodo.Identity });
            // Content is false when there was nothing connected to disconnect (e.g. the peer already
            // severed the connection via a prior reciprocal-disconnect notification) -- that's not a failure.
            Assert.That(disconnectResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await AssertConnectionStatus(sam.Owner, frodo.Identity, ConnectionStatus.None);
        }

        {
            var frodoConnections = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var disconnectResponse = await frodoConnections.Disconnect(new OdinIdRequest() { OdinId = sam.Identity });
            // Content is false when there was nothing connected to disconnect (e.g. the peer already
            // severed the connection via a prior reciprocal-disconnect notification) -- that's not a failure.
            Assert.That(disconnectResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await AssertConnectionStatus(frodo.Owner, sam.Identity, ConnectionStatus.None);
        }
    }


    [Test(Description = "All connected identities go into the system circle")]
    public async Task ConnectedIdentitiesAreInSystemCircleUponApproval()
    {
        var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

        await AcceptConnectionRequest(sender: frodo, recipient: sam);

        {
            var circleDefSvc = sam.Owner.RefitFor<IRefitOwnerCircleDefinition>();
            var getSystemCircleDefinitionResponse = await circleDefSvc.GetCircleDefinition(SystemCircleConstants.ConfirmedConnectionsCircleId);
            Assert.That(getSystemCircleDefinitionResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getSystemCircleDefinitionResponse.Content, Is.Not.Null);
            var systemCircleDef = getSystemCircleDefinitionResponse.Content;

            //
            var samsConnetions = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var getFrodoInfoResponse = await samsConnetions.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity }, omitContactData: false);

            Assert.That(getFrodoInfoResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getFrodoInfoResponse.Content, Is.Not.Null, $"No status for {frodo.Identity} found");
            Assert.That(getFrodoInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

            var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
            var frodoAccessFromSystemCircle =
                frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId);
            Assert.That(frodoAccessFromSystemCircle, Is.Not.Null);

            AssertAllDrivesGrantedFromCircle(systemCircleDef, frodoAccessFromSystemCircle);

            // Frodo should show up in the member list for each circle
            //
            await AssertIdentityIsInCircle(sam.Owner, SystemCircleConstants.ConfirmedConnectionsCircleId, frodo.Identity);
        }


        //
        // Now connect to Frodo to see that sam is a connection with correct access
        //
        {
            var circleDefSvc = frodo.Owner.RefitFor<IRefitOwnerCircleDefinition>();
            var getSystemCircleDefinitionResponse = await circleDefSvc.GetCircleDefinition(SystemCircleConstants.ConfirmedConnectionsCircleId);
            Assert.That(getSystemCircleDefinitionResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getSystemCircleDefinitionResponse.Content, Is.Not.Null);
            var systemCircleDef = getSystemCircleDefinitionResponse.Content;

            //
            var frodoConnections = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var getSamInfoResponse = await frodoConnections.GetConnectionInfo(new OdinIdRequest() { OdinId = sam.Identity }, omitContactData: false);

            Assert.That(getSamInfoResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getSamInfoResponse.Content, Is.Not.Null, $"No status for {sam.Identity} found");
            Assert.That(getSamInfoResponse.Content.Status, Is.EqualTo(ConnectionStatus.Connected));

            var samAccess = getSamInfoResponse.Content.AccessGrant;
            var samAccessFromSystemCircle =
                samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId);
            Assert.That(samAccessFromSystemCircle, Is.Not.Null);

            AssertAllDrivesGrantedFromCircle(systemCircleDef, samAccessFromSystemCircle);

            // Frodo should show up in the member list for each circle
            //
            await AssertIdentityIsInCircle(frodo.Owner, SystemCircleConstants.ConfirmedConnectionsCircleId, sam.Identity);
        }

        await DisconnectIdentities(frodo, sam);
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>The original's <c>TestAppContext</c>, reduced to what these tests actually read.</summary>
    private sealed class TestApp
    {
        public OwnerSession Owner { get; init; }
        public ContactRequestData ContactData { get; init; }
        public OdinId Identity => Owner.Identity;
    }

    private static void AssertAllDrivesGrantedFromCircle(CircleDefinition circleDefinition, RedactedCircleGrant actual)
    {
        foreach (var circleDriveGrant in circleDefinition.DriveGrants)
        {
            //be sure it's in the list of granted drives; use Single to be sure it's only in there once
            var result = actual.DriveGrants.SingleOrDefault(x =>
                x.PermissionedDrive.Drive == circleDriveGrant.PermissionedDrive.Drive &&
                x.PermissionedDrive.Permission == circleDriveGrant.PermissionedDrive.Permission);
            Assert.That(result, Is.Not.Null);
        }
    }

    private static async Task AssertIdentityIsInCircle(OwnerSession owner, GuidId circleId, OdinId expectedIdentity)
    {
        var circleMemberSvc = owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
        var getCircleMemberResponse = await circleMemberSvc.GetCircleMembers(new GetCircleMembersRequest() { CircleId = circleId });
        Assert.That(getCircleMemberResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var members = getCircleMemberResponse.Content;
        Assert.That(members, Is.Not.Null);
        Assert.That(members, Is.Not.Empty);
        Assert.That(members, Has.Exactly(1).EqualTo(expectedIdentity));
    }

    private static async Task AssertConnectionStatus(OwnerSession owner, string odinId, ConnectionStatus expected)
    {
        var svc = owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
        var response = await svc.GetConnectionInfo(new OdinIdRequest() { OdinId = odinId });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null, $"No status for {odinId} found");
        Assert.That(response.Content.Status, Is.EqualTo(expected), $"{odinId} status does not match {expected}");
    }

    private async Task<(TestApp, TestApp, ConnectionRequestHeader)> CreateConnectionRequestFrodoToSam()
    {
        var frodoOwner = await LoginAsOwner(Identities.Frodo);
        var samOwner = await LoginAsOwner(Identities.Sam);
        return await CreateConnectionRequestFrodoToSam(frodoOwner, samOwner);
    }

    private async Task<(TestApp, TestApp, ConnectionRequestHeader)> CreateConnectionRequestFrodoToSam(
        OwnerSession frodoOwner,
        OwnerSession samOwner,
        CircleDefinition circleDefinition1 = null,
        CircleDefinition circleDefinition2 = null)
    {
        List<GuidId> cids = new List<GuidId>();
        if (null != circleDefinition1)
        {
            cids.Add(circleDefinition1.Id);
        }

        if (null != circleDefinition2)
        {
            cids.Add(circleDefinition2.Id);
        }

        var appTargetDrive = TargetDrive.NewTargetDrive();

        var circleMemberGrantRequest = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new()
                    {
                        Drive = appTargetDrive,
                        Permission = DrivePermission.All
                    }
                }
            }
        };

        Guid appId = Guid.NewGuid();
        var sender = await SetupTestSampleAppAsync(frodoOwner,
            TestIdentities.Frodo,
            appId,
            canReadConnections: true,
            targetDrive: appTargetDrive,
            authorizedCircles: cids.Select(c => c.Value).ToList(),
            circleMemberGrantRequest: circleMemberGrantRequest);

        var recipient = await SetupTestSampleAppAsync(samOwner,
            TestIdentities.Samwise,
            appId,
            canReadConnections: true,
            targetDrive: appTargetDrive,
            authorizedCircles: cids.Select(c => c.Value).ToList(),
            circleMemberGrantRequest: circleMemberGrantRequest);

        var id = Guid.NewGuid();
        var requestHeader = new ConnectionRequestHeader()
        {
            Id = id,
            Recipient = recipient.Identity,
            Message = "Please add me",
            CircleIds = cids,
            ContactData = sender.ContactData
        };

        //have frodo send it
        {
            var svc = sender.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var response = await svc.SendConnectionRequest(requestHeader);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content, Is.True, "Failed sending the request");
        }

        //check that sam got it
        {
            var svc = recipient.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();
            var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender.Identity });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            Assert.That(response.Content, Is.Not.Null, $"No request found from {sender.Identity}");
            Assert.That(response.Content.SenderOdinId, Is.EqualTo(sender.Identity.DomainName));
        }

        return (sender, recipient, requestHeader);
    }

    private static async Task AcceptConnectionRequest(TestApp sender, TestApp recipient)
    {
        var svc = recipient.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

        var header = new AcceptRequestHeader()
        {
            Sender = sender.Identity,
            CircleIds = new List<GuidId>(),
            ContactData = recipient.ContactData
        };

        var acceptResponse = await svc.AcceptConnectionRequest(header);
        Assert.That(acceptResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await AssertConnectionStatus(recipient.Owner, sender.Identity, ConnectionStatus.Connected);
    }

    private static async Task DeleteConnectionRequestsFromFrodoToSam(TestApp frodo, TestApp sam)
    {
        {
            var svc = sam.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var deleteResponse = await svc.DeletePendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
            Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        {
            var svc = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkRequests>();

            var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
            Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }

    private static async Task DisconnectIdentities(TestApp frodo, TestApp sam)
    {
        {
            var frodoConnections = frodo.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var disconnectResponse = await frodoConnections.Disconnect(new OdinIdRequest() { OdinId = sam.Identity });
            // Content is false when there was nothing connected to disconnect (e.g. the peer already
            // severed the connection via a prior reciprocal-disconnect notification) -- that's not a failure.
            Assert.That(disconnectResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await AssertConnectionStatus(frodo.Owner, sam.Identity, ConnectionStatus.None);
        }

        {
            var samConnections = sam.Owner.RefitFor<IRefitOwnerCircleNetworkConnections>();
            var disconnectResponse = await samConnections.Disconnect(new OdinIdRequest() { OdinId = frodo.Identity });
            // Content is false when there was nothing connected to disconnect (e.g. the peer already
            // severed the connection via a prior reciprocal-disconnect notification) -- that's not a failure.
            Assert.That(disconnectResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await AssertConnectionStatus(sam.Owner, frodo.Identity, ConnectionStatus.None);
        }
    }

    private async Task<CircleDefinition> CreateCircleWith2Drives(OwnerSession owner, string name, IEnumerable<int> permissionKeys)
    {
        var targetDrive1 = TargetDrive.NewTargetDrive();
        var targetDrive2 = TargetDrive.NewTargetDrive();

        await owner.Admin.CreateDrive(targetDrive1, $"Drive 1 for circle {name}", allowAnonymousReads: false);
        await owner.Admin.CreateDrive(targetDrive2, $"Drive 2 for circle {name}", allowAnonymousReads: false);

        {
            var svc = owner.RefitFor<IRefitOwnerCircleDefinition>();

            Guid someId = Guid.NewGuid();
            var dgr1 = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive1,
                    Permission = DrivePermission.ReadWrite
                }
            };

            var dgr2 = new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive2,
                    Permission = DrivePermission.Write
                }
            };

            var request = new CreateCircleRequest()
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = $"total hack {someId}",
                DriveGrants = new List<DriveGrantRequest>() { dgr1, dgr2 },
                Permissions = permissionKeys?.Any() ?? false ? new PermissionSet(permissionKeys?.ToArray()) : new PermissionSet()
            };

            var createCircleResponse = await svc.CreateCircleDefinition(request);
            Assert.That(createCircleResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
            Assert.That(getCircleDefinitionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var definitionList = getCircleDefinitionsResponse.Content;
            Assert.That(definitionList, Is.Not.Null);

            //grab the circle by the id we put in the description.  we don't have the newly created circle's id because i need to update the create circle method
            var circle = definitionList.Single(c => c.Description.Contains(someId.ToString()));

            Assert.That(circle.DriveGrants.SingleOrDefault(d => d == dgr1), Is.Not.Null);
            Assert.That(circle.DriveGrants.SingleOrDefault(d => d == dgr2), Is.Not.Null);

            foreach (var k in permissionKeys)
            {
                Assert.That(circle.Permissions.HasKey(k), Is.True, $"circle is missing permission key {k}");
            }

            Assert.That(circle.Name, Is.EqualTo(request.Name));
            Assert.That(circle.Description, Is.EqualTo(request.Description));
            Assert.That(circle.Permissions, Is.EqualTo(request.Permissions));

            return circle;
        }
    }

    /// <summary>
    /// Stand-in for <c>OwnerApiTestUtils.SetupTestSampleApp</c>: the app drive plus an app holding
    /// full permission on it. The original also registered an app client; no test here uses the
    /// resulting token, so that step is dropped.
    /// </summary>
    private static async Task<TestApp> SetupTestSampleAppAsync(
        OwnerSession owner,
        TestIdentity identity,
        Guid appId,
        bool canReadConnections = false,
        TargetDrive targetDrive = null,
        List<Guid> authorizedCircles = null,
        PermissionSetGrantRequest circleMemberGrantRequest = null)
    {
        targetDrive ??= TargetDrive.NewTargetDrive();

        var keys = new List<int>();
        if (canReadConnections)
        {
            keys.Add(PermissionKeys.ReadConnections);
            keys.Add(PermissionKeys.ReadConnectionRequests);
        }

        keys.Add(PermissionKeys.UseTransitWrite);

        await owner.Admin.CreateDrive(targetDrive, $"Test Drive name with type {targetDrive.Type}",
            allowAnonymousReads: false, ownerOnly: false);

        await owner.Admin.RegisterApp(appId,
            new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(keys.ToArray()),
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = targetDrive,
                            Permission = DrivePermission.All
                        }
                    }
                }
            },
            authorizedCircles,
            circleMemberGrantRequest);

        return new TestApp
        {
            Owner = owner,
            ContactData = identity.ContactData
        };
    }
}
