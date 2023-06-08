using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Configuration;

namespace Odin.Hosting.Tests.OwnerApi.Configuration
{
    public class OwnerAppSettingsTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: true);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task CanGetAndReadOwnerAppSetting()
        {
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);

                var getOwnerAppSettingsEmpty = await svc.GetOwnerAppSettings();
                Assert.IsTrue(getOwnerAppSettingsEmpty.IsSuccessStatusCode, "system should return empty settings when first initialized");
                Assert.IsNotNull(getOwnerAppSettingsEmpty.Content, "system should return empty settings when first initialized");
                Assert.IsNotNull(getOwnerAppSettingsEmpty.Content.Settings, "system should return empty settings when first initialized");
                
                var ownerSettings = new OwnerAppSettings()
                {
                    Settings = new Dictionary<string, string>()
                    {
                        { "setting1", "value1" },
                        { "setting2", "value2" }
                    }
                };

                await svc.UpdateOwnerAppSetting(ownerSettings);
                var getSettingsResponse = await svc.GetOwnerAppSettings();
                Assert.IsTrue(getSettingsResponse.IsSuccessStatusCode);
                Assert.IsNotNull(getSettingsResponse.Content);
                var updatedSettings = getSettingsResponse.Content;

                CollectionAssert.AreEquivalent(ownerSettings.Settings, updatedSettings.Settings);
            }
        }
    }
}