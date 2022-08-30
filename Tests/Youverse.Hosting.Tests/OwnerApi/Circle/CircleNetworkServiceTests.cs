using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Definition;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers;

namespace Youverse.Hosting.Tests.OwnerApi.Circle
{
    public class CircleNetworkServiceTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
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
            //runs before each test 
            //_scaffold.DeleteData(); 
        }

        [Test]
        public async Task CanSendConnectionRequestAndGetPendingRequest()
        {
            Guid appId = Guid.NewGuid();
            var sender = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Frodo, canManageConnections: true);
            var recipient = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canManageConnections: true);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sender.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var id = Guid.NewGuid();
                var requestHeader = new ConnectionRequestHeader()
                {
                    Id = id,
                    Recipient = recipient.Identity,
                    Message = "Please add me"
                };

                var response = await svc.SendConnectionRequest(requestHeader);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                Assert.IsTrue(response.Content, "Failed sending the request");
            }

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(recipient.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);
                var response = await svc.GetPendingRequest(new DotYouIdRequest() { DotYouId = sender.Identity });

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
                Assert.IsTrue(response.Content.SenderDotYouId == sender.Identity);
            }
        }

        [Test]
        public async Task CanDeleteConnectionRequest()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var deleteResponse = await svc.DeletePendingRequest(new DotYouIdRequest() { DotYouId = sam.Identity });
                Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(new DotYouIdRequest() { DotYouId = sam.Identity });
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
            }
        }

        [Test]
        public async Task CanGetPendingConnectionRequestList()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var response = await svc.GetPendingRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content);
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.SenderDotYouId == frodo.Identity), $"Could not find request from {frodo.Identity} in the results");
            }
        }

        [Test]
        public async Task CanGetSentConnectionRequestList()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            //Check Sam's list of sent requests
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var response = await svc.GetSentRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, "No result returned");
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Recipient == sam.Identity), $"Could not find request with recipient {sam.Identity} in the results");
            }
        }

        [Test]
        public async Task CanGetSentConnectionRequest()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var response = await svc.GetSentRequest(new DotYouIdRequest() { DotYouId = sam.Identity });

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, $"No request found with recipient [{sam.Identity}]");
                Assert.IsTrue(response.Content.Recipient == sam.Identity);
            }
        }


        [Test]
        public async Task CanAcceptConnectionRequest_AndAccessCirclePermissions()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            var circleDefinition1 = await this.CreateCircle(sam.Identity, "c1");
            var circleDefinition2 = await this.CreateCircle(sam.Identity, "c2");

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret))
            {
                var connectionRequestService = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var header = new AcceptRequestHeader()
                {
                    Sender = frodo.Identity,
                    CircleIds = new List<ByteArrayId>() { circleDefinition1.Id, circleDefinition2.Id }
                };

                var acceptResponse = await connectionRequestService.AcceptConnectionRequest(header);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                //
                // The pending request should be removed
                //
                var getResponse = await connectionRequestService.GetPendingRequest(new DotYouIdRequest() { DotYouId = frodo.Identity });
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with sender {frodo.Identity} still exists");

                //
                // Frodo should be in Sam's contacts network.
                //
                var samsConnetions = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var response = await samsConnetions.GetConnectionInfo(new DotYouIdRequest() { DotYouId = frodo.Identity });


                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {frodo.Identity}.  Status code was {response.StatusCode}");
                Assert.IsNotNull(response.Content, $"No status for {frodo.Identity} found");
                Assert.IsTrue(response.Content.Status == ConnectionStatus.Connected);

                var x = response.Content.AccessGrant;
            }

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret))
            {
                //
                // Sam should be in Frodo's contacts network
                //
                var frodoConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var response = await frodoConnections.GetConnectionInfo(new DotYouIdRequest() { DotYouId = sam.Identity });

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {sam.Identity}.  Status code was {response.StatusCode}");
                Assert.IsNotNull(response.Content, $"No status for {sam.Identity} found");
                Assert.IsTrue(response.Content.Status == ConnectionStatus.Connected);

                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);
                var getResponse = await svc.GetSentRequest(new DotYouIdRequest() { DotYouId = sam.Identity });
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - sent request to {sam.Identity} still exists");
            }

            await DisconnectIdentities(frodo, sam);
        }

        [Test]
        public async Task GrantCircle()
        {
            Assert.Inconclusive("TODO");
        }

        [Test]
        public async Task RevokeCircle()
        {
            Assert.Inconclusive("TODO");
        }

        [Test]
        public async Task CanBlock()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var header = new AcceptRequestHeader()
                {
                    Sender = frodo.Identity,
                    // CircleIds = new List<ByteArrayId>() { defaultCircleId }
                    CircleIds = new List<ByteArrayId>()
                };

                var acceptResponse = await svc.AcceptConnectionRequest(header);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Connected);

                var samConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var blockResponse = await samConnections.Block(new DotYouIdRequest() { DotYouId = frodo.Identity });

                Assert.IsTrue(blockResponse.IsSuccessStatusCode && blockResponse.Content, "failed to block");
                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Blocked);

                await samConnections.Unblock(new DotYouIdRequest() { DotYouId = frodo.Identity });
            }

            await DisconnectIdentities(frodo, sam);
        }

        [Test]
        public async Task CanUnblock()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var header = new AcceptRequestHeader()
                {
                    Sender = frodo.Identity,
                    // CircleIds = new List<ByteArrayId>() { defaultCircleId }
                    CircleIds = new List<ByteArrayId>()
                };

                var acceptResponse = await svc.AcceptConnectionRequest(header);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Connected);

                var samConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var blockResponse = await samConnections.Block(new DotYouIdRequest() { DotYouId = frodo.Identity });

                Assert.IsTrue(blockResponse.IsSuccessStatusCode && blockResponse.Content, "failed to block");
                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Blocked);

                var unblockResponse = await samConnections.Unblock(new DotYouIdRequest() { DotYouId = frodo.Identity });
                Assert.IsTrue(unblockResponse.IsSuccessStatusCode && unblockResponse.Content, "failed to unblock");
                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Connected);
            }

            await DisconnectIdentities(frodo, sam);
        }

        [Test]
        public async Task CanDisconnect()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var header = new AcceptRequestHeader()
                {
                    Sender = frodo.Identity,
                    // CircleIds = new List<ByteArrayId>() { defaultCircleId }
                    CircleIds = new List<ByteArrayId>()
                };

                var acceptResponse = await svc.AcceptConnectionRequest(header);
                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.Connected);

                var samConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var disconnectResponse = await samConnections.Disconnect(new DotYouIdRequest() { DotYouId = frodo.Identity });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, frodo.Identity, ConnectionStatus.None);
            }

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret))
            {
                var frodoConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var disconnectResponse = await frodoConnections.Disconnect(new DotYouIdRequest() { DotYouId = sam.Identity });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Samwise, ConnectionStatus.None);
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

        private async Task<(TestSampleAppContext, TestSampleAppContext)> CreateConnectionRequestFrodoToSam()
        {
            Guid appId = Guid.NewGuid();
            var sender = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Frodo, canManageConnections: true);
            var recipient = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canManageConnections: true);

            var circleDefinition1 = await this.CreateCircle(sender.Identity, "c1");
            var circleDefinition2 = await this.CreateCircle(sender.Identity, "c2");

            //have frodo send it
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sender.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var id = Guid.NewGuid();
                var requestHeader = new ConnectionRequestHeader()
                {
                    Id = id,
                    Recipient = recipient.Identity,
                    Message = "Please add me",
                    CircleIds = new List<ByteArrayId>()
                    {
                        circleDefinition1.Id,
                        circleDefinition2.Id
                    }
                };

                var response = await svc.SendConnectionRequest(requestHeader);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                Assert.IsTrue(response.Content, "Failed sending the request");
            }

            //check that sam got it
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(recipient.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);
                var response = await svc.GetPendingRequest(new DotYouIdRequest() { DotYouId = sender.Identity });

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
                Assert.IsTrue(response.Content.SenderDotYouId == sender.Identity);
            }

            return (sender, recipient);
        }

        private async Task DisconnectIdentities(TestSampleAppContext frodo, TestSampleAppContext sam)
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret))
            {
                var frodoConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var disconnectResponse = await frodoConnections.Disconnect(new DotYouIdRequest() { DotYouId = sam.Identity });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Samwise, ConnectionStatus.None);
            }

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret))
            {
                var samConnections = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var disconnectResponse = await samConnections.Disconnect(new DotYouIdRequest() { DotYouId = frodo.Identity });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Frodo, ConnectionStatus.None);
            }
        }

        private async Task<CircleDefinition> CreateCircle(DotYouIdentity identity, string name)
        {
            var targetDrive1 = TargetDrive.NewTargetDrive();
            var targetDrive2 = TargetDrive.NewTargetDrive();

            await _scaffold.OwnerApi.CreateDrive(identity, targetDrive1, $"Drive 1 for circle {name}", "", false);
            await _scaffold.OwnerApi.CreateDrive(identity, targetDrive2, $"Drive 2 for circle {name}", "", false);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                Guid someId = Guid.NewGuid();
                var dgr1 = new DriveGrantRequest()
                {
                    Drive = targetDrive1,
                    Permission = DrivePermission.ReadWrite
                };

                var dgr2 = new DriveGrantRequest()
                {
                    Drive = targetDrive1,
                    Permission = DrivePermission.Write
                };

                var request = new CreateCircleRequest()
                {
                    Name = name,
                    Description = $"total hack {someId}",
                    Drives = new List<DriveGrantRequest>() { dgr1, dgr2 },
                    Permissions = new PermissionSet(PermissionFlags.ReadConnectionRequests | PermissionFlags.ReadConnections)
                };

                var createCircleResponse = await svc.Create(request);
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                //grab the circle by the id we put in the description.  we don't have the newly created circle's id because i need to update the create circle method  
                var circle = definitionList.Single(c => c.Description.Contains(someId.ToString()));

                Assert.IsNotNull(circle.Drives.SingleOrDefault(d => d.Drive.Alias == dgr1.Drive.Alias && d.Drive.Type == dgr1.Drive.Type && d.Permission == dgr1.Permission));
                Assert.IsNotNull(circle.Drives.SingleOrDefault(d => d.Drive.Alias == dgr2.Drive.Alias && d.Drive.Type == dgr2.Drive.Type && d.Permission == dgr2.Permission));

                Assert.IsTrue(circle.Permissions.PermissionFlags.HasFlag(PermissionFlags.ReadConnectionRequests));
                Assert.IsTrue(circle.Permissions.PermissionFlags.HasFlag(PermissionFlags.ReadConnections));

                Assert.AreEqual(request.Name, circle.Name);
                Assert.AreEqual(request.Description, circle.Description);
                Assert.IsTrue(request.Permissions == circle.Permissions);

                return circle;
            }
        }
    }
}