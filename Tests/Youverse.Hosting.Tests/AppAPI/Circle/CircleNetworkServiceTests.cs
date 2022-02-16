using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle;

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
            var recipient = await _scaffold.SetupTestSampleApp(appId, TestIdentities.Samwise, canManageConnections: true);

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
        public async Task CanDeleteConnectionRequest()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.CreateAppApiHttpClient(frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var deleteResponse = await svc.DeletePendingRequest(sam.Identity);
                Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(sam.Identity);
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
            }

            //await DisconnectSamAndFrodo();
        }

        [Test]
        public async Task CanGetPendingConnectionRequestList()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.CreateAppApiHttpClient(sam))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var response = await svc.GetPendingRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content);
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.SenderDotYouId == frodo.Identity), $"Could not find request from {frodo.Identity} in the results");
            }

            //await DisconnectSamAndFrodo();
        }

        [Test]
        public async Task CanGetSentConnectionRequestList()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();
            
            //Check Sam's list of sent requests
            using (var client = _scaffold.CreateAppApiHttpClient(frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var response = await svc.GetSentRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, "No result returned");
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Recipient == sam.Identity), $"Could not find request with recipient {sam.Identity} in the results");
            }

            //await DisconnectSamAndFrodo();
        }


        [Test]
        public async Task CanGetSentConnectionRequest()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.CreateAppApiHttpClient(frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var response = await svc.GetSentRequest(sam.Identity);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, $"No request found with recipient [{sam.Identity}]");
                Assert.IsTrue(response.Content.Recipient == sam.Identity);
            }
            
        }

        [Test]
        public async Task CanAcceptConnectionRequest()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.CreateOwnerApiHttpClient(sam.Identity))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(frodo.Identity);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                //
                // The pending request should be removed
                //
                var getResponse = await svc.GetPendingRequest(frodo.Identity);
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with sender {frodo.Identity} still exists");

                //
                // Frodo should be in Sams's contacts network.
                //
                var samsConnetions = RestService.For<ICircleNetworkConnectionsClient>(client);
                var response = await samsConnetions.GetStatus(frodo.Identity);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {frodo.Identity}.  Status code was {response.StatusCode}");
                Assert.IsNotNull(response.Content, $"No status for {frodo.Identity} found");
                Assert.IsTrue(response.Content.Status == ConnectionStatus.Connected);
            }

            using (var client = _scaffold.CreateOwnerApiHttpClient(frodo.Identity))
            {
                //
                // Frodo should be in sam's contacts network
                //
                var frodoConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var response = await frodoConnections.GetStatus(sam.Identity);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {sam.Identity}.  Status code was {response.StatusCode}");
                Assert.IsNotNull(response.Content, $"No status for {sam.Identity} found");
                Assert.IsTrue(response.Content.Status == ConnectionStatus.Connected);
            }

            // await DisconnectIdentities(frodo, sam);
        }


        [Test]
        public async Task CanBlock()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.CreateAppApiHttpClient(sam))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(frodo.Identity);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, frodo.Identity, ConnectionStatus.Connected);

                var samConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var blockResponse = await samConnections.Block(frodo.Identity);

                Assert.IsTrue(blockResponse.IsSuccessStatusCode && blockResponse.Content, "failed to block");
                await AssertConnectionStatus(client, frodo.Identity, ConnectionStatus.Blocked);
            }

            await DisconnectIdentities(frodo, sam);
        }

        [Test]
        public async Task CanUnblock()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.CreateAppApiHttpClient(sam))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(frodo.Identity);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, frodo.Identity, ConnectionStatus.Connected);

                var samConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var blockResponse = await samConnections.Block(frodo.Identity);

                Assert.IsTrue(blockResponse.IsSuccessStatusCode && blockResponse.Content, "failed to block");
                await AssertConnectionStatus(client, frodo.Identity, ConnectionStatus.Blocked);

                var unblockResponse = await samConnections.Unblock(frodo.Identity);
                Assert.IsTrue(unblockResponse.IsSuccessStatusCode && unblockResponse.Content, "failed to unblock");
                await AssertConnectionStatus(client, frodo.Identity, ConnectionStatus.Connected);
            }

            await DisconnectIdentities(frodo, sam);
        }


        [Test]
        public async Task CanDisconnect()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            using (var client = _scaffold.CreateAppApiHttpClient(sam))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(frodo.Identity);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                await AssertConnectionStatus(client, frodo.Identity, ConnectionStatus.Connected);

                var samConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var disconnectResponse = await samConnections.Disconnect(frodo.Identity);
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, frodo.Identity, ConnectionStatus.None);
            }
            
        }

        private async Task AssertConnectionStatus(HttpClient client, string dotYouId, ConnectionStatus expected)
        {
            var svc = RestService.For<ICircleNetworkConnectionsClient>(client);
            var response = await svc.GetStatus(dotYouId);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {dotYouId}.  Status code was {response.StatusCode}");
            Assert.IsNotNull(response.Content, $"No status for {dotYouId} found");
            Assert.IsTrue(response.Content.Status == expected, $"{dotYouId} status does not match {expected}");
        }

        private async Task<(TestSampleAppContext, TestSampleAppContext)> CreateConnectionRequestFrodoToSam()
        {
            Guid appId = Guid.NewGuid();
            var sender = await _scaffold.SetupTestSampleApp(appId, TestIdentities.Frodo, canManageConnections: true);
            var recipient = await _scaffold.SetupTestSampleApp(appId, TestIdentities.Samwise, canManageConnections: true);

            //have frodo send it
            using (var client = _scaffold.CreateOwnerApiHttpClient(sender.Identity))
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

            //check that sam got it
            using (var client = _scaffold.CreateOwnerApiHttpClient(recipient.Identity))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
                var response = await svc.GetPendingRequest(sender.Identity);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
                Assert.IsTrue(response.Content.SenderDotYouId == sender.Identity);
            }

            return (sender, recipient);
        }

        private async Task DisconnectIdentities(TestSampleAppContext frodo, TestSampleAppContext sam)
        {
            using (var client = _scaffold.CreateAppApiHttpClient(frodo))
            {
                var frodoConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var disconnectResponse = await frodoConnections.Delete(sam.Identity);
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, TestIdentities.Samwise, ConnectionStatus.None);
            }

            using (var client = _scaffold.CreateAppApiHttpClient(sam))
            {
                var samConnections = RestService.For<ICircleNetworkConnectionsClient>(client);
                var disconnectResponse = await samConnections.Delete(frodo.Identity);
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, TestIdentities.Frodo, ConnectionStatus.None);
            }
        }
    }
}