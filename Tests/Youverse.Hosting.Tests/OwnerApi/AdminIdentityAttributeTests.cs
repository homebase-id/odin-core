using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Profile;
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
        public async Task GetSaveAndGetPublicProfile()
        {
            DotYouIdentity user = scaffold.Frodo;
            using var client = scaffold.CreateHttpClient(user);
            var svc = RestService.For<IAdminIdentityAttributeClient>(client);

            var profile = new BasicProfileInfo()
            {
                Name = new NameAttribute()
                {
                    Personal = "Frodo",
                    Surname = "Baggins"
                },

                Photo = new ProfilePicAttribute()
                {
                    ProfilePic = "https://somephoto.com/photo"
                }
            };

            var saveResponse = await svc.SavePublicProfile(profile);

            Assert.IsTrue(saveResponse.IsSuccessStatusCode, "Failed to update public profile");
            Assert.IsNotNull(saveResponse.Content, "No content returned");
            Assert.IsTrue(saveResponse.Content.Success, "Save did not return a success");

            var getResponse = await svc.GetPublicProfile();

            Assert.IsTrue(getResponse.IsSuccessStatusCode, "Failed to retrieve updated public profile");
            Assert.IsNotNull(getResponse.Content, "No content returned");
            var storedProfile = getResponse.Content;

            Assert.IsTrue(storedProfile.Name.Personal == profile.Name.Personal);
            Assert.IsTrue(storedProfile.Name.Surname == profile.Name.Surname);
            Assert.IsTrue(storedProfile.Name.Additional == profile.Name.Additional);
            Assert.IsTrue(storedProfile.Name.Prefix == profile.Name.Prefix);
            Assert.IsTrue(storedProfile.Name.Suffix == profile.Name.Suffix);
            Assert.IsTrue(storedProfile.Name.AttributeType == profile.Name.AttributeType);
            Assert.IsTrue(storedProfile.Name.CategoryId == profile.Name.CategoryId);

            Assert.IsTrue(storedProfile.Photo.ProfilePic == profile.Photo.ProfilePic);
            Assert.IsTrue(storedProfile.Photo.AttributeType == profile.Photo.AttributeType);
            Assert.IsTrue(storedProfile.Photo.CategoryId == profile.Photo.CategoryId);
        }

        [Test]
        public async Task GetSaveAndGetConnectedProfile()
        {
            DotYouIdentity user = scaffold.Frodo;
            using var client = scaffold.CreateHttpClient(user);
            var svc = RestService.For<IAdminIdentityAttributeClient>(client);

            var profile = new BasicProfileInfo()
            {
                Name = new NameAttribute()
                {
                    Personal = "Frodo",
                    Surname = "Baggins"
                },

                Photo = new ProfilePicAttribute()
                {
                    ProfilePic = "https://somephoto.com/photo"
                }
            };

            var saveResponse = await svc.SaveConnectedProfile(profile);

            Assert.IsTrue(saveResponse.IsSuccessStatusCode, "Failed to update connected profile");
            Assert.IsNotNull(saveResponse.Content, "No content returned");
            Assert.IsTrue(saveResponse.Content.Success, "Save did not return a success");

            var getResponse = await svc.GetConnectedProfile();

            Assert.IsTrue(getResponse.IsSuccessStatusCode, "Failed to retrieve updated connected profile");
            Assert.IsNotNull(getResponse.Content, "No content returned");
            var storedProfile = getResponse.Content;

            Assert.IsTrue(storedProfile.Name.Personal == profile.Name.Personal);
            Assert.IsTrue(storedProfile.Name.Surname == profile.Name.Surname);
            Assert.IsTrue(storedProfile.Name.Additional == profile.Name.Additional);
            Assert.IsTrue(storedProfile.Name.Prefix == profile.Name.Prefix);
            Assert.IsTrue(storedProfile.Name.Suffix == profile.Name.Suffix);
            Assert.IsTrue(storedProfile.Name.AttributeType == profile.Name.AttributeType);
            Assert.IsTrue(storedProfile.Name.CategoryId == profile.Name.CategoryId);

            Assert.IsTrue(storedProfile.Photo.ProfilePic == profile.Photo.ProfilePic);
            Assert.IsTrue(storedProfile.Photo.AttributeType == profile.Photo.AttributeType);
            Assert.IsTrue(storedProfile.Photo.CategoryId == profile.Photo.CategoryId);
        }
    }
}