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
using DotYou.Types.ApiClient;
using DotYou.Types.DataAttribute;

namespace DotYou.TenantHost.WebAPI.Tests.OwnerApi
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
                var svc = RestService.For<IContactManagementClient>(client);                
                var contactResponse = await svc.GetContactByDomain(scaffold.Samwise);

                Assert.IsNotNull(contactResponse.Content, $"Contact was not found by domain [{scaffold.Samwise}]");
                Assert.IsTrue(contactResponse.Content.Name.Personal == "Samwise");
                Assert.IsTrue(contactResponse.Content.Name.Surname == "Gamgee");

            }
        }

        [Test]
        public async Task CanGetContactList()
        {
            using (var client = scaffold.CreateHttpClient(scaffold.Samwise))
            {
                await AddFrodoToSamsContacts();

                var svc = RestService.For<IContactManagementClient>(client);

                var response = await svc.GetContactsList(PageOptions.Default, false);

                await scaffold.OutputRequestInfo(response);
                
                Assert.IsTrue(response.Content.TotalPages >= 1);
                Assert.IsTrue(response.Content.Results.Count >= 1);
                Assert.IsNotNull(response.Content.Results.SingleOrDefault(c => c.Id.ToString().ToLower() == scaffold.Frodo.ToString().ToLower()), $"Could not find contact with domain [{scaffold.Frodo}] in the results");

            }
        }

        private async Task AddSamToFrodosContacts()
        {
            using (var client = scaffold.CreateHttpClient(scaffold.Frodo))
            {
                var svc = RestService.For<IContactManagementClient>(client);

                var name = new NameAttribute()
                {
                    Personal = "Samwise",
                    Surname = "Gamgee"
                };
                HumanProfile humanProfile = new()
                {
                    Id = scaffold.Samwise,
                    Name = name,
                    PublicKeyCertificate = "",
                };

                var response = await svc.SaveContact(humanProfile);
                await scaffold.OutputRequestInfo(response);
                
                Assert.IsTrue(response.IsSuccessStatusCode, $"Response failed with status code [{response.StatusCode}]");

            }
        }
        
        private async Task AddFrodoToSamsContacts()
        {
            using (var client = scaffold.CreateHttpClient(scaffold.Samwise))
            {
                var svc = RestService.For<IContactManagementClient>(client);
                
                var name = new NameAttribute()
                {
                    Personal = "Frodo",
                    Surname = "Baggins"
                };
                
                HumanProfile humanProfile = new()
                {
                    Id = scaffold.Frodo,
                    PublicKeyCertificate = "",
                };

                var response = await svc.SaveContact(humanProfile);
                await scaffold.OutputRequestInfo(response);
                
                Assert.IsTrue(response.IsSuccessStatusCode, $"Response failed with status code [{response.StatusCode}]");

            }
        }

    }
}