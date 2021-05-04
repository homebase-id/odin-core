using DotYou.Kernel;
using DotYou.Kernel.Services.TrustNetwork;
using DotYou.Kernel.Storage;
using DotYou.Types;
using DotYou.Types.Certificate;
using DotYou.Types.TrustNetwork;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Refit;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
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
            //var request = await CreateConnectionRequestSamToFrodo();
            //var tn = CreateService(frodo);

            //await tn.DeletePendingRequest(request.Id);
            //var storedRequest = await tn.GetPendingRequest(request.Id);
            //Assert.IsNull(storedRequest, $"Should not have found a request with Id [{request.Id}]");
        }

        [Test]
        public async Task CanGetPendingConnectionRequestList()
        {
            //var request = await CreateConnectionRequestSamToFrodo();

            //var tn = CreateService(frodo);
            //var pagedResult = await tn.GetPendingRequests(PageOptions.Default);
            //Assert.IsNotNull(pagedResult);
            //Assert.IsTrue(pagedResult.TotalPages == 1);
            //Assert.IsTrue(pagedResult.Results.Count == 1);
            //Assert.IsTrue(pagedResult.Results[0].Id == request.Id);
        }

        [Test]
        public async Task CanGetSentConnectionRequestList()
        {

            //var request = await CreateConnectionRequestSamToFrodo();

            //var tn = CreateService(samwise);

            //var pagedResult = await tn.GetSentRequests(PageOptions.Default);

            //Assert.IsNotNull(pagedResult);
            //Assert.IsTrue(pagedResult.TotalPages == 1);
            //Assert.IsTrue(pagedResult.Results.Count == 1);
            //Assert.IsTrue(pagedResult.Results[0].Id == request.Id);
        }

        [Test]
        public async Task CanAcceptConnectionRequest()
        {
            Assert.Fail("awaiting implementation");
        }

        private ITrustNetworkService CreateService(DotYouIdentity identity)
        {
            var ctx = _registry.ResolveContext(identity);
            var tn = new TrustNetworkService(ctx, CreateLogger<TrustNetworkService>());
            return tn;
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

        private ILogger<T> CreateLogger<T>()
        {
            var lf = new LoggerFactory();
            var logger = lf.CreateLogger<T>();
            return logger;

        }
    }
}