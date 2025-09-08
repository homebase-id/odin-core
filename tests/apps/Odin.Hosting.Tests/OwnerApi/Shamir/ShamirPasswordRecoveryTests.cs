using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Services.Drives;
using Odin.Services.ShamiraPasswordRecovery;

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
        [Ignore("dark launched")]
        public async Task CanDistributeShardsToDelegatePeersAndVerify()
        {
            List<OdinId> peerIdentities =
            [
                TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId, TestIdentities.Pippin.OdinId, TestIdentities.TomBombadil.OdinId
            ];

            await PrepareConnections(peerIdentities);

            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            var shardRequest = new ConfigureShardsRequest
            {
                Players = peerIdentities.Select(d => new ShamiraPlayer()
                {
                    OdinId = d,
                    Type = PlayerType.Delegate
                }).ToList(),
                MinMatchingShards = 2
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

            await CleanupConnections(peerIdentities);
        }

        [Test]
        [Ignore("dark launched")]
        public async Task FailToDistributeWhenOneOrMorePeersIsNotConnected()
        {
            _scaffold.SetAssertLogEventsAction(logEvents =>
            {
                var errorLogs = logEvents[Serilog.Events.LogEventLevel.Error];
                Assert.That(errorLogs.Count, Is.EqualTo(1), "Unexpected number of Error log events");
                var error = errorLogs.First();
                Assert.That(error.MessageTemplate.Text.StartsWith("Failed while creating outbox item"), Is.True);
            });

            List<OdinId> connectedIdentities =
            [
                TestIdentities.Samwise.OdinId, TestIdentities.Merry.OdinId, TestIdentities.Pippin.OdinId
            ];

            await PrepareConnections(connectedIdentities);

            // add one who is not connected
            List<OdinId> peerIdentities = connectedIdentities.Concat([TestIdentities.TomBombadil.OdinId]).ToList();

            var frodo = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            var shardRequest = new ConfigureShardsRequest
            {
                Players = peerIdentities.Select(d => new ShamiraPlayer()
                {
                    OdinId = d,
                    Type = PlayerType.Delegate
                }).ToList(),
                MinMatchingShards = 2
            };

            var configureShardsResponse = await frodo.Security.ConfigureShards(shardRequest);
            Assert.That(configureShardsResponse.IsSuccessful, Is.False);

            await CleanupConnections(connectedIdentities);
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