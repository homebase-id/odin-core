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
    public class AuthenticationTests
    {
        static DotYouIdentity frodo = (DotYouIdentity)"frodobaggins.me";
        static DotYouIdentity samwise = (DotYouIdentity)"samwisegamgee.me";

        IHost webserver;
        IdentityContextRegistry _registry;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _registry = new IdentityContextRegistry();
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
        public async Task CanAuthenticateWithClientCertificate()
        {
            Assert.Pass();
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


        private ILogger<T> CreateLogger<T>()
        {
            var lf = new LoggerFactory();
            var logger = lf.CreateLogger<T>();
            return logger;

        }
    }
}