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
        private TestScaffold scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            scaffold = new TestScaffold(folder);
            scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task CanAddAndGetContactByDomain()
        {
            await AddSamToFrodosContacts();
            
            using (var client = scaffold.CreateHttpClient(scaffold.Frodo))
            {
                var svc = RestService.For<IContactRequestsClient>(client);                
                var contactResponse = await svc.GetContactByDomain(scaffold.Samwise);

                Assert.IsNotNull(contactResponse.Content, $"Contact was not found by domain [{scaffold.Samwise}]");
                Assert.IsTrue(contactResponse.Content.GivenName == "Samwise");
                Assert.IsTrue(contactResponse.Content.Surname == "Gamgee");
                Assert.IsTrue(contactResponse.Content.PrimaryEmail == "mail@samwisegamgee.me");

            }
        }

        [Test]
        public async Task CanGetContactList()
        {
            using (var client = scaffold.CreateHttpClient(scaffold.Samwise))
            {
                await AddFrodoToSamsContacts();

                var svc = RestService.For<IContactRequestsClient>(client);

                var response = await svc.GetContactsList(PageOptions.Default);

                await scaffold.OutputRequestInfo(response);
                
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(c => c.DotYouId.ToString().ToLower() == scaffold.Frodo.ToString().ToLower()), $"Could not find contact with domain [{scaffold.Frodo}] in the results");

            }
        }

        private async Task AddSamToFrodosContacts()
        {
            using (var client = scaffold.CreateHttpClient(scaffold.Frodo))
            {
                var svc = RestService.For<IContactRequestsClient>(client);
                
                Contact contact = new()
                {
                    DotYouId = scaffold.Samwise,
                    GivenName = "Samwise",
                    Surname = "Gamgee",
                    PrimaryEmail = "mail@samwisegamgee.me",
                    PublicKeyCertificate = "",
                    SystemCircle = SystemCircle.PublicAnonymous,
                    Tag = "fellowship",
                };

                var response = await svc.SaveContact(contact);
                await scaffold.OutputRequestInfo(response);
                
                Assert.IsTrue(response.IsSuccessStatusCode, $"Response failed with status code [{response.StatusCode}]");

            }
        }
        
        private async Task AddFrodoToSamsContacts()
        {
            using (var client = scaffold.CreateHttpClient(scaffold.Samwise))
            {
                var svc = RestService.For<IContactRequestsClient>(client);
                
                Contact contact = new()
                {
                    DotYouId = scaffold.Frodo,
                    GivenName = "Frodo",
                    Surname = "Baggins",
                    PrimaryEmail = "mail@frodobaggins.me",
                    PublicKeyCertificate = "",
                    SystemCircle = SystemCircle.PublicAnonymous,
                    Tag = "fellowship",
                };

                var response = await svc.SaveContact(contact);
                await scaffold.OutputRequestInfo(response);
                
                Assert.IsTrue(response.IsSuccessStatusCode, $"Response failed with status code [{response.StatusCode}]");

            }
        }

    }
}