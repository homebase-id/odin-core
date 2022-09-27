using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.OwnerToken.Circles;
using Youverse.Hosting.Controllers.OwnerToken.Drive;
using Youverse.Hosting.Tests.OwnerApi.Circle;
using Youverse.Hosting.Tests.OwnerApi.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Configuration
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
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret))
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