using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Security.PasswordRecovery.Shamir;

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
                testIdentities:
                [
                    TestIdentities.Frodo, TestIdentities.Pippin, TestIdentities.Samwise,
                    TestIdentities.Collab, TestIdentities.Merry, TestIdentities.TomBombadil
                ]);
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
            // initialize everyone else

            List<OdinId> devAutoPlayers =
            [
                new("tom.dotyou.cloud"),
                new("collab.dotyou.cloud"),
                new("merry.dotyou.cloud"),
                new("pippin.dotyou.cloud"),
            ];

            foreach (var odinId in devAutoPlayers)
            {
                var client = _scaffold.CreateOwnerApiClient(TestIdentities.InitializedIdentities[odinId]);
                await client.Configuration.InitializeIdentity(new InitialSetupRequest());
            }

            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            //success = system drives created, other drives created
            var getIsIdentityConfiguredResponse1 = await frodo.Configuration.IsIdentityConfigured();
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse1.IsSuccessStatusCode);
            ClassicAssert.IsFalse(getIsIdentityConfiguredResponse1.Content);

            var setupConfig = new InitialSetupRequest()
            {
                Drives = null,
                Circles = null,
            };

            var initIdentityResponse = await frodo.Configuration.InitializeIdentity(setupConfig);
            ClassicAssert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

            var getIsIdentityConfiguredResponse = await frodo.Configuration.IsIdentityConfigured();
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse.Content);

            
            // now that drives are setup, we can enable auto password recovery
            var enableAutoPasswordRecoveryResponse = await frodo.Configuration.EnableAutoPasswordRecovery();
            ClassicAssert.IsTrue(enableAutoPasswordRecoveryResponse.IsSuccessStatusCode);

            var shardConfigResponse = await frodo.Security.GetDealerShardConfig();
            // should have the automated identities configured
            var config = shardConfigResponse.Content;

            Assert.That(config, Is.Not.Null);

            Assert.That(config.Envelopes.SingleOrDefault(e => e.Player.OdinId == TestIdentities.Pippin.OdinId &&
                                                              e.Player.Type == PlayerType.Automatic), Is.Not.Null);
            Assert.That(config.Envelopes.SingleOrDefault(e => e.Player.OdinId == TestIdentities.Collab.OdinId &&
                                                              e.Player.Type == PlayerType.Automatic), Is.Not.Null);
            Assert.That(config.Envelopes.SingleOrDefault(e => e.Player.OdinId == TestIdentities.Merry.OdinId &&
                                                              e.Player.Type == PlayerType.Automatic), Is.Not.Null);
            Assert.That(config.Envelopes.SingleOrDefault(e => e.Player.OdinId == TestIdentities.TomBombadil.OdinId &&
                                                              e.Player.Type == PlayerType.Automatic), Is.Not.Null);

            Assert.That(config.MinMatchingShards, Is.EqualTo(ShamirConfigurationService.MinimumPlayerCount));
            
            // verify the shards made it to the identities
            
            await frodo.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

            var verifyShardsResponse = await frodo.Security.VerifyShards();
            Assert.That(verifyShardsResponse.IsSuccessful, Is.True);

            var results = verifyShardsResponse.Content;
            Assert.That(results, Is.Not.Null);
            Assert.That(results.Players, Is.Not.Null);
            Assert.That(results.Players.Count, Is.EqualTo(4), "mismatch number of shards in verified results");
            Assert.That(results.Players.All(p => p.Value.IsValid), "one or more players not verified");
        }
    }
}