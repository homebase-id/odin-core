using System;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
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
            var (frodoTestContext, samTestContext) = await CreateConnectionRequestFrodoToSam();
            
            //sam should have a pending incoming request
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(samTestContext))
            {
                var svc = _scaffold.RestServiceFor<ICircleNetworkRequestsClient>(client, samTestContext.SharedSecret);
                
                var response = await svc.GetPendingRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content);
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.SenderDotYouId == frodoTestContext.Identity), $"Could not find request from {frodoTestContext.Identity} in the results");
            }
        }

        [Test]
        public async Task CanGetSentConnectionRequestList()
        {
            var (frodoTestContext, samTestContext) = await CreateConnectionRequestFrodoToSam();
            
            //sam should have a pending incoming request

            //Check frodo's list of sent requests
            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(frodoTestContext))
            {
                var svc = _scaffold.RestServiceFor<ICircleNetworkRequestsClient>(client, frodoTestContext.SharedSecret.ToSensitiveByteArray());
                var response = await svc.GetSentRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, "No result returned");
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Recipient == samTestContext.Identity), $"Could not find request with recipient {samTestContext.Identity} in the results");
            }
        }

        private async Task<(TestSampleAppContext, TestSampleAppContext)> CreateConnectionRequestFrodoToSam()
        {
            Guid appId = Guid.NewGuid();
            var sender = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Frodo, canManageConnections: true);
            var recipient = await _scaffold.OwnerApi.SetupTestSampleApp(appId, TestIdentities.Samwise, canManageConnections: true);

            //have frodo send it
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(sender.Identity, out var sharedSecret))
            {
                var svc = _scaffold.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, sharedSecret);
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

            //check that sam got it
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(recipient.Identity, out var ownerSharedSecret))
            {

                var svc = _scaffold.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var response = await svc.GetPendingRequest(new DotYouIdRequest() { DotYouId = sender.Identity });

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsNotNull(response.Content, $"No request found from {sender.Identity}");
                Assert.IsTrue(response.Content.SenderDotYouId == sender.Identity);
            }

            return (sender, recipient);
        }
    }
}