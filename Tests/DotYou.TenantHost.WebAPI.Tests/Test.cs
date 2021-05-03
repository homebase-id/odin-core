using DotYou.Kernel;
using DotYou.Kernel.Services.TrustNetwork;
using DotYou.Kernel.Storage;
using DotYou.Types;
using DotYou.Types.Certificate;
using DotYou.Types.TrustNetwork;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
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
        IdentityContextRegistry _registry = new IdentityContextRegistry();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _registry.Initialize();
        }

        [SetUp]
        public void Setup()
        {
            var args = new string[0];
            webserver = Program.CreateHostBuilder(args).Build();
            webserver.StartAsync();
        }

        [TearDown]
        public void Teardown()
        {
            webserver.StopAsync();
        }

        [Test(Description = "Test ensures a client certificate can be used to authenticate an identity")]
        [Ignore("Need to add authentication endpoint")]
        public async Task CanAuthenticateWithClientCertificate()
        {
            var ctx = _registry.ResolveContext(frodo);
            var cert = ctx.TenantCertificate.LoadCertificate();

            HttpClientHandler handler = new();
            handler.ClientCertificates.Add(cert);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;

            HttpClient client = new HttpClient(handler);

            var result = await client.GetStringAsync($"https://{frodo}/api/verify");

            Console.WriteLine($"Result: [{result}]");

            Assert.AreEqual("CN=samwisegamgee.me", result);
        }

        [Test(Description = "Send a Connection Request to be added to an individual's network")]
        [Order(1)]
        public async Task CanSendConnectionRequest()
        {
            var request = await CreateConnectionRequestSamToFrodo();

            var tn = CreateService(frodo);
            var storedRequest = await tn.GetPendingRequest(request.Id);
            Assert.IsNotNull(storedRequest, $"No request found with Id [{request.Id}]");
        }

        [Test(Description ="Create then delete a connection request")]
        [Order(2)]

        public async Task CanDeleteConnectionRequest()
        {
            var request = await CreateConnectionRequestSamToFrodo();
            var tn = CreateService(frodo);
            
            await tn.DeletePendingRequest(request.Id);
            var storedRequest = await tn.GetPendingRequest(request.Id);
            Assert.IsNull(storedRequest, $"Should not have found a request with Id [{request.Id}]");
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
                var result = await client.PostAsJsonAsync($"https://{frodo}/api/incoming/invitations/connect", request);
                Assert.IsTrue(result.IsSuccessStatusCode, "Failed sending the request");
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