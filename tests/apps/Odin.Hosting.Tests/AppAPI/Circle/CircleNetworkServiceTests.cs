using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Odin.Hosting.Tests.AppAPI.Circle
{
    public class CircleNetworkServiceTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise });
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
        public async Task CanGetPendingConnectionRequestList()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            //sam should have a pending incoming request
            var client = _scaffold.AppApi.CreateAppApiHttpClient(sam);
            {
                var svc = _scaffold.RestServiceFor<ICircleNetworkRequestsClient>(client, sam.SharedSecret);

                var response = await svc.GetPendingRequestList(PageOptions.Default);

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                ClassicAssert.IsNotNull(response.Content);
                ClassicAssert.IsTrue(response.Content.TotalPages >= 1);
                ClassicAssert.IsTrue(response.Content.Results.Count >= 1);
                ClassicAssert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.SenderOdinId == frodo.Identity),
                    $"Could not find request from {frodo.Identity} in the results");

                ClassicAssert.IsTrue(response.Content.Results.All(r => r.Payload == null), "Payload should not be sent to the client");
            }

            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
                ClassicAssert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
                ClassicAssert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
            }

            await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
        }

        [Test]
        public async Task CanGetSentConnectionRequestList()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();

            //sam should have a pending incoming request

            //Check frodo's list of sent requests
            var client = _scaffold.AppApi.CreateAppApiHttpClient(frodo);
            {
                var svc = _scaffold.RestServiceFor<ICircleNetworkRequestsClient>(client, frodo.SharedSecret.ToSensitiveByteArray());
                var response = await svc.GetSentRequestList(PageOptions.Default);

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                ClassicAssert.IsNotNull(response.Content, "No result returned");
                ClassicAssert.IsTrue(response.Content.TotalPages >= 1);
                ClassicAssert.IsTrue(response.Content.Results.Count >= 1);
                ClassicAssert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Recipient == sam.Identity),
                    $"Could not find request with recipient {sam.Identity} in the results");
            }

            await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
        }

        private async Task<(TestAppContext, TestAppContext)> CreateConnectionRequestFrodoToSam()
        {
            Guid appId = Guid.NewGuid();
            var sender = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, TestIdentities.Frodo, canReadConnections: true);
            var recipient = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canReadConnections: true);

            //have frodo send it
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender.Identity, out var sharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, sharedSecret);
                var id = Guid.NewGuid();
                var requestHeader = new ConnectionRequestHeader()
                {
                    Id = id,
                    Recipient = recipient.Identity,
                    Message = "Please add me",
                    ContactData = sender.ContactData
                };

                var response = await svc.SendConnectionRequest(requestHeader);

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                ClassicAssert.IsTrue(response.Content, "Failed sending the request");
            }

            //check that sam got it
            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(recipient.Identity, out var ownerSharedSecret);
            {
                var svc = _scaffold.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender.Identity });

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                ClassicAssert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
                ClassicAssert.IsTrue(response.Content.SenderOdinId == sender.Identity);
            }

            return (sender, recipient);
        }

        private async Task DeleteConnectionRequestsFromFrodoToSam(TestAppContext frodo, TestAppContext sam)
        {
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sam.Identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var deleteResponse = await svc.DeletePendingRequest(new OdinIdRequest() { OdinId = frodo.Identity });
                ClassicAssert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
                ClassicAssert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
            }

            client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

                var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
                ClassicAssert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
                ClassicAssert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
            }
        }
    }
}