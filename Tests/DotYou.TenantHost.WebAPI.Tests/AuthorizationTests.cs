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
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class AuthorizationTests
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
        public async Task CannotPerformUnauthorizedAction()
        {
            //have sam attempted to perform an action on frodos site
            using (var client = CreateHttpClient(samwise))
            {
                //point sams client to frodo
                client.BaseAddress = new Uri($"https://{frodo}");
                var svc = RestService.For<ITrustNetworkRequestsClient>(client);
                var response = await svc.GetPendingRequests(PageOptions.Default);

                Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden, "User was able to perform unauthorized action");

            }
        }

        [Test]
        public async Task CanSuccessfullyPerformAuthorized()
        {
            //have sam perform a normal operation on his site
            using (var client = CreateHttpClient(samwise))
            {
                var svc = RestService.For<ITrustNetworkRequestsClient>(client);
                var response = await svc.GetPendingRequests(PageOptions.Default);

                Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden, "User was able to perform unauthorized action");

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

    }
}