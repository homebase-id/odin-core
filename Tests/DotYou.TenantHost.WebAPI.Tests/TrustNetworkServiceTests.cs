using DotYou.Types;
using DotYou.Types.TrustNetwork;
using NUnit.Framework;
using Refit;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace DotYou.TenantHost.WebAPI.Tests
{

    public class TrustNetworkServiceTests
    {
        static DotYouIdentity frodo = (DotYouIdentity)"frodobaggins.me";
        static DotYouIdentity samwise = (DotYouIdentity)"samwisegamgee.me";

        private IdentityContextRegistry _registry;

        public void SleepHack(int seconds)
        {
            //eww - until i figure out why async is not quite right
            System.Threading.Thread.Sleep(seconds * 1000);
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _registry = new IdentityContextRegistry();
            _registry.Initialize();
        }


        [SetUp]
        public void Setup() { }

        [Test]
        public async Task CanSendConnectionRequestAndGetPendingReqeust()
        {
            //Have sam send Frodo a request.
            var request = await CreateConnectionRequestSamToFrodo();
            var id = request.Id;

            //Check if Frodo received the request?
            using (var client = CreateHttpClient(frodo))
            {
                var svc = RestService.For<ITrustNetworkRequestsClient>(client);
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

            using (var client = CreateHttpClient(frodo))
            {
                var svc = RestService.For<ITrustNetworkRequestsClient>(client);

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

            using (var client = CreateHttpClient(frodo))
            {
                var svc = RestService.For<ITrustNetworkRequestsClient>(client);

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
            using (var client = CreateHttpClient(samwise))
            {
                var svc = RestService.For<ITrustNetworkRequestsClient>(client);

                var response = await svc.GetSentRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

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
            using (var client = CreateHttpClient(samwise))
            {
                var svc = RestService.For<ITrustNetworkRequestsClient>(client);

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

            using (var client = CreateHttpClient(frodo))
            {
                var svc = RestService.For<ITrustNetworkRequestsClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(request.Id);

                //
                // The pending request should be removed
                //
                var getReponse = await svc.GetPendingRequest(request.Id);
                Assert.IsTrue(getReponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with Id {request.Id} still exists");

                //
                // Sam should be in frodo's contacts network.
                //
                var frodoContactSvc = RestService.For<IContactRequestsClient>(client);
                var samResponse = await frodoContactSvc.GetContact(samwise);

                Assert.IsTrue(samResponse.IsSuccessStatusCode, $"Failed to retrieve {samwise}");
                Assert.IsNotNull(samResponse.Content, $"No contact with domain {samwise} found");

                //TODO: add checks that Surname and Givenname are correct

            }

            using (var client = CreateHttpClient(samwise))
            {
                //
                // Frodo should be in sam's contacts network
                //
                var contactSvc = RestService.For<IContactRequestsClient>(client);

                var response = await contactSvc.GetContact(frodo);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to retrieve {frodo}");
                Assert.IsNotNull(response.Content, $"No contact with domain {frodo} found");

                //TODO: add checks that Surname and Givenname are correct
            }
        }

        private HttpClient CreateHttpClient(DotYouIdentity identity)
        {
            var samContext = _registry.ResolveContext(identity);
            var samCert = samContext.TenantCertificate.LoadCertificate();

            HttpClientHandler handler = new();
            handler.ClientCertificates.Add(samCert);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;

            HttpClient client = new(handler);

            client.BaseAddress = new Uri($"https://{identity}");
            return client;
        }

        private async Task<ConnectionRequest> CreateConnectionRequestSamToFrodo()
        {
            var samContext = _registry.ResolveContext(samwise);
            var samCert = samContext.TenantCertificate.LoadCertificate();

            
            var request = new ConnectionRequest()
            {
                Id = Guid.NewGuid(),
                DateSent = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Message = "Please add me",
                Recipient = (DotYouIdentity)frodo,
                Sender = (DotYouIdentity)samwise,
                SenderPublicKey = samCert.GetPublicKeyString(),
                SenderGivenName = "Samwise",
                SenderSurname = "Gamgee"
            };

            using (var client = CreateHttpClient(samwise))
            {
                var svc = RestService.For<ITrustNetworkRequestsClient>(client);
                var response = await svc.SendConnectionRequest(request);
                Assert.IsTrue(response.IsSuccessStatusCode, "Failed sending the request");
                Assert.IsTrue(response.Content.Success, "Failed sending the request");
            }

            return request;
        }
    }
}