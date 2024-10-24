using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Connections;

namespace Odin.Hosting.Tests.OwnerApi.Membership.Connections
{
    public class CircleNetworkServiceTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }


        [SetUp]
        public void Setup()
        {
            _scaffold.ClearAssertLogEventsAction();
            _scaffold.ClearLogEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _scaffold.AssertLogEvents();
        }


        [Test]
        public async Task FailToSendConnectionRequestToSelf()
        {
            Guid appId = Guid.NewGuid();
            var sender = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, TestIdentities.Frodo, canReadConnections: true);
            // var recipient = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canReadConnections: true);

            List<GuidId> cids = new List<GuidId>();

            var id = Guid.NewGuid();

            var requestHeader = new ConnectionRequestHeader()
            {
                Id = id,
                Recipient = sender.Identity,
                Message = "Please add me",
                CircleIds = cids
            };

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var response = await svc.SendConnectionRequest(requestHeader);

                Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest,
                    $"Should have failed sending the request to self.  Response code was [{response.StatusCode}]");
            }
        }

        [Test]
        public async Task CanSendConnectionRequestAndGetPendingRequest()
        {
            Guid appId = Guid.NewGuid();
            var sender = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, TestIdentities.Frodo, canReadConnections: true);
            var recipient = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canReadConnections: true);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var id = Guid.NewGuid();
                var requestHeader = new ConnectionRequestHeader()
                {
                    Id = id,
                    Recipient = recipient.Identity,
                    Message = "Please add me",
                    ContactData = sender.ContactData
                };

                var response = await svc.SendConnectionRequest(requestHeader);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                Assert.IsTrue(response.Content, "Failed sending the request");
            }

            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(recipient.Identity, out ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);
                var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender.Identity });

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
                Assert.IsTrue(response.Content.SenderOdinId == sender.Identity);

                Assert.IsTrue(response.Content.ContactData.Name == sender.ContactData.Name);
                Assert.IsTrue(response.Content.ContactData.ImageId == sender.ContactData.ImageId);
            }

            await DeleteConnectionRequestsFromFrodoToSam(sender, recipient);
        }

        [Test]
        public async Task CanDeletePendingConnectionRequest()
        {
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var deleteResponse = await svc.DeletePendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
                Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
            }

            await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
        }

        [Test]
        public async Task CanDeleteSentConnectionRequest()
        {
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
                Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
            }

            await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
        }

        [Test]
        public async Task CanGetPendingConnectionRequestList()
        {
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var response = await svc.GetPendingRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content);
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.SenderOdinId == frodo.Identity),
                    $"Could not find request from {frodo.Identity} in the results");

                Assert.IsTrue(response.Content.Results.All(r => r.Payload == null));
            }

            await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
        }

        [Test]
        public async Task CanGetPendingConnectionFullDetails()
        {
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                var request = response.Content;
                Assert.IsNotNull(request);

                Assert.IsTrue(request.Recipient == sam.Identity.DomainName);

                Assert.IsTrue(request.ReceivedTimestampMilliseconds > 0);
                Assert.IsTrue(request.Id != Guid.Empty);
                Assert.IsTrue(request.SenderOdinId == frodo.Identity.DomainName);
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
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var response = await svc.GetSentRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, "No result returned");
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Recipient == sam.Identity),
                    $"Could not find request with recipient {sam.Identity} in the results");
            }

            await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
        }

        [Test]
        public async Task CanGetSentConnectionRequest()
        {
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var response = await svc.GetSentRequest(new OdinIdRequest() { OdinId = sam.Identity });

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, $"No request found with recipient [{sam.Identity}]");
                Assert.IsTrue(response.Content.Recipient == sam.Identity);
            }

            await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
        }

        [Test]
        public async Task CanAcceptConnectionRequest_AndAccessCirclePermissions()
        {
            //basically create 2 circles on frodo's identity, then give sam access
            var circleOnFrodosIdentity1 =
                await this.CreateCircleWith2Drives(TestIdentities.Frodo.OdinId, "frodo c1",
                    new List<int>() { PermissionKeys.ReadConnections, PermissionKeys.ReadConnections });
            var circleOnFrodosIdentity2 =
                await this.CreateCircleWith2Drives(TestIdentities.Frodo.OdinId, "frodo c2", new List<int> { PermissionKeys.ReadCircleMembership });
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam(circleOnFrodosIdentity1, circleOnFrodosIdentity2);

            // create 2 circles on sam's identity and give frodo access
            var circleOnSamsIdentity1 = await this.CreateCircleWith2Drives(sam.Identity, "c1", new List<int>());
            var circleOnSamsIdentity2 =
                await this.CreateCircleWith2Drives(sam.Identity, "c2", new List<int> { PermissionKeys.ReadConnections, PermissionKeys.ReadConnections });

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
            {
                var connectionRequestService = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var header = new AcceptRequestHeader()
                {
                    Sender = frodo.Identity,
                    CircleIds = new List<GuidId>() { circleOnSamsIdentity1.Id, circleOnSamsIdentity2.Id },
                    ContactData = sam.ContactData
                };

                var acceptResponse = await connectionRequestService.AcceptConnectionRequest(header);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                //
                // The pending request should be removed
                //
                var getResponse = await connectionRequestService.GetPendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with sender {frodo.Identity} still exists");

                //
                // Frodo should be in Sam's contacts network.
                //
                var samsConnetions = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var getFrodoInfoResponse = await samsConnetions.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity }, omitContactData: false);

                Assert.IsTrue(getFrodoInfoResponse.IsSuccessStatusCode,
                    $"Failed to get status for {frodo.Identity}.  Status code was {getFrodoInfoResponse.StatusCode}");
                Assert.IsNotNull(getFrodoInfoResponse.Content, $"No status for {frodo.Identity} found");
                Assert.IsTrue(getFrodoInfoResponse.Content.Status == ConnectionStatus.Connected);

                //
                // Validate the contact data sent by frodo was set on his ICR on sam's identity
                //
                Assert.IsTrue(getFrodoInfoResponse.Content.OriginalContactData.Name == frodo.ContactData.Name);
                Assert.IsTrue(getFrodoInfoResponse.Content.OriginalContactData.ImageId == frodo.ContactData.ImageId);

                var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
                var frodoAccessFromCircle1 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity1.Id);
                Assert.NotNull(frodoAccessFromCircle1);
                Assert.IsTrue(frodoAccessFromCircle1.PermissionSet == circleOnSamsIdentity1.Permissions);

                AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity1, frodoAccessFromCircle1);

                var frodoAccessFromCircle2 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity2.Id);
                Assert.NotNull(frodoAccessFromCircle2);
                Assert.IsTrue(frodoAccessFromCircle2.PermissionSet == circleOnSamsIdentity2.Permissions);
                AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity2, frodoAccessFromCircle2);

                //
                // Frodo should show up in the member list for each circle
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity1.Id, frodo.Identity);
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity2.Id, frodo.Identity);
            }


            //
            // Now connect to Frodo to see that sam is a connection with correct access
            //
            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out ownerSharedSecret);
            {
                //
                // Sent request should be deleted
                //
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);
                var getSentRequestResponse = await svc.GetSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
                Assert.IsTrue(getSentRequestResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - sent request to {sam.Identity} still exists");

                //
                // Sam should be in Frodo's contacts network
                //
                var frodoConnections = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var getSamConnectionInfoResponse =
                    await frodoConnections.GetConnectionInfo(new OdinIdRequest() { OdinId = sam.Identity }, omitContactData: false);

                Assert.IsTrue(getSamConnectionInfoResponse.IsSuccessStatusCode,
                    $"Failed to get status for {sam.Identity}.  Status code was {getSamConnectionInfoResponse.StatusCode}");
                Assert.IsNotNull(getSamConnectionInfoResponse.Content, $"No status for {sam.Identity} found");
                Assert.IsTrue(getSamConnectionInfoResponse.Content.Status == ConnectionStatus.Connected);

                //
                // Validate the contact data sent by sam was set on his ICR on frodo's identity
                //
                Assert.IsTrue(getSamConnectionInfoResponse.Content.OriginalContactData.Name == sam.ContactData.Name);
                Assert.IsTrue(getSamConnectionInfoResponse.Content.OriginalContactData.ImageId == sam.ContactData.ImageId);

                var samAccess = getSamConnectionInfoResponse.Content.AccessGrant;
                var samAccessFromCircle1 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity1.Id);
                Assert.NotNull(samAccessFromCircle1);
                Assert.IsTrue(samAccessFromCircle1.PermissionSet == circleOnFrodosIdentity1.Permissions);

                AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity1, samAccessFromCircle1);

                var samAccessFromCircle2 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity2.Id);
                Assert.NotNull(samAccessFromCircle2);
                Assert.IsTrue(samAccessFromCircle2.PermissionSet == circleOnFrodosIdentity2.Permissions);
                AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity2, samAccessFromCircle2);

                //
                // Sam should show up in the member list for each circle
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnFrodosIdentity1.Id, sam.Identity);
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnFrodosIdentity2.Id, sam.Identity);
            }

            await DisconnectIdentities(frodo, sam);
        }


        [Test]
        public async Task GrantCircle()
        {
            #region Firstly, setup connections and put into circles

            var circleOnFrodosIdentity1 = await this.CreateCircleWith2Drives(TestIdentities.Frodo.OdinId, "frodo c1", new List<int>());
            var circleOnFrodosIdentity2 =
                await this.CreateCircleWith2Drives(TestIdentities.Frodo.OdinId, "frodo c2", new List<int>() { PermissionKeys.ReadConnections });
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam(circleOnFrodosIdentity1, circleOnFrodosIdentity2);

            // create 2 circles on sam's identity and give frodo access
            var circleOnSamsIdentity1 = await this.CreateCircleWith2Drives(sam.Identity, "c1", new List<int>() { PermissionKeys.ReadCircleMembership });
            var circleOnSamsIdentity2 = await this.CreateCircleWith2Drives(sam.Identity, "c2", new List<int>());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
            {
                var connectionRequestService = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var header = new AcceptRequestHeader()
                {
                    Sender = frodo.Identity,
                    CircleIds = new List<GuidId>() { circleOnSamsIdentity1.Id, circleOnSamsIdentity2.Id },
                    ContactData = sam.ContactData
                };

                var acceptResponse = await connectionRequestService.AcceptConnectionRequest(header);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                //
                // The pending request should be removed
                //
                var getResponse = await connectionRequestService.GetPendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with sender {frodo.Identity} still exists");

                //
                // Frodo should be in Sam's contacts network.
                //
                var samsConnetionsService = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var getFrodoInfoResponse = await samsConnetionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity });

                Assert.IsTrue(getFrodoInfoResponse.IsSuccessStatusCode,
                    $"Failed to get status for {frodo.Identity}.  Status code was {getFrodoInfoResponse.StatusCode}");
                Assert.IsNotNull(getFrodoInfoResponse.Content, $"No status for {frodo.Identity} found");
                Assert.IsTrue(getFrodoInfoResponse.Content.Status == ConnectionStatus.Connected);

                var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
                var frodoAccessFromCircle1 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity1.Id);
                Assert.NotNull(frodoAccessFromCircle1);
                Assert.IsTrue(frodoAccessFromCircle1.PermissionSet == circleOnSamsIdentity1.Permissions);

                AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity1, frodoAccessFromCircle1);

                var frodoAccessFromCircle2 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity2.Id);
                Assert.NotNull(frodoAccessFromCircle2);
                Assert.IsTrue(frodoAccessFromCircle2.PermissionSet == circleOnSamsIdentity2.Permissions);
                AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity2, frodoAccessFromCircle2);

                //
                // Frodo should show up in the member list for each circle
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity1.Id, frodo.Identity);
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity2.Id, frodo.Identity);
            }


            //now connect to Frodo to see that sam is a connection with correct access
            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out ownerSharedSecret);
            {
                //
                // Sent request should be deleted
                //
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);
                var getSentRequestResponse = await svc.GetSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
                Assert.IsTrue(getSentRequestResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - sent request to {sam.Identity} still exists");

                //
                // Sam should be in Frodo's contacts network
                //
                var frodoConnections = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var getSamConnectionInfoResponse = await frodoConnections.GetConnectionInfo(new OdinIdRequest() { OdinId = sam.Identity });

                Assert.IsTrue(getSamConnectionInfoResponse.IsSuccessStatusCode,
                    $"Failed to get status for {sam.Identity}.  Status code was {getSamConnectionInfoResponse.StatusCode}");
                Assert.IsNotNull(getSamConnectionInfoResponse.Content, $"No status for {sam.Identity} found");
                Assert.IsTrue(getSamConnectionInfoResponse.Content.Status == ConnectionStatus.Connected);

                var samAccess = getSamConnectionInfoResponse.Content.AccessGrant;
                var samAccessFromCircle1 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity1.Id);
                Assert.NotNull(samAccessFromCircle1);
                Assert.IsTrue(samAccessFromCircle1.PermissionSet == circleOnFrodosIdentity1.Permissions);

                AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity1, samAccessFromCircle1);

                var samAccessFromCircle2 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity2.Id);
                Assert.NotNull(samAccessFromCircle2);
                Assert.IsTrue(samAccessFromCircle2.PermissionSet == circleOnFrodosIdentity2.Permissions);
                AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity2, samAccessFromCircle2);

                //
                // Sam should show up in the member list for each circle
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnFrodosIdentity1.Id, sam.Identity);
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnFrodosIdentity2.Id, sam.Identity);
            }

            #endregion

            //
            // Create a new circle and grant frodo access circle access
            //
            var newCircleDefinitionOnSamsIdentity =
                await this.CreateCircleWith2Drives(sam.Identity, "newly created circle", new List<int>() { PermissionKeys.ReadConnections });

            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out ownerSharedSecret);
            {
                //
                // Frodo should show in both original circles
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity1.Id, frodo.Identity);
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity2.Id, frodo.Identity);

                //
                // Add Frodo to newCircleDefinitionOnSamsIdentity
                //
                var circleMemberSvc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var addMemberResponse = await circleMemberSvc.AddCircle(new AddCircleMembershipRequest()
                {
                    CircleId = newCircleDefinitionOnSamsIdentity.Id,
                    OdinId = frodo.Identity
                });

                Assert.IsTrue(addMemberResponse.IsSuccessStatusCode, $"Actual status code {addMemberResponse.StatusCode}");

                //
                // Frodo should be in 3 circles
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, newCircleDefinitionOnSamsIdentity.Id, frodo.Identity);
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity2.Id, frodo.Identity);
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity2.Id, frodo.Identity);

                //
                // Get frodo's connection info to see he s been given access to the new circle's drives
                //
                var samsConnectionsService = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var getFrodoInfoResponse = await samsConnectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity });

                Assert.IsTrue(getFrodoInfoResponse.IsSuccessStatusCode,
                    $"Failed to get status for {frodo.Identity}.  Status code was {getFrodoInfoResponse.StatusCode}");
                Assert.IsNotNull(getFrodoInfoResponse.Content, $"No status for {frodo.Identity} found");
                Assert.IsTrue(getFrodoInfoResponse.Content.Status == ConnectionStatus.Connected);

                // frodo should have access to the new circle
                var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
                var frodoAccessFromNewCircle = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == newCircleDefinitionOnSamsIdentity.Id);
                Assert.NotNull(frodoAccessFromNewCircle);
                AssertAllDrivesGrantedFromCircle(newCircleDefinitionOnSamsIdentity, frodoAccessFromNewCircle);

                // frodo should still access to circle 1
                var frodoAccessFromCircle1 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity1.Id);
                Assert.NotNull(frodoAccessFromCircle1);
                Assert.IsTrue(frodoAccessFromCircle1.PermissionSet == circleOnSamsIdentity1.Permissions);
                AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity1, frodoAccessFromCircle1);

                // frodo should still access to circle 2
                var frodoAccessFromCircle2 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity2.Id);
                Assert.NotNull(frodoAccessFromCircle2);
                Assert.IsTrue(frodoAccessFromCircle2.PermissionSet == circleOnSamsIdentity2.Permissions);
                AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity2, frodoAccessFromCircle2);
            }


            await DisconnectIdentities(frodo, sam);
        }

        [Test]
        public async Task RevokeCircle()
        {
            #region Firstly, setup connections and put into circles

            var circleOnFrodosIdentity1 = await this.CreateCircleWith2Drives(TestIdentities.Frodo.OdinId, "frodo c1", new List<int>());
            var circleOnFrodosIdentity2 =
                await this.CreateCircleWith2Drives(TestIdentities.Frodo.OdinId, "frodo c2", new List<int>() { PermissionKeys.ReadConnections });
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam(circleOnFrodosIdentity1, circleOnFrodosIdentity2);

            // create 2 circles on sam's identity and give frodo access
            var circleOnSamsIdentity1 = await this.CreateCircleWith2Drives(sam.Identity, "c1", new List<int>() { PermissionKeys.ReadCircleMembership });
            var circleOnSamsIdentity2 = await this.CreateCircleWith2Drives(sam.Identity, "c2", new List<int>());

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
            {
                var connectionRequestService = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var header = new AcceptRequestHeader()
                {
                    Sender = frodo.Identity,
                    CircleIds = new List<GuidId>() { circleOnSamsIdentity1.Id, circleOnSamsIdentity2.Id },
                    ContactData = sam.ContactData
                };

                var acceptResponse = await connectionRequestService.AcceptConnectionRequest(header);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                //
                // The pending request should be removed
                //
                var getResponse = await connectionRequestService.GetPendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with sender {frodo.Identity} still exists");

                //
                // Frodo should be in Sam's contacts network.
                //
                var samsConnetionsService = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var getFrodoInfoResponse = await samsConnetionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity });

                Assert.IsTrue(getFrodoInfoResponse.IsSuccessStatusCode,
                    $"Failed to get status for {frodo.Identity}.  Status code was {getFrodoInfoResponse.StatusCode}");
                Assert.IsNotNull(getFrodoInfoResponse.Content, $"No status for {frodo.Identity} found");
                Assert.IsTrue(getFrodoInfoResponse.Content.Status == ConnectionStatus.Connected);

                var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
                var frodoAccessFromCircle1 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity1.Id);
                Assert.NotNull(frodoAccessFromCircle1);
                Assert.IsTrue(frodoAccessFromCircle1.PermissionSet == circleOnSamsIdentity1.Permissions);

                AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity1, frodoAccessFromCircle1);

                var frodoAccessFromCircle2 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity2.Id);
                Assert.NotNull(frodoAccessFromCircle2);
                Assert.IsTrue(frodoAccessFromCircle2.PermissionSet == circleOnSamsIdentity2.Permissions);
                AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity2, frodoAccessFromCircle2);

                //
                // Frodo should show up in the member list for each circle
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity1.Id, frodo.Identity);
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity2.Id, frodo.Identity);
            }


            //now connect to Frodo to see that sam is a connection with correct access
            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out ownerSharedSecret);
            {
                //
                // Sent request should be deleted
                //
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);
                var getSentRequestResponse = await svc.GetSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
                Assert.IsTrue(getSentRequestResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - sent request to {sam.Identity} still exists");

                //
                // Sam should be in Frodo's contacts network
                //
                var frodoConnections = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var getSamConnectionInfoResponse = await frodoConnections.GetConnectionInfo(new OdinIdRequest() { OdinId = sam.Identity });

                Assert.IsTrue(getSamConnectionInfoResponse.IsSuccessStatusCode,
                    $"Failed to get status for {sam.Identity}.  Status code was {getSamConnectionInfoResponse.StatusCode}");
                Assert.IsNotNull(getSamConnectionInfoResponse.Content, $"No status for {sam.Identity} found");
                Assert.IsTrue(getSamConnectionInfoResponse.Content.Status == ConnectionStatus.Connected);

                var samAccess = getSamConnectionInfoResponse.Content.AccessGrant;
                var samAccessFromCircle1 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity1.Id);
                Assert.NotNull(samAccessFromCircle1);
                Assert.IsTrue(samAccessFromCircle1.PermissionSet == circleOnFrodosIdentity1.Permissions);

                AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity1, samAccessFromCircle1);

                var samAccessFromCircle2 = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnFrodosIdentity2.Id);
                Assert.NotNull(samAccessFromCircle2);
                Assert.IsTrue(samAccessFromCircle2.PermissionSet == circleOnFrodosIdentity2.Permissions);
                AssertAllDrivesGrantedFromCircle(circleOnFrodosIdentity2, samAccessFromCircle2);

                //
                // Sam should show up in the member list for each circle
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnFrodosIdentity1.Id, sam.Identity);
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnFrodosIdentity2.Id, sam.Identity);
            }

            #endregion

            //
            // Revoke circle access
            //
            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out ownerSharedSecret);
            {
                var revokedCircle = circleOnSamsIdentity1;

                //
                // Frodo should show in both circles
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, revokedCircle.Id, frodo.Identity);
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity2.Id, frodo.Identity);

                //
                // Revoke circleOnSamsIdentity1 from frodo
                //
                var circleMemberSvc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var removeMembersResponse = await circleMemberSvc.RevokeCircle(new RevokeCircleMembershipRequest()
                {
                    CircleId = revokedCircle.Id,
                    OdinId = frodo.Identity
                });

                Assert.IsTrue(removeMembersResponse.IsSuccessStatusCode, $"Actual status code {removeMembersResponse.StatusCode}");

                //
                // Frodo should not be in the revoked circle
                //
                var getCircleMemberResponse = await circleMemberSvc.GetCircleMembers(new GetCircleMembersRequest() { CircleId = revokedCircle.Id });
                Assert.IsTrue(getCircleMemberResponse.IsSuccessStatusCode, $"Actual status code {getCircleMemberResponse.StatusCode}");

                var members = getCircleMemberResponse.Content;
                Assert.NotNull(members);
                Assert.IsTrue(members.All(m => m != frodo.Identity));

                //
                // Frodo should still be in the second circle
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, circleOnSamsIdentity2.Id, frodo.Identity);

                //
                // Get frodo's connection info to see he's no longer has the drives for this circle
                //
                var samsConnectionsService = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var getFrodoInfoResponse = await samsConnectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity });

                Assert.IsTrue(getFrodoInfoResponse.IsSuccessStatusCode,
                    $"Failed to get status for {frodo.Identity}.  Status code was {getFrodoInfoResponse.StatusCode}");
                Assert.IsNotNull(getFrodoInfoResponse.Content, $"No status for {frodo.Identity} found");
                Assert.IsTrue(getFrodoInfoResponse.Content.Status == ConnectionStatus.Connected);

                var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
                var frodoAccessFromCircle1 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == revokedCircle.Id);
                Assert.Null(frodoAccessFromCircle1);

                // frodo should still access to circle 2
                var frodoAccessFromCircle2 = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == circleOnSamsIdentity2.Id);
                Assert.NotNull(frodoAccessFromCircle2);
                Assert.IsTrue(frodoAccessFromCircle2.PermissionSet == circleOnSamsIdentity2.Permissions);
                AssertAllDrivesGrantedFromCircle(circleOnSamsIdentity2, frodoAccessFromCircle2);
            }


            await DisconnectIdentities(frodo, sam);
        }


        [Test]
        public async Task CanBlock()
        {
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var header = new AcceptRequestHeader()
                {
                    Sender = frodo.Identity,
                    CircleIds = new List<GuidId>(),
                    ContactData = sam.ContactData
                };

                var acceptResponse = await svc.AcceptConnectionRequest(header);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Connected);

                var samConnections = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var blockResponse = await samConnections.Block(new OdinIdRequest() { OdinId = frodo.Identity });

                Assert.IsTrue(blockResponse.IsSuccessStatusCode && blockResponse.Content, "failed to block");
                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Blocked);

                await samConnections.Unblock(new OdinIdRequest() { OdinId = frodo.Identity });
            }

            await DisconnectIdentities(frodo, sam);
        }

        [Test]
        public async Task CanUnblock()
        {
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var header = new AcceptRequestHeader()
                {
                    Sender = frodo.Identity,
                    CircleIds = new List<GuidId>(),
                    ContactData = sam.ContactData
                };

                var acceptResponse = await svc.AcceptConnectionRequest(header);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Connected);

                var samConnections = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var blockResponse = await samConnections.Block(new OdinIdRequest() { OdinId = frodo.Identity });

                Assert.IsTrue(blockResponse.IsSuccessStatusCode && blockResponse.Content, "failed to block");
                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Blocked);

                var unblockResponse = await samConnections.Unblock(new OdinIdRequest() { OdinId = frodo.Identity });
                Assert.IsTrue(unblockResponse.IsSuccessStatusCode && unblockResponse.Content, "failed to unblock");
                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Connected);
            }

            await DisconnectIdentities(frodo, sam);
        }

        [Test]
        public async Task CanDisconnect()
        {
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var header = new AcceptRequestHeader()
                {
                    Sender = frodo.Identity,
                    CircleIds = new List<GuidId>(),
                    ContactData = sam.ContactData
                };

                var acceptResponse = await svc.AcceptConnectionRequest(header);
                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Connected);

                var samConnections = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var disconnectResponse = await samConnections.Disconnect(new OdinIdRequest() { OdinId = frodo.Identity });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.None);
            }

            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out ownerSharedSecret);
            {
                var frodoConnections = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var disconnectResponse = await frodoConnections.Disconnect(new OdinIdRequest() { OdinId = sam.Identity });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Samwise.OdinId, ConnectionStatus.None);
            }
        }


        [Test(Description = "All connected identities go into the system circle")]
        public async Task ConnectedIdentitiesAreInSystemCircleUponApproval()
        {
            var (frodo, sam, _) = await CreateConnectionRequestFrodoToSam();

            await AcceptConnectionRequest(sender: frodo, recipient: sam);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
            {
                var circleDefSvc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);
                var getSystemCircleDefinitionResponse = await circleDefSvc.GetCircleDefinition(SystemCircleConstants.ConnectedIdentitiesSystemCircleId);
                Assert.IsTrue(getSystemCircleDefinitionResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getSystemCircleDefinitionResponse.Content);
                var systemCircleDef = getSystemCircleDefinitionResponse.Content;

                //
                var samsConnetions = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var getFrodoInfoResponse = await samsConnetions.GetConnectionInfo(new OdinIdRequest() { OdinId = frodo.Identity }, omitContactData: false);

                Assert.IsTrue(getFrodoInfoResponse.IsSuccessStatusCode,
                    $"Failed to get status for {frodo.Identity}.  Status code was {getFrodoInfoResponse.StatusCode}");
                Assert.IsNotNull(getFrodoInfoResponse.Content, $"No status for {frodo.Identity} found");
                Assert.IsTrue(getFrodoInfoResponse.Content.Status == ConnectionStatus.Connected);

                var frodoAccess = getFrodoInfoResponse.Content.AccessGrant;
                var frodoAccessFromSystemCircle = frodoAccess.CircleGrants.SingleOrDefault(c => c.CircleId == SystemCircleConstants.ConnectedIdentitiesSystemCircleId);
                Assert.NotNull(frodoAccessFromSystemCircle);

                AssertAllDrivesGrantedFromCircle(systemCircleDef, frodoAccessFromSystemCircle);

                //check if system drives exist
                // var configSvc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);
                // var getSystemDrivesResponse = await configSvc.GetSystemDrives();
                // Assert.IsNotNull(getSystemDrivesResponse.Content, "No system drives defined");
                // Assert.IsTrue(getSystemDrivesResponse.IsSuccessStatusCode, "No system drives defined");
                // Assert.IsTrue(getSystemDrivesResponse.Content.TryGetValue("contact", out var contactDrive), "contact system drive should have returned");
                // Assert.IsTrue(getSystemDrivesResponse.Content.TryGetValue("profile", out var standardProfileDrive), "standardProfileDrive should have returned");

                // Frodo should show up in the member list for each circle
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, SystemCircleConstants.ConnectedIdentitiesSystemCircleId, frodo.Identity);
            }


            //
            // Now connect to Frodo to see that sam is a connection with correct access
            //
            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out ownerSharedSecret);
            {
                var circleDefSvc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);
                var getSystemCircleDefinitionResponse = await circleDefSvc.GetCircleDefinition(SystemCircleConstants.ConnectedIdentitiesSystemCircleId);
                Assert.IsTrue(getSystemCircleDefinitionResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getSystemCircleDefinitionResponse.Content);
                var systemCircleDef = getSystemCircleDefinitionResponse.Content;

                //
                var frodoConnections = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
                var getSamInfoResponse = await frodoConnections.GetConnectionInfo(new OdinIdRequest() { OdinId = sam.Identity }, omitContactData: false);

                Assert.IsTrue(getSamInfoResponse.IsSuccessStatusCode,
                    $"Failed to get status for {sam.Identity}.  Status code was {getSamInfoResponse.StatusCode}");
                Assert.IsNotNull(getSamInfoResponse.Content, $"No status for {sam.Identity} found");
                Assert.IsTrue(getSamInfoResponse.Content.Status == ConnectionStatus.Connected);

                var samAccess = getSamInfoResponse.Content.AccessGrant;
                var samAccessFromSystemCircle = samAccess.CircleGrants.SingleOrDefault(c => c.CircleId == SystemCircleConstants.ConnectedIdentitiesSystemCircleId);
                Assert.NotNull(samAccessFromSystemCircle);

                AssertAllDrivesGrantedFromCircle(systemCircleDef, samAccessFromSystemCircle);

                //check if system drives exist
                // var configSvc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);
                // var getSystemDrivesResponse = await configSvc.GetSystemDrives();
                // Assert.IsNotNull(getSystemDrivesResponse.Content, "No system drives defined");
                // Assert.IsTrue(getSystemDrivesResponse.IsSuccessStatusCode, "No system drives defined");
                // Assert.IsTrue(getSystemDrivesResponse.Content.TryGetValue("contact", out var contactDrive), "contact system drive should have returned");
                // Assert.IsTrue(getSystemDrivesResponse.Content.TryGetValue("profile", out var standardProfileDrive), "standardProfileDrive should have returned");

                // Frodo should show up in the member list for each circle
                //
                await AssertIdentityIsInCircle(client, ownerSharedSecret, SystemCircleConstants.ConnectedIdentitiesSystemCircleId, sam.Identity);
            }

            await DisconnectIdentities(frodo, sam);
        }

        private void AssertAllDrivesGrantedFromCircle(CircleDefinition circleDefinition, RedactedCircleGrant actual)
        {
            foreach (var circleDriveGrant in circleDefinition.DriveGrants)
            {
                //be sure it's in the list of granted drives; use Single to be sure it's only in there once
                var result = actual.DriveGrants.SingleOrDefault(x =>
                    x.PermissionedDrive.Drive == circleDriveGrant.PermissionedDrive.Drive &&
                    x.PermissionedDrive.Permission == circleDriveGrant.PermissionedDrive.Permission);
                Assert.NotNull(result);
            }
        }

        private async Task AssertIdentityIsInCircle(HttpClient client, SensitiveByteArray ownerSharedSecret, GuidId circleId, OdinId expectedIdentity)
        {
            var circleMemberSvc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var getCircleMemberResponse = await circleMemberSvc.GetCircleMembers(new GetCircleMembersRequest() { CircleId = circleId });
            Assert.IsTrue(getCircleMemberResponse.IsSuccessStatusCode, $"Actual status code {getCircleMemberResponse.StatusCode}");
            var members = getCircleMemberResponse.Content;
            Assert.NotNull(members);
            Assert.IsTrue(members.Any());
            Assert.IsFalse(members.SingleOrDefault(m => m == expectedIdentity).DomainName == null);
        }

        private async Task AssertConnectionStatus(HttpClient client, SensitiveByteArray ownerSharedSecret, string odinId, ConnectionStatus expected)
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
            var response = await svc.GetConnectionInfo(new OdinIdRequest() { OdinId = odinId });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {odinId}.  Status code was {response.StatusCode}");
            Assert.IsNotNull(response.Content, $"No status for {odinId} found");
            Assert.IsTrue(response.Content.Status == expected, $"{odinId} status does not match {expected}");
        }

        private async Task<(TestAppContext, TestAppContext, ConnectionRequestHeader)> CreateConnectionRequestFrodoToSam(
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
            var sender = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId,
                TestIdentities.Frodo,
                canReadConnections: true,
                targetDrive: appTargetDrive,
                driveAllowAnonymousReads: false,
                ownerOnlyDrive: false,
                authorizedCircles: cids.Select(c => c.Value).ToList(),
                circleMemberGrantRequest: circleMemberGrantRequest);

            var recipient = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId,
                TestIdentities.Samwise,
                canReadConnections: true,
                targetDrive: appTargetDrive,
                driveAllowAnonymousReads: false,
                ownerOnlyDrive: false,
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

        private async Task AcceptConnectionRequest(TestAppContext sender, TestAppContext recipient)
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

        private async Task DeleteConnectionRequestsFromFrodoToSam(TestAppContext frodo, TestAppContext sam)
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

        private async Task DisconnectIdentities(TestAppContext frodo, TestAppContext sam)
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

        private async Task<CircleDefinition> CreateCircleWith2Drives(OdinId identity, string name, IEnumerable<int> permissionKeys)
        {
            var targetDrive1 = TargetDrive.NewTargetDrive();
            var targetDrive2 = TargetDrive.NewTargetDrive();

            await _scaffold.OldOwnerApi.CreateDrive(identity, targetDrive1, $"Drive 1 for circle {name}", "", false);
            await _scaffold.OldOwnerApi.CreateDrive(identity, targetDrive2, $"Drive 2 for circle {name}", "", false);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

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
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                //grab the circle by the id we put in the description.  we don't have the newly created circle's id because i need to update the create circle method
                var circle = definitionList.Single(c => c.Description.Contains(someId.ToString()));

                Assert.IsNotNull(circle.DriveGrants.SingleOrDefault(d => d == dgr1));
                Assert.IsNotNull(circle.DriveGrants.SingleOrDefault(d => d == dgr2));

                foreach (var k in permissionKeys)
                {
                    Assert.IsTrue(circle.Permissions.HasKey(k));
                }

                Assert.AreEqual(request.Name, circle.Name);
                Assert.AreEqual(request.Description, circle.Description);
                Assert.IsTrue(request.Permissions == circle.Permissions);

                return circle;
            }
        }
    }
}