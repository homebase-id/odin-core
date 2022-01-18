using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Hosting.Tests.ApiClient;

namespace Youverse.Hosting.Tests.AppAPI.Circle
{
    public class CircleNetworkServiceTests
    {
        private TestScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new TestScaffold(folder);
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
            var sender = await _scaffold.SetupTestSampleApp(appId, TestIdentities.Frodo, canManageConnections: true);
            var recipient = await _scaffold.SetupTestSampleApp(sender.AppId, TestIdentities.Samwise, canManageConnections: true);

            using (var client = _scaffold.CreateAppApiHttpClient(sender))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var id = Guid.NewGuid();
                var requestHeader = new ConnectionRequestHeader()
                {
                    Id = id,
                    Recipient = recipient.Identity,
                    Message = "Please add me"
                };

                var response = await svc.SendConnectionRequest(requestHeader);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                Assert.IsTrue(response.Content.Success, "Failed sending the request");
            }

            using (var client = _scaffold.CreateAppApiHttpClient(recipient))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
                var response = await svc.GetPendingRequest(sender.Identity);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
                Assert.IsTrue(response.Content.SenderDotYouId == sender.Identity);
            }

//            await DisconnectSamAndFrodo();
        }

        [Test]
        public async Task FailsWhenCannotManageConnections()
        {
            Guid appId = Guid.NewGuid();
            var sender = await _scaffold.SetupTestSampleApp(appId, TestIdentities.Frodo, canManageConnections: false);
            var recipient = await _scaffold.SetupTestSampleApp(sender.AppId, TestIdentities.Samwise, canManageConnections: false);

            using (var client = _scaffold.CreateAppApiHttpClient(sender))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var id = Guid.NewGuid();
                var requestHeader = new ConnectionRequestHeader()
                {
                    Id = id,
                    Recipient = recipient.Identity,
                    Message = "Please add me"
                };

                var response = await svc.SendConnectionRequest(requestHeader);
                Assert.IsFalse(response.IsSuccessStatusCode, response.ReasonPhrase);
            }

            using (var client = _scaffold.CreateAppApiHttpClient(recipient))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
                var response = await svc.GetPendingRequest(sender.Identity);
                Assert.IsFalse(response.IsSuccessStatusCode, response.ReasonPhrase);
            }

//            await DisconnectSamAndFrodo();
        }

        [Test]
        [Ignore("Need to convert to transit protocol")]
        public async Task CanDeleteConnectionRequest()
        {
            await CreateConnectionRequestSamToFrodo();

            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var deleteResponse = await svc.DeletePendingRequest(TestIdentities.Samwise);
                Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(TestIdentities.Samwise);
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {TestIdentities.Samwise} still exists");
            }

            await DisconnectSamAndFrodo();
        }

        [Test]
        [Ignore("Need to convert to transit protocol")]
        public async Task CanGetPendingConnectionRequestList()
        {
            await CreateConnectionRequestSamToFrodo();

            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var response = await svc.GetPendingRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.SenderDotYouId == TestIdentities.Samwise), $"Could not find request from {TestIdentities.Samwise} in the results");
            }

            await DisconnectSamAndFrodo();
        }

        [Test]
        [Ignore("Need to convert to transit protocol")]
        public async Task CanGetSentConnectionRequestList()
        {
            await CreateConnectionRequestSamToFrodo();

            //Check Sam's list of sent requests
            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Samwise))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var response = await svc.GetSentRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, "No result returned");
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Recipient == TestIdentities.Frodo), $"Could not find request with recipient {TestIdentities.Frodo} in the results");
            }

            await DisconnectSamAndFrodo();
        }


        [Test]
        [Ignore("Need to convert to transit protocol")]
        public async Task CanGetSentConnectionRequest()
        {
            await CreateConnectionRequestSamToFrodo();

            //Check Sam's list of sent requests
            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Samwise))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var response = await svc.GetSentRequest(TestIdentities.Frodo);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, $"No request found with recipient [{TestIdentities.Frodo}]");
                Assert.IsTrue(response.Content.Recipient == TestIdentities.Frodo);
            }

            await DisconnectSamAndFrodo();
        }

        [Test]
        [Ignore("Need to convert to transit protocol")]
        public async Task CanAcceptConnectionRequest()
        {
            await CreateConnectionRequestSamToFrodo();

            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(TestIdentities.Samwise);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                //
                // The pending request should be removed
                //
                var getResponse = await svc.GetPendingRequest(TestIdentities.Samwise);
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with sender {TestIdentities.Samwise} still exists");

                //
                // Sam should be in scaffold.Frodo's contacts network.
                //
                var frodoConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var response = await frodoConnections.GetStatus(TestIdentities.Samwise);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {TestIdentities.Samwise}.  Status code was {response.StatusCode}");
                Assert.IsNotNull(response.Content, $"No status for {TestIdentities.Samwise} found");
                Assert.IsTrue(response.Content.Status == ConnectionStatus.Connected);
            }

            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Samwise))
            {
                //
                // Frodo should be in sam's contacts network
                //
                var samConnections = RestService.For<ICircleNetworkConnectionsClient>(client);

                var response = await samConnections.GetStatus(TestIdentities.Frodo);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {TestIdentities.Frodo}.  Status code was {response.StatusCode}");
                Assert.IsNotNull(response.Content, $"No status for {TestIdentities.Frodo} found");
                Assert.IsTrue(response.Content.Status == ConnectionStatus.Connected);
            }

            await DisconnectSamAndFrodo();
        }


        [Test]
        [Ignore("Need to convert to transit protocol")]
        public async Task CanBlock()
        {
            await CreateConnectionRequestSamToFrodo();

            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(TestIdentities.Samwise);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, TestIdentities.Samwise, ConnectionStatus.Connected);

                var frodoConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var blockResponse = await frodoConnections.Block(TestIdentities.Samwise);

                Assert.IsTrue(blockResponse.IsSuccessStatusCode && blockResponse.Content, "failed to block");
                await AssertConnectionStatus(client, TestIdentities.Samwise, ConnectionStatus.Blocked);
            }

            await DisconnectSamAndFrodo();
        }

        [Test]
        [Ignore("Need to convert to transit protocol")]
        public async Task CanUnblock()
        {
            await CreateConnectionRequestSamToFrodo();

            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(TestIdentities.Samwise);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, TestIdentities.Samwise, ConnectionStatus.Connected);

                var frodoConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var blockResponse = await frodoConnections.Block(TestIdentities.Samwise);

                Assert.IsTrue(blockResponse.IsSuccessStatusCode && blockResponse.Content, "failed to block");
                await AssertConnectionStatus(client, TestIdentities.Samwise, ConnectionStatus.Blocked);

                var unblockResponse = await frodoConnections.Unblock(TestIdentities.Samwise);
                Assert.IsTrue(unblockResponse.IsSuccessStatusCode && unblockResponse.Content, "failed to unblock");
                await AssertConnectionStatus(client, TestIdentities.Samwise, ConnectionStatus.Connected);
            }

            await DisconnectSamAndFrodo();
        }


        [Test]
        [Ignore("Need to convert to transit protocol")]
        public async Task CanDisconnect()
        {
            await CreateConnectionRequestSamToFrodo();

            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(TestIdentities.Samwise);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, TestIdentities.Samwise, ConnectionStatus.Connected);

                var frodoConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var disconnectResponse = await frodoConnections.Disconnect(TestIdentities.Samwise);
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, TestIdentities.Samwise, ConnectionStatus.None);
            }

            await DisconnectSamAndFrodo();
        }

        private async Task AssertConnectionStatus(HttpClient client, string dotYouId, ConnectionStatus expected)
        {
            var svc = RestService.For<ICircleNetworkConnectionsClient>(client);
            var response = await svc.GetStatus(dotYouId);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {dotYouId}.  Status code was {response.StatusCode}");
            Assert.IsNotNull(response.Content, $"No status for {dotYouId} found");
            Assert.IsTrue(response.Content.Status == expected, $"{dotYouId} status does not match {expected}");
        }

        private async Task CreateConnectionRequestSamToFrodo()
        {
            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Samwise))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var id = Guid.NewGuid();
                var requestHeader = new ConnectionRequestHeader()
                {
                    Id = id,
                    Recipient = TestIdentities.Frodo,
                    Message = "Please add me"
                };

                var response = await svc.SendConnectionRequest(requestHeader);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                Assert.IsTrue(response.Content.Success, "Failed sending the request");
            }
        }

        private async Task DisconnectSamAndFrodo()
        {
            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var frodoConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var disconnectResponse = await frodoConnections.Delete(TestIdentities.Samwise);
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, TestIdentities.Samwise, ConnectionStatus.None);
            }

            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Samwise))
            {
                var samConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var disconnectResponse = await samConnections.Delete(TestIdentities.Frodo);
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, TestIdentities.Frodo, ConnectionStatus.None);
            }
        }
    }
}