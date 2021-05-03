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
    public class Tests
    {
        static string frodo = "frodobaggins.me";
        static string samwise = "samwisegamgee.me";

        IHost webserver;

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
        [Ignore("need to find a permentant certificate for testing")]
        public async Task CanAuthenticateWithClientCertificate()
        {
            string publicKeyFile = Path.Combine(Environment.CurrentDirectory, "https", samwise, "certificate.crt");
            string privateKeyFile = Path.Combine(Environment.CurrentDirectory, "https", samwise, "private.key");

            var cert = CertificateLoader.LoadPublicPrivateRSAKey(publicKeyFile, privateKeyFile);

            HttpClientHandler handler = new();
            handler.ClientCertificates.Add(cert);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;

            HttpClient client = new HttpClient(handler);

            var result = await client.GetStringAsync($"https://{frodo}/api/verify");

            Console.WriteLine($"Result: [{result}]");

            Assert.AreEqual("CN=samwisegamgee.me", result);
        }

        [Test(Description = "Test scenario of sending a connection request to be added to an individual's network")]
        public async Task CanSendConnectionRequest()
        {
            string publicKeyFile = Path.Combine(Environment.CurrentDirectory, "https", samwise, "certificate.crt");
            string privateKeyFile = Path.Combine(Environment.CurrentDirectory, "https", samwise, "private.key");

            var cert = CertificateLoader.LoadPublicPrivateRSAKey(publicKeyFile, privateKeyFile);

            HttpClientHandler handler = new();
            handler.ClientCertificates.Add(cert);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;

            HttpClient client = new(handler);

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

            var result = await client.PostAsJsonAsync($"https://{frodo}/api/incoming/invitations/connect", request);

            Assert.IsTrue(result.IsSuccessStatusCode, "Failed sending the request");

            IdentityContextRegistry reg = new IdentityContextRegistry();
            reg.Initialize();

            var ctx = reg.ResolveContext(frodo);
            var lf = new LoggerFactory();
            var logger  = lf.CreateLogger<TrustNetworkService>();
            var tn = new TrustNetworkService(ctx, logger);

            var storedRequest = await tn.GetPendingRequest(request.Id);

            Assert.IsNotNull(storedRequest, $"No request found with Id [{request.Id}]");
            Assert.AreEqual(request.Id, storedRequest.Id);

        }
    }
}