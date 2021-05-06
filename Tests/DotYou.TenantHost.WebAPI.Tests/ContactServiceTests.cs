using DotYou.Types;
using NUnit.Framework;
using Refit;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class ContactServiceTests
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
        public async Task CanAddAndGetContactByDomain()
        {
            //Add frodo to sams list as just a contact.
            using (var client = CreateHttpClient(samwise))
            {

                await AddFrodoToSamsContacts();

                var svc = RestService.For<IContactRequestsClient>(client);                
                var contactResponse = await svc.GetContactByDomain(frodo);

                Assert.IsNotNull(contactResponse.Content, $"Contact was not found by domain [{frodo}]");
                Assert.IsTrue(contactResponse.Content.GivenName == "Frodo");
                Assert.IsTrue(contactResponse.Content.Surname == "Baggins");
                Assert.IsTrue(contactResponse.Content.PrimaryEmail == "mail@frodobaggins.me");

            }
        }

        [Test]
        public async Task CanGetContactList()
        {
            //have sam perform a normal operation on his site
            using (var client = CreateHttpClient(samwise))
            {
                await AddFrodoToSamsContacts();

                var svc = RestService.For<IContactRequestsClient>(client);

                var response = await svc.GetContactsList(PageOptions.Default);

                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(c => c.DotYouId.ToString().ToLower() == frodo.ToString().ToLower()), $"Could not find contact with domain [{frodo}] in the results");

            }
        }

        private async Task AddFrodoToSamsContacts()
        {
            using (var client = CreateHttpClient(samwise))
            {
                var svc = RestService.For<IContactRequestsClient>(client);

                Contact contact = new()
                {
                    DotYouId = (DotYouIdentity)frodo,
                    GivenName = "Frodo",
                    Surname = "Baggins",
                    PrimaryEmail = "mail@frodobaggins.me",
                    PublicKeyCertificate = "",
                    SystemCircle = SystemCircle.PublicAnonymous,
                    Tag = "fellowship",
                };

                var response = await svc.SaveContact(contact);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Response failed with status code [{response.StatusCode}]");

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