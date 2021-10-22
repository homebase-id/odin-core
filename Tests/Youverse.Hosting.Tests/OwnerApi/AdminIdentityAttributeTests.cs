using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Hosting.Tests.ApiClient;

namespace Youverse.Hosting.Tests.OwnerApi
{
    public class AdminIdentityAttributeTests
    {
        private TestScaffold scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;

            Console.WriteLine($"one time setup {folder}");
            scaffold = new TestScaffold(folder);
            scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task CanSaveAndGetPrimaryNameAttribute()
        {
            Console.WriteLine($"starting");
            DotYouIdentity user = scaffold.Frodo;
            using var client = scaffold.CreateHttpClient(user);
            var svc = RestService.For<IAdminIdentityAttributeClient>(client);

            var name = new NameAttribute()
            {
                Personal = "Frodo",
                Surname = "Baggins"
            };

            var saveResponse = await svc.SavePrimaryName(name);

            Assert.IsTrue(saveResponse.IsSuccessStatusCode, "Failed to update name");
            Assert.IsNotNull(saveResponse.Content, "No content returned");
            Assert.IsTrue(saveResponse.Content.Success, "Save did not return a success");

            var getResponse = await svc.GetPrimaryName();

            Assert.IsTrue(getResponse.IsSuccessStatusCode, "Failed to retrieve updated name");
            Assert.IsNotNull(getResponse.Content, "No content returned");
            var storedName = getResponse.Content;

            Assert.IsTrue(storedName.Personal == "Frodo");
            Assert.IsTrue(storedName.Surname == "Baggins");
        }
    }
}