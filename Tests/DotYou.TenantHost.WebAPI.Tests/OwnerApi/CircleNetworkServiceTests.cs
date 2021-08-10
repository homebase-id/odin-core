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
using DotYou.Types.ApiClient;

namespace DotYou.TenantHost.WebAPI.Tests.OwnerApi
{

    public class CircleNetworkServiceTests
    {
        private TestScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new TestScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }
        
        [SetUp]
        public void Setup() { }

        [Test]
        public async Task CanSendConnectionRequestAndGetPendingRequest()
        {
            //Have sam send Frodo a request.
            var requestId = await CreateConnectionRequestSamToFrodo();
            

            //Check if Frodo received the request?
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var svc = RestService.For<ICircleNetworkClient>(client);
                var response = await svc.GetPendingRequest(requestId);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsNotNull(response.Content, $"No request found with Id [{requestId}]");
                Assert.IsTrue(response.Content.Id == requestId);
            }
        }

        [Test]
        public async Task CanDeleteConnectionRequest()
        {
            var requestId = await CreateConnectionRequestSamToFrodo();

            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var svc = RestService.For<ICircleNetworkClient>(client);

                var deleteResponse = await svc.DeletePendingRequest(requestId);
                Assert.IsTrue(deleteResponse.IsSuccessStatusCode, deleteResponse.ReasonPhrase);

                var getResponse = await svc.GetPendingRequest(requestId);
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with Id {requestId} still exists");
            }
        }

        [Test]
        public async Task CanGetPendingConnectionRequestList()
        {
            var requestId = await CreateConnectionRequestSamToFrodo();

            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var svc = RestService.For<ICircleNetworkClient>(client);

                var response = await svc.GetPendingRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);

                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Id == requestId), $"Could not find request with id [{requestId}] in the results");
            }
        }

        [Test]
        public async Task CanGetSentConnectionRequestList()
        {
            var requestId = await CreateConnectionRequestSamToFrodo();

            //Check Sam's list of sent requests
            using (var client = _scaffold.CreateHttpClient(_scaffold.Samwise))
            {
                var svc = RestService.For<ICircleNetworkClient>(client);

                var response = await svc.GetSentRequestList(PageOptions.Default);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, "No result returned");
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(r => r.Id == requestId), $"Could not find request with id [{requestId}] in the results");

            }
        }


        [Test]
        public async Task CanGetSentConnectionRequest()
        {
            var requestId = await CreateConnectionRequestSamToFrodo();

            //Check Sam's list of sent requests
            using (var client = _scaffold.CreateHttpClient(_scaffold.Samwise))
            {
                var svc = RestService.For<ICircleNetworkClient>(client);

                var response = await svc.GetSentRequest(requestId);

                Assert.IsTrue(response.IsSuccessStatusCode, response.ReasonPhrase);
                Assert.IsNotNull(response.Content, $"No request found with Id [{requestId}]");
                Assert.IsTrue(response.Content.Id == requestId);
            }
        }

        [Test]
        public async Task CanAcceptConnectionRequest()
        {

            var requestId = await CreateConnectionRequestSamToFrodo();

            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var svc = RestService.For<ICircleNetworkClient>(client);

                var acceptResponse = await svc.AcceptConnectionRequest(requestId);

                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");

                //
                // The pending request should be removed
                //
                var getResponse = await svc.GetPendingRequest(requestId);
                Assert.IsTrue(getResponse.StatusCode == System.Net.HttpStatusCode.NotFound, $"Failed - request with Id {requestId} still exists");

                //
                // Sam should be in scaffold.Frodo's contacts network.
                //
                var frodoContactSvc = RestService.For<IContactManagementClient>(client);
                var response = await frodoContactSvc.GetContactByDomain(_scaffold.Samwise);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to contain at domain {_scaffold.Samwise}.  Status code was {response.StatusCode}");
                Assert.IsNotNull(response.Content, $"No contact with domain {_scaffold.Samwise} found");
                Assert.IsTrue(response.Content.Name.Personal == "Samwise");
                Assert.IsTrue(response.Content.Name.Surname == "Gamgee");

            }

            using (var client = _scaffold.CreateHttpClient(_scaffold.Samwise))
            {
                //
                // Frodo should be in sam's contacts network
                //
                var contactSvc = RestService.For<IContactManagementClient>(client);

                var response = await contactSvc.GetContactByDomain(_scaffold.Frodo);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to retrieve {_scaffold.Frodo}");
                Assert.IsNotNull(response.Content, $"No contact with domain {_scaffold.Frodo} found");
                Assert.IsTrue(response.Content.Name.Personal == "Frodo");
                Assert.IsTrue(response.Content.Name.Surname == "Baggins");
            }
        }


        private async Task<Guid> CreateConnectionRequestSamToFrodo()
        {
            using (var client = _scaffold.CreateHttpClient(_scaffold.Samwise))
            {
                var svc = RestService.For<ICircleNetworkClient>(client);

                var id = Guid.NewGuid();
                var requestHeader = new ConnectionRequestHeader()
                {
                    Id = id,
                    Recipient = _scaffold.Frodo,
                    Message = "Please add me"
                };
                
                var response = await svc.SendConnectionRequest(requestHeader);
                
                
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                Assert.IsTrue(response.Content.Success, "Failed sending the request");

                return id;
            }
        }
    }
}