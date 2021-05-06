using DotYou.Types;
using NUnit.Framework;
using Refit;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class AuthorizationTests
    {
        private IHost webserver;

        static DotYouIdentity frodo = (DotYouIdentity)"frodobaggins.me";
        static DotYouIdentity samwise = (DotYouIdentity)"samwisegamgee.me";

        //IHost webserver;
        IdentityContextRegistry _registry;
        
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            string testDataPath = Path.Combine(Path.DirectorySeparatorChar.ToString(), @"tmp","dotyoudata", folder);
            string logFilePath = Path.Combine(Path.DirectorySeparatorChar.ToString(), @"tmp","dotyoulogs", folder);

            if (Directory.Exists(testDataPath))
            {
                Console.WriteLine($"Removing data in [{testDataPath}]");
                Directory.Delete(testDataPath, true);
            }
            
            if (Directory.Exists(logFilePath))
            {
                Console.WriteLine($"Removing data in [{logFilePath}]");
                Directory.Delete(logFilePath, true);
            }

            Directory.CreateDirectory(testDataPath);
            Directory.CreateDirectory(logFilePath);

            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            var args = new string[2];
            args[0] = testDataPath;
            args[1] = logFilePath;
            webserver = Program.CreateHostBuilder(args).Build();
            webserver.Start();
            
            _registry = new IdentityContextRegistry(testDataPath);
            _registry.Initialize();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            System.Threading.Thread.Sleep(2000);
            webserver.StopAsync();
            webserver.Dispose();
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
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
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
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
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