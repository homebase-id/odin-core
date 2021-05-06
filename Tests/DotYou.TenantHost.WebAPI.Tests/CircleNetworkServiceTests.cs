using DotYou.Types;
using DotYou.Types.Circle;
using NUnit.Framework;
using Refit;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace DotYou.TenantHost.WebAPI.Tests
{

    public class CircleNetworkServiceTests
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
        public async Task CanSendConnectionRequestAndGetPendingReqeust()
        {
            //Have sam send Frodo a request.
            var request = await CreateConnectionRequestSamToFrodo();
            var id = request.Id;

            //Check if Frodo received the request?
            using (var client = CreateHttpClient(frodo))
            {
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
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
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

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
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

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
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var response = await svc.GetSentRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, "No result returned");
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
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

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
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(request.Id);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                //
                // The pending request should be removed
                //
                var getReponse = await svc.GetPendingRequest(request.Id);
                Assert.IsTrue(getReponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with Id {request.Id} still exists");

                //
                // Sam should be in frodo's contacts network.
                //
                var frodoContactSvc = RestService.For<IContactRequestsClient>(client);
                var samResponse = await frodoContactSvc.GetContactByDomain(samwise);

                Assert.IsTrue(samResponse.IsSuccessStatusCode, $"Failed to retrieve {samwise}.  Status code was {samResponse.StatusCode}");
                Assert.IsNotNull(samResponse.Content, $"No contact with domain {samwise} found");

                //TODO: add checks that Surname and Givenname are correct

            }

            using (var client = CreateHttpClient(samwise))
            {
                //
                // Frodo should be in sam's contacts network
                //
                var contactSvc = RestService.For<IContactRequestsClient>(client);

                var response = await contactSvc.GetContactByDomain(frodo);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to retrieve {frodo}");
                Assert.IsNotNull(response.Content, $"No contact with domain {frodo} found");

                //TODO: add checks that Surname and Givenname are correct
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

        private async Task<ConnectionRequest> CreateConnectionRequestSamToFrodo()
        {
            var samContext = _registry.ResolveContext(samwise);
            var samCert = samContext.TenantCertificate.LoadCertificateWithPrivateKey();
            
            var rsa = (RSA)samCert.PublicKey.Key;
            byte[] certBytes = rsa.ExportSubjectPublicKeyInfo();
            //byte[] certBytes = x.ExportRSAPublicKey();
            string certPublicKey = Convert.ToBase64String(certBytes);
            
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
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
                var response = await svc.SendConnectionRequest(request);
                Assert.IsTrue(response.IsSuccessStatusCode, "Failed sending the request");
                Assert.IsTrue(response.Content.Success, "Failed sending the request");
            }

            return request;
        }
    }
}