using DotYou.Types;
using NUnit.Framework;
using Refit;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class AuthorizationTests
    {
        static DotYouIdentity frodo = (DotYouIdentity)"frodobaggins.me";
        static DotYouIdentity samwise = (DotYouIdentity)"samwisegamgee.me";

        //IHost webserver;
        IdentityContextRegistry _registry;

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

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
        }

        [SetUp]
        public void Setup() { }

        [Test]
        public void GetCertInfo()
        {
            var samContext = _registry.ResolveContext(samwise);
            var samCert = samContext.TenantCertificate.LoadPublicKeyCertificate();

            Console.WriteLine($"yooo: {samCert}");

            string path = samContext.TenantCertificate.Location.CertificatePath;
            //path = "/Users/taud/src/DotYouCore/ossl.crt";
            //using (X509Certificate2 publicKey = new X509Certificate2(path))
            //{
            //    Console.WriteLine($"simple name: {publicKey.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false)}");
            //}
        }

        [Test]
        public async Task CannotPerformUnauthorizedAction()
        {
            //have sam attempted to perform an action on frodos site
            using (var client = CreateHttpClient(samwise))
            {
                //point sams client to frodo
                client.BaseAddress = new Uri($"https://{frodo}");
                var svc = RestService.For<ITrustNetworkRequestsClient>(client);
                var response = await svc.GetPendingRequestList(PageOptions.Default);

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
                var response = await svc.GetPendingRequestList(PageOptions.Default);

                Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden, "User was able to perform unauthorized action");

            }
        }


        private HttpClient CreateHttpClient(DotYouIdentity identity)
        {
            var samContext = _registry.ResolveContext(identity);
            var samCert = samContext.TenantCertificate.LoadCertificateWithPrivateKey();

            HttpClientHandler handler = new();
            handler.ClientCertificates.Add(samCert);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;

            HttpClient client = new(handler);

            client.BaseAddress = new Uri($"https://{identity}");
            return client;
        }

    }
}