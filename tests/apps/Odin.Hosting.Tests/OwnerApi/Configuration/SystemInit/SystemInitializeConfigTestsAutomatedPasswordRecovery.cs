using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.OwnerApi.Configuration.SystemInit
{
    public class SystemInitializeConfigTestsAutomatedPasswordRecovery
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: false,
                testIdentities: [TestIdentities.Frodo, TestIdentities.Pippin, TestIdentities.Samwise]);
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
        public async Task CanInitializeSystem_WithAutomatedPasswordRecovery()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

            //success = system drives created, other drives created
            var getIsIdentityConfiguredResponse1 = await ownerClient.Configuration.IsIdentityConfigured();
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse1.IsSuccessStatusCode);
            ClassicAssert.IsFalse(getIsIdentityConfiguredResponse1.Content);

            var setupConfig = new InitialSetupRequest()
            {
                Drives = null,
                Circles = null,
                UseAutomatedPasswordRecovery = true // << testing this here
            };

            var initIdentityResponse = await ownerClient.Configuration.InitializeIdentity(setupConfig);
            ClassicAssert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

            var getIsIdentityConfiguredResponse = await ownerClient.Configuration.IsIdentityConfigured();
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse.Content);


            var shardConfig = await ownerClient.Security.GetDealerShardConfig();
            // should have the automated identities configured
            



        }
        
    }
}