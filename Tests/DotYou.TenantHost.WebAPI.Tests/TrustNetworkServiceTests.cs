using DotYou.Types;
using DotYou.Types.TrustNetwork;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Refit;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class TrustNetworkServiceTests
    {
        static DotYouIdentity frodo = (DotYouIdentity)"frodobaggins.me";
        static DotYouIdentity samwise = (DotYouIdentity)"samwisegamgee.me";

        IHost webserver;
        IdentityContextRegistry _registry;

        public void SleepHack(int seconds)
        {
            //eww - until i figure out why async is not quite right
            System.Threading.Thread.Sleep(seconds * 1000);
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            var args = new string[0];
            webserver = Program.CreateHostBuilder(args).Build();
            webserver.Start();

            _registry = new IdentityContextRegistry();
            _registry.Initialize();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            //HACK: make the server wait a few seconds so it can complete all operations
            System.Threading.Thread.Sleep(2 * 1000);
            webserver.StopAsync().Wait();
        }

        [SetUp]
        public void Setup() { }

        [Test]
        public async Task CanSendConnectionRequest()
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

                Assert.IsNotNull(response.Content, $"No request found with Id [{id}]");
                Assert.IsTrue(response.Content.Id == id);
            }
        }

        [Test]
        public async Task CanDeleteConnectionRequest()
        {
            var request = await CreateConnectionRequestSamToFrodo();

            using (var client = CreateHttpClient(frodo))
            {
                var svc = RestService.For<ITrustNetworkRequestsClient>(client);

                var response = await svc.DeletePendingRequest(request.Id);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

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

                Assert.IsTrue(response.Content.TotalPages == 1);
                Assert.IsTrue(response.Content.Results.Count == 1);
                Assert.IsTrue(response.Content.Results[0].Id == request.Id);
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

                Assert.IsTrue(response.Content.TotalPages == 1);
                Assert.IsTrue(response.Content.Results.Count == 1);
                Assert.IsTrue(response.Content.Results[0].Id == request.Id);
            }
        }

        [Test]
        public async Task CanAcceptConnectionRequest()
        {
            Assert.Fail("awaiting implementation");
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
            var request = new ConnectionRequest()
            {
                Id = Guid.NewGuid(),
                DateSent = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Message = "Please add me",
                Recipient = (DotYouIdentity)frodo,
                Sender = (DotYouIdentity)samwise,
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