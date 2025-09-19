using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Services.Drives;
using Odin.Services.Security.Email;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Serilog.Events;

namespace Odin.Hosting.Tests.OwnerApi.Shamir
{
    public class ShamirPasswordRecoveryTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: true, setupOwnerAccounts: true);

            _scaffold.AssertLogEvents();
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
#if !DEBUG
        [Ignore("Ignored for release tests due to how we test reovery mode")]
#endif
        public async Task CanEnterRecoveryMode()
        {
            List<OdinId> peerIdentities =
            [
                TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId, TestIdentities.Pippin.OdinId, TestIdentities.TomBombadil.OdinId
            ];

            await PrepareConnections(peerIdentities);

            await DistributeAndVerifyAutomaticShards(peerIdentities);

            // enter recovery mode
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            var enterResponse = await frodo.Security.EnterRecoveryMode();
            Assert.That(enterResponse.IsSuccessful, Is.True);

            // watch for the recovery links
            var nonceId = await _scaffold.WaitForLogPropertyValue(RecoveryEmailer.NoncePropertyName, LogEventLevel.Information);
            Assert.That(nonceId, Is.Not.Null.Or.Empty, "Could not find recovery link");

            var verifyEnterResponse = await frodo.Security.VerifyEnterRecoveryMode(nonceId);
            Assert.That(verifyEnterResponse.StatusCode == HttpStatusCode.Redirect, Is.True, $"Response was {verifyEnterResponse.StatusCode}");

            await CleanupConnections(peerIdentities);
        }

        [Test]
        public async Task CanExitRecoveryMode()
        {
            List<OdinId> peerIdentities =
            [
                TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId, TestIdentities.Pippin.OdinId, TestIdentities.TomBombadil.OdinId
            ];

            //
            // Setup - enter recovery mode
            //
            await PrepareConnections(peerIdentities);

            await DistributeAndVerifyAutomaticShards(peerIdentities);

            // enter recovery mode
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            var enterResponse = await frodo.Security.EnterRecoveryMode();
            Assert.That(enterResponse.IsSuccessful, Is.True);

            // watch for the recovery links
            var enterRecoveryNonceId = await _scaffold.WaitForLogPropertyValue(RecoveryEmailer.NoncePropertyName, LogEventLevel.Information);
            Assert.That(enterRecoveryNonceId, Is.Not.Null.Or.Empty, "Could not find recovery link");

            var verifyEnterResponse = await frodo.Security.VerifyEnterRecoveryMode(enterRecoveryNonceId);
            Assert.That(verifyEnterResponse.StatusCode == HttpStatusCode.Redirect, Is.True, $"Response was {verifyEnterResponse.StatusCode}");
            
            // Act - exit recovery mode
            var exitEnterResponse = await frodo.Security.ExitRecoveryMode();
            Assert.That(exitEnterResponse.IsSuccessful, Is.True, $"Response was {exitEnterResponse.StatusCode}");
            
            // Assert
            var exitRecoveryNonceId = await _scaffold.WaitForLogPropertyValue(RecoveryEmailer.NoncePropertyName, LogEventLevel.Information);
            
            var verifyExitRecoveryModeResponse = await frodo.Security.VerifyExitRecoveryMode(exitRecoveryNonceId);
            Assert.That(verifyExitRecoveryModeResponse.StatusCode == HttpStatusCode.Redirect, Is.True,
                $"Response was {verifyExitRecoveryModeResponse.StatusCode}");
            
            await CleanupConnections(peerIdentities);
        }

        private async Task PrepareConnections(List<OdinId> peers)
        {
            // Note: no circles
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            foreach (var peer in peers)
            {
                var sendConnectionRequestResponse = await frodo.Connections.SendConnectionRequest(peer);
                Assert.That(sendConnectionRequestResponse.IsSuccessful, Is.True);

                var peerOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.All[peer]);
                var acceptConnectionRequestResponse = await peerOwnerClient.Connections.AcceptConnectionRequest(frodo.OdinId);
                Assert.That(acceptConnectionRequestResponse.IsSuccessful, Is.True);
            }
        }

        private async Task DistributeAndVerifyAutomaticShards(List<OdinId> peerIdentities)
        {
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            var shardRequest = new ConfigureShardsRequest
            {
                Players = peerIdentities.Select(d => new ShamiraPlayer()
                {
                    OdinId = d,
                    Type = PlayerType.Automatic
                }).ToList(),
                MinMatchingShards = 3
            };

            var configureShardsResponse = await frodo.Security.ConfigureShards(shardRequest);
            Assert.That(configureShardsResponse.IsSuccessful, Is.True);

            await frodo.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

            var verifyShardsResponse = await frodo.Security.VerifyShards();
            Assert.That(verifyShardsResponse.IsSuccessful, Is.True);

            var results = verifyShardsResponse.Content;
            Assert.That(results, Is.Not.Null);
            Assert.That(results.Players, Is.Not.Null);
            Assert.That(results.Players.Count, Is.EqualTo(shardRequest.Players.Count), "mismatch number of shards in verified results");
            Assert.That(results.Players.All(p => p.Value.IsValid), "one or more players not verified");

        }

        private async Task CleanupConnections(List<OdinId> peers)
        {
            // Note: no circles
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            foreach (var peer in peers)
            {
                var sendConnectionRequestResponse = await frodo.Connections.DisconnectFrom(peer);
                Assert.That(sendConnectionRequestResponse.IsSuccessful, Is.True);

                var peerOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.All[peer]);
                var acceptConnectionRequestResponse = await peerOwnerClient.Connections.DisconnectFrom(frodo.OdinId);
                Assert.That(acceptConnectionRequestResponse.IsSuccessful, Is.True);
            }
        }
    }
}