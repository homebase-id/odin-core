using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Configuration;

namespace Odin.Hosting.Tests.OwnerApi.Configuration
{
    public class OwnerAppSettingsTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: true);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [SetUp]
        public void Setup()
        {
            _scaffold.ClearAssertLogEventsAction();
            _scaffold.ClearLogEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _scaffold.AssertLogEvents();
        }


        [Test]
        public async Task CanGetAndReadOwnerAppSetting()
        {
            var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
            
            var getOwnerAppSettingsEmpty = await frodoOwnerClient.Configuration.GetOwnerAppSettings();
            ClassicAssert.IsTrue(getOwnerAppSettingsEmpty.IsSuccessStatusCode, "system should return empty settings when first initialized");
            ClassicAssert.IsNotNull(getOwnerAppSettingsEmpty.Content, "system should return empty settings when first initialized");
            ClassicAssert.IsNotNull(getOwnerAppSettingsEmpty.Content.Settings, "system should return empty settings when first initialized");

            var ownerSettings = new OwnerAppSettings()
            {
                Settings = new Dictionary<string, string>()
                {
                    { "setting1", "value1" },
                    { "setting2", "value2" }
                }
            };
            
            await frodoOwnerClient.Configuration.UpdateOwnerAppSetting(ownerSettings);
            var getSettingsResponse = await frodoOwnerClient.Configuration.GetOwnerAppSettings();
            ClassicAssert.IsTrue(getSettingsResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(getSettingsResponse.Content);
            var updatedSettings = getSettingsResponse.Content;

            CollectionAssert.AreEquivalent(ownerSettings.Settings, updatedSettings.Settings);
        }
    }
}