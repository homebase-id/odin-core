using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Tests.OwnerApi.Circle;

namespace Youverse.Hosting.Tests.AppAPI.Circle
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
        public async Task CanGetPendingConnectionRequestList()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();
            
            //sam should have a pending incoming request
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(sam))
            {
                var svc = _scaffold.RestServiceFor<ICircleNetworkRequestsClient>(client, sam.SharedSecret);
                
                var response = await svc.GetPendingRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content);
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.SenderOdinId == frodo.Identity), $"Could not find request from {frodo.Identity} in the results");
            }

            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodo.Identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var deleteResponse = await svc.DeleteSentRequest(new OdinIdRequest() { OdinId = sam.Identity });
                Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sam.Identity });
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with from {sam.Identity} still exists");
            }

            await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
        }

        [Test]
        public async Task CanGetSentConnectionRequestList()
        {
            var (frodo, sam) = await CreateConnectionRequestFrodoToSam();
            
            //sam should have a pending incoming request

            //Check frodo's list of sent requests
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(frodo))
            {
                var svc = _scaffold.RestServiceFor<ICircleNetworkRequestsClient>(client, frodo.SharedSecret.ToSensitiveByteArray());
                var response = await svc.GetSentRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, "No result returned");
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Recipient == sam.Identity), $"Could not find request with recipient {sam.Identity} in the results");
            }
            
            await DeleteConnectionRequestsFromFrodoToSam(frodo, sam);
        }

        private async Task<(TestAppContext, TestAppContext)> CreateConnectionRequestFrodoToSam()
        {
            Guid appId = Guid.NewGuid();
            var sender = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, TestIdentities.Frodo, canReadConnections: true);
            var recipient = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canReadConnections: true);

            //have frodo send it
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(sender.Identity, out var sharedSecret))
            {
                var svc = _scaffold.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, sharedSecret);
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

            //check that sam got it
            using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(recipient.Identity, out var ownerSharedSecret))
            {

                var svc = _scaffold.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var response = await svc.GetPendingRequest(new OdinIdRequest() { OdinId = sender.Identity });

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
                Assert.IsTrue(response.Content.SenderOdinId == sender.Identity);
            }

            return (sender, recipient);
        }
        
        private async Task DeleteConnectionRequestsFromFrodoToSam(TestAppContext frodo, TestAppContext sam)
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
    }
}