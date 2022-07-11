using System;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Requests;
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

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sam.Identity))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

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
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(frodo.Identity))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var response = await svc.GetSentRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, "No result returned");
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Recipient == sam.Identity), $"Could not find request with recipient {sam.Identity} in the results");
            }
        }

        private async Task<(TestSampleAppContext, TestSampleAppContext)> CreateConnectionRequestFrodoToSam()
        {
            Guid appId = Guid.NewGuid();
            var sender = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Frodo, canManageConnections: true);
            var recipient = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canManageConnections: true);

            //have frodo send it
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sender.Identity))
            {
                var svc = RestService.For<ICircleNetworkRequestsOwnerClient>(client);

                var id = Guid.NewGuid();
                var requestHeader = new ConnectionRequestHeader()
                {
                    Id = id,
                    Recipient = recipient.Identity,
                    Message = "Please add me"
                };

                var response = await svc.SendConnectionRequest(requestHeader);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                Assert.IsTrue(response.Content!.Success, "Failed sending the request");
            }

            //check that sam got it
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(recipient.Identity))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
                var response = await svc.GetPendingRequest(sender.Identity);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
                Assert.IsTrue(response.Content.SenderDotYouId == sender.Identity);
            }

            return (sender, recipient);
        }
    }
}