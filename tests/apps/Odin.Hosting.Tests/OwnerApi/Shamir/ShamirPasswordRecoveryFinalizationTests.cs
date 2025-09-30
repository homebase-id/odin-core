using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Services.Drives;
using Odin.Services.Security.Email;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;
using Serilog.Events;

namespace Odin.Hosting.Tests.OwnerApi.Shamir
{
    public class ShamirPasswordRecoveryFinalizationTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: true, setupOwnerAccounts: true,
                testIdentities:
                [
                    TestIdentities.Frodo,
                    TestIdentities.Samwise,
                    TestIdentities.Merry,
                    TestIdentities.Pippin,
                    TestIdentities.TomBombadil
                ]);

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
        [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
        public async Task DelegatePlayersCanApproveShardRequestsAndPasswordCanBeReset()
        {
            var dealer = TestIdentities.Frodo;
            List<OdinId> peerIdentities =
            [
                TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId, TestIdentities.Pippin.OdinId, TestIdentities.TomBombadil.OdinId
            ];

            var frodoClient = _scaffold.CreateOwnerApiClientRedux(dealer);

            //
            // Setup - distribute delegate shards
            //
            await PrepareConnections(peerIdentities);

            await DistributeAndVerifyDelegateShards(peerIdentities);

            var getConfigResponse = await frodoClient.Security.GetDealerShardConfig();
            Assert.That(getConfigResponse.IsSuccessful, Is.True);
            Assert.That(getConfigResponse.Content, Is.Not.Null);

            var config = getConfigResponse.Content;

            //
            // Act - enter recovery mode
            //
            await EnterRecoveryMode();

            //
            // Assert - all player delegates have a request in their list
            //
            foreach (var peer in peerIdentities)
            {
                var peerOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.InitializedIdentities[peer]);
                var item = await GetPlayerShardRequest(config, peerOwnerClient);
                Assert.That(item, Is.Not.Null, "Release request for shard was not found");
                var shardId = item.ShardId;

                // now release the shard
                var approveResponse = await peerOwnerClient.Security.ApproveShardRequest(new ApproveShardRequest
                {
                    OdinId = dealer.OdinId,
                    ShardId = shardId
                });

                Assert.That(approveResponse.IsSuccessStatusCode, Is.True, $"Code was {approveResponse.StatusCode}");
            }

            // Wait for status
            var recoverStatus = await frodoClient.Security.WaitForShamirStatus(ShamirRecoveryState.AwaitingOwnerFinalization);

            // this is a dumb test but I just wanted to be clear about success criterion (i.e. an explicit assert)
            Assert.That(recoverStatus.State == ShamirRecoveryState.AwaitingOwnerFinalization);

            // scan for the nonceId

            var finalizeNonceId = await _scaffold.WaitForLogPropertyValue(
                RecoveryEmailer.FinalRecoveryNonceIdPropertyName,
                LogEventLevel.Information);

            var finalRecoveryKey = await _scaffold.WaitForLogPropertyValue(
                RecoveryEmailer.FinalRecoveryKeyPropertyName,
                LogEventLevel.Information);

            Assert.That(finalizeNonceId, Is.Not.Null.Or.Empty, "Could not find final recovery email link");
            Assert.That(finalRecoveryKey, Is.Not.Null.Or.Empty, "Could not find final recovery email link");

            //
            // Cleanup connections before resetting the password so we can still use the old context
            //
            await CleanupConnections(peerIdentities);
            
            const string newPassword = "bipbopboop";

            using var authClient = _scaffold.OldOwnerApi.CreateAnonymousClient(frodoClient.OdinId);
            var passwordEccKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
            var saltyReply = await _scaffold.OldOwnerApi.CalculatePasswordReply(authClient, newPassword, passwordEccKey);

            // here we will call finalize to get the recovery key
            var finalizeRecoveryResponse = await frodoClient.Security.FinalizeRecovery(new FinalRecoveryRequest
            {
                Id = finalizeNonceId,
                FinalKey = finalRecoveryKey,
                PasswordReply = saltyReply
            });

            Assert.That(finalizeRecoveryResponse.IsSuccessful, Is.True);

            //login with the new password
            var loginEccKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
            var secondLogin = await _scaffold.OldOwnerApi.LoginToOwnerConsole(frodoClient.OdinId, newPassword, loginEccKey);
            Assert.That(secondLogin.cat.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(secondLogin.cat.AccessTokenHalfKey.IsSet(), Is.True);
            Assert.That(secondLogin.sharedSecret.IsSet, Is.True);
        }

        [Test]
#if !DEBUG
        [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
        public async Task ShardingIsResetAfterPasswordIsRecovered()
        {
            var dealer = TestIdentities.Frodo;
            List<OdinId> peerIdentities =
            [
                TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId, TestIdentities.Pippin.OdinId, TestIdentities.TomBombadil.OdinId
            ];

            var frodoClient = _scaffold.CreateOwnerApiClientRedux(dealer);

            //
            // Setup - distribute delegate shards
            //
            await PrepareConnections(peerIdentities);

            await DistributeAndVerifyDelegateShards(peerIdentities);

            var getConfigResponse = await frodoClient.Security.GetDealerShardConfig();
            Assert.That(getConfigResponse.IsSuccessful, Is.True);
            Assert.That(getConfigResponse.Content, Is.Not.Null);

            var config = getConfigResponse.Content;

            //
            // Act - enter recovery mode
            //
            await EnterRecoveryMode();

            //
            // Assert - all player delegates have a request in their list
            //
            foreach (var peer in peerIdentities)
            {
                var peerOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.InitializedIdentities[peer]);
                var item = await GetPlayerShardRequest(config, peerOwnerClient);
                Assert.That(item, Is.Not.Null, "Release request for shard was not found");
                var shardId = item.ShardId;

                // now release the shard
                var approveResponse = await peerOwnerClient.Security.ApproveShardRequest(new ApproveShardRequest
                {
                    OdinId = dealer.OdinId,
                    ShardId = shardId
                });

                Assert.That(approveResponse.IsSuccessStatusCode, Is.True, $"Code was {approveResponse.StatusCode}");
            }

            // Wait for status
            var recoverStatus = await frodoClient.Security.WaitForShamirStatus(ShamirRecoveryState.AwaitingOwnerFinalization);

            // this is a dumb test but I just wanted to be clear about success criterion (i.e. an explicit assert)
            Assert.That(recoverStatus.State == ShamirRecoveryState.AwaitingOwnerFinalization);

            // scan for the nonceId

            var finalizeNonceId = await _scaffold.WaitForLogPropertyValue(
                RecoveryEmailer.FinalRecoveryNonceIdPropertyName,
                LogEventLevel.Information);

            var finalRecoveryKey = await _scaffold.WaitForLogPropertyValue(
                RecoveryEmailer.FinalRecoveryKeyPropertyName,
                LogEventLevel.Information);

            Assert.That(finalizeNonceId, Is.Not.Null.Or.Empty, "Could not find final recovery email link");
            Assert.That(finalRecoveryKey, Is.Not.Null.Or.Empty, "Could not find final recovery email link");

            const string newPassword = "bipbopboop";

            using var authClient = _scaffold.OldOwnerApi.CreateAnonymousClient(frodoClient.OdinId);
            var passwordEccKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
            var saltyReply = await _scaffold.OldOwnerApi.CalculatePasswordReply(authClient, newPassword, passwordEccKey);

            // here we will call finalize to get the recovery key
            var finalizeRecoveryResponse = await frodoClient.Security.FinalizeRecovery(new FinalRecoveryRequest
            {
                Id = finalizeNonceId,
                FinalKey = finalRecoveryKey,
                PasswordReply = saltyReply
            });

            Assert.That(finalizeRecoveryResponse.IsSuccessful, Is.True);
            
            //login with the new password
            var loginEccKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
            var secondLogin = await _scaffold.OldOwnerApi.LoginToOwnerConsole(frodoClient.OdinId, newPassword, loginEccKey);
            Assert.That(secondLogin.cat.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(secondLogin.cat.AccessTokenHalfKey.IsSet(), Is.True);
            Assert.That(secondLogin.sharedSecret.IsSet, Is.True);

            // the system should configure shards again
            // so lets call something on the owner console to stoke the auth handler
            var newClient = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(frodoClient.OdinId, 
                secondLogin.cat, 
                secondLogin.sharedSecret,
                FileSystemType.Standard);
            
            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(newClient, secondLogin.sharedSecret);
            var settingsResponse = await svc.GetTenantSettings();
            Assert.That(settingsResponse.IsSuccessful, Is.True);
            // var settings = settingsResponse.Content;
            
            // now just see if the log is updated with an entry that we rotated shards

            _scaffold.AssertHasDebugLogEvent(message: ShamirConfigurationService.RotateShardsHasStarted, count: 1);
            
            // TODO: need to reset the test scaffolds login stuff
            //
            // cleanup connections
            //
            // await CleanupConnections(peerIdentities);
        }

        private async Task<ShardApprovalRequest> GetPlayerShardRequest(DealerShardConfig config, OwnerApiClientRedux peerOwnerClient)
        {
            var peer = peerOwnerClient.OdinId;
            var shardId = config.Envelopes.Single(e => e.Player.OdinId == peer).ShardId;
            var getListOfShardRequestsResponse = await peerOwnerClient.Security.GetShardRequestList();

            Assert.That(getListOfShardRequestsResponse.IsSuccessful, Is.True);
            Assert.That(getListOfShardRequestsResponse.Content, Is.Not.Null);

            var list = getListOfShardRequestsResponse.Content;

            var item = list.SingleOrDefault(item => item.ShardId == shardId);
            return item;
        }

        private async Task PrepareConnections(List<OdinId> peers)
        {
            // Note: no circles
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            foreach (var peer in peers)
            {
                var sendConnectionRequestResponse = await frodo.Connections.SendConnectionRequest(peer);
                Assert.That(sendConnectionRequestResponse.IsSuccessful, Is.True);

                var peerOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.InitializedIdentities[peer]);
                var acceptConnectionRequestResponse = await peerOwnerClient.Connections.AcceptConnectionRequest(frodo.OdinId);
                Assert.That(acceptConnectionRequestResponse.IsSuccessful, Is.True);
            }
        }

        private async Task DistributeAndVerifyDelegateShards(List<OdinId> peerIdentities)
        {
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            var players = peerIdentities.Select(d => new ShamiraPlayer()
            {
                OdinId = d,
                Type = PlayerType.Delegate
            }).ToList();

            var shardRequest = new ConfigureShardsRequest
            {
                Players = players,
                MinMatchingShards = ShamirConfigurationService.CalculateMinAllowedShardCount(players.Count)
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

                var peerOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.InitializedIdentities[peer]);
                var acceptConnectionRequestResponse = await peerOwnerClient.Connections.DisconnectFrom(frodo.OdinId);
                Assert.That(acceptConnectionRequestResponse.IsSuccessful, Is.True);
            }
        }

        private async Task EnterRecoveryMode()
        {
            // enter recovery mode
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            var enterResponse = await frodo.Security.EnterRecoveryMode();
            Assert.That(enterResponse.IsSuccessful, Is.True);

            // watch for the recovery links
            var nonceId = await _scaffold.WaitForLogPropertyValue(RecoveryEmailer.EnterNoncePropertyName, LogEventLevel.Information);
            Assert.That(nonceId, Is.Not.Null.Or.Empty, "Could not find recovery link");

            var verifyEnterResponse = await frodo.Security.VerifyEnterRecoveryMode(nonceId);
            Assert.That(verifyEnterResponse.StatusCode == HttpStatusCode.Redirect, Is.True,
                $"Response was {verifyEnterResponse.StatusCode}");
        }

        private async Task ExitRecoveryMode()
        {
            // enter recovery mode
            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            // Act - exit recovery mode
            var exitEnterResponse = await frodo.Security.ExitRecoveryMode();
            Assert.That(exitEnterResponse.IsSuccessful, Is.True, $"Response was {exitEnterResponse.StatusCode}");

            // Assert
            var exitRecoveryNonceId =
                await _scaffold.WaitForLogPropertyValue(RecoveryEmailer.ExitNoncePropertyName, LogEventLevel.Information);

            var verifyExitRecoveryModeResponse = await frodo.Security.VerifyExitRecoveryMode(exitRecoveryNonceId);
            Assert.That(verifyExitRecoveryModeResponse.StatusCode == HttpStatusCode.Redirect, Is.True,
                $"Response was {verifyExitRecoveryModeResponse.StatusCode}");
        }
    }
}