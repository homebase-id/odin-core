using DotYou.Types;
using DotYou.Types.Circle;
using NUnit.Framework;
using Refit;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DotYou.TenantHost.WebAPI.Tests
{

    public class CircleNetworkServiceTests
    {
        private TestScaffold scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            scaffold = new TestScaffold(folder);
            scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            scaffold.RunAfterAnyTests();
        }
        
        [SetUp]
        public void Setup() { }

        [Test]
        public async Task CanSendConnectionRequestAndGetPendingRequest()
        {
            //Have sam send Frodo a request.
            var request = await CreateConnectionRequestSamToFrodo();
            var id = request.Id;

            //Check if Frodo received the request?
            using (var client = scaffold.CreateHttpClient(scaffold.Frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
                var response = await svc.GetPendingRequest(id);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsNotNull(response.Content, $"No request found with Id [{request.Id}]");
                Assert.IsTrue(response.Content.Id == request.Id);
            }
        }

        [Test]
        public async Task CanDeleteConnectionRequest()
        {
            var request = await CreateConnectionRequestSamToFrodo();

            using (var client = scaffold.CreateHttpClient(scaffold.Frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var deleteResponse = await svc.DeletePendingRequest(request.Id);
                Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getReponse = await svc.GetPendingRequest(request.Id);
                Assert.IsTrue(getReponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with Id {request.Id} still exists");
            }
        }

        [Test]
        public async Task CanGetPendingConnectionRequestList()
        {
            var request = await CreateConnectionRequestSamToFrodo();

            using (var client = scaffold.CreateHttpClient(scaffold.Frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var response = await svc.GetPendingRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Id == request.Id), $"Could not find request with id [{request.Id}] in the results");
            }
        }

        [Test]
        public async Task CanGetSentConnectionRequestList()
        {
            var request = await CreateConnectionRequestSamToFrodo();

            //Check Sam's list of sent requests
            using (var client = scaffold.CreateHttpClient(scaffold.Samwise))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var response = await svc.GetSentRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, "No result returned");
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Id == request.Id), $"Could not find request with id [{request.Id}] in the results");

            }
        }


        [Test]
        public async Task CanGetSentConnectionRequest()
        {
            var request = await CreateConnectionRequestSamToFrodo();

            //Check Sam's list of sent requests
            using (var client = scaffold.CreateHttpClient(scaffold.Samwise))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var response = await svc.GetSentRequest(request.Id);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, $"No request found with Id [{request.Id}]");
                Assert.IsTrue(response.Content.Id == request.Id);
            }
        }

        [Test]
        public async Task CanAcceptConnectionRequest()
        {

            var request = await CreateConnectionRequestSamToFrodo();

            using (var client = scaffold.CreateHttpClient(scaffold.Frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(request.Id);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                //
                // The pending request should be removed
                //
                var getReponse = await svc.GetPendingRequest(request.Id);
                Assert.IsTrue(getReponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with Id {request.Id} still exists");

                //
                // Sam should be in scaffold.Frodo's contacts network.
                //
                var frodoContactSvc = RestService.For<IContactRequestsClient>(client);
                var response = await frodoContactSvc.GetContactByDomain(scaffold.Samwise);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to contain at domain {scaffold.Samwise}.  Status code was {response.StatusCode}");
                Assert.IsNotNull(response.Content, $"No contact with domain {scaffold.Samwise} found");
                Assert.IsTrue(response.Content.GivenName == "Samwise");
                Assert.IsTrue(response.Content.Surname == "Gamgee");

            }

            using (var client = scaffold.CreateHttpClient(scaffold.Samwise))
            {
                //
                // Frodo should be in sam's contacts network
                //
                var contactSvc = RestService.For<IContactRequestsClient>(client);

                var response = await contactSvc.GetContactByDomain(scaffold.Frodo);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to retrieve {scaffold.Frodo}");
                Assert.IsNotNull(response.Content, $"No contact with domain {scaffold.Frodo} found");
                Assert.IsTrue(response.Content.GivenName == "Frodo");
                Assert.IsTrue(response.Content.Surname == "Baggins");
            }
        }


        private async Task<ConnectionRequest> CreateConnectionRequestSamToFrodo()
        {
            var samContext = scaffold.Registry.ResolveContext(scaffold.Samwise);
            var samCert = samContext.TenantCertificate.LoadCertificateWithPrivateKey();
            
            var request = new ConnectionRequest()
            {
                Id = Guid.NewGuid(),
                DateSent = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Message = "Please add me",
                Recipient = (DotYouIdentity)scaffold.Frodo,
                Sender = (DotYouIdentity)scaffold.Samwise,
                SenderGivenName = "Samwise",
                SenderSurname = "Gamgee"
            };
            
            using (var client = scaffold.CreateHttpClient(scaffold.Samwise))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
                var response = await svc.SendConnectionRequest(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                Assert.IsTrue(response.Content.Success, "Failed sending the request");
            }

            return request;
        }
    }
}