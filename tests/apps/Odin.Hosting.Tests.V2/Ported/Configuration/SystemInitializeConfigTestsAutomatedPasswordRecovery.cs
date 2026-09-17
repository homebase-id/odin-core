using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Configuration;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Port of <c>OwnerApi/Configuration/SystemInit/SystemInitializeConfigTestsAutomatedPasswordRecovery</c>.
/// Once an identity has completed initial setup it can turn on automated password recovery, which
/// splits the recovery key across the four automated players configured in
/// <c>AccountRecovery:AutomatedPasswordRecoveryIdentities</c> and ships each a shard.
/// </summary>
/// <remarks>
/// <para>
/// Same friction and same answer as <see cref="SystemInitializeConfigTests"/>: initial setup is part
/// of the subject, so <see cref="V2Fixture.WarmTenantBaselineAsync"/> is overridden to log each owner
/// in (setting the password the snapshot baseline needs) without calling
/// <c>Admin.InitializeIdentity()</c>. The test's first assertion — <c>isconfigured</c> is false —
/// is what proves the override took.
/// </para>
/// <para>
/// Deviations, all checked:
/// <list type="bullet">
/// <item>The original read each auto-player's <c>TestIdentity</c> out of
/// <c>TestIdentities.InitializedIdentities</c>, which is <b>null</b> under this framework (only
/// <c>WebScaffold.RunBeforeAnyTests</c> populates it). The players are logged in by domain name
/// instead — the same four names the original hard-coded.</item>
/// <item><c>DriveRedux.WaitForEmptyOutbox(TransientTempDrive)</c> is a passive poll of the outbox
/// background service, which the fast host registers but never starts. It becomes
/// <c>frodo.Sync.DrainOutboxAsync()</c>. The shard-distribution worker resolves each send
/// permanently here (every player is reachable in-process), so the items are gone after the drain
/// and <c>VerifyShards</c> sees four stored shards — which is exactly what the test then asserts.</item>
/// <item><c>EnableAutoPasswordRecovery</c>, <c>GetDealerShardConfig</c> and <c>VerifyShards</c> are
/// the system under test, so they go through <see cref="OwnerSession.RefitFor{T}"/>.</item>
/// </list>
/// </para>
/// <para>No caller matrix in the original and none added.</para>
/// </remarks>
[TestFixture]
public class SystemInitializeConfigTestsAutomatedPasswordRecovery : V2Fixture
{
    /// <summary>
    /// The four identities named by <c>AccountRecovery:AutomatedPasswordRecoveryIdentities</c> in
    /// <c>appsettings.development.json</c>. They have to be real tenants on this host for the shards
    /// to be delivered and then verified.
    /// </summary>
    private static readonly string[] AutoPlayers =
        [Identities.TomBombadil, Identities.Collab, Identities.Merry, Identities.Pippin];

    protected override string[] HostIdentities => [Identities.Frodo, .. AutoPlayers];

    /// <summary>The system under test is initial setup itself, so the tenants must not be initialized.</summary>
    protected override bool InitializeIdentities => false;

    [Test]
    public async Task CanInitializeSystem_WithAutomatedPasswordRecovery()
    {
        // initialize everyone else
        foreach (var odinId in AutoPlayers)
        {
            var player = await LoginAsOwner(odinId);
            await player.RefitFor<IRefitOwnerConfiguration>().InitializeIdentity(new InitialSetupRequest());
        }

        var frodo = await LoginAsOwner(Identities.Frodo);
        var config = frodo.RefitFor<IRefitOwnerConfiguration>();

        //success = system drives created, other drives created
        var getIsIdentityConfiguredResponse1 = await config.IsIdentityConfigured();
        Assert.That(getIsIdentityConfiguredResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getIsIdentityConfiguredResponse1.Content, Is.False);

        var setupConfig = new InitialSetupRequest
        {
            Drives = null,
            Circles = null,
        };

        var initIdentityResponse = await config.InitializeIdentity(setupConfig);
        Assert.That(initIdentityResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getIsIdentityConfiguredResponse = await config.IsIdentityConfigured();
        Assert.That(getIsIdentityConfiguredResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getIsIdentityConfiguredResponse.Content, Is.True);

        // now that drives are setup, we can enable auto password recovery
        var enableAutoPasswordRecoveryResponse = await config.EnableAutoPasswordRecovery();
        Assert.That(enableAutoPasswordRecoveryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var security = frodo.RefitFor<ITestSecurityContextOwnerClient>();
        var shardConfigResponse = await security.GetShardConfig();
        // should have the automated identities configured
        var shardConfig = shardConfigResponse.Content;

        Assert.That(shardConfig, Is.Not.Null);

        AssertAutomaticPlayer(shardConfig!, TestIdentities.Pippin.OdinId);
        AssertAutomaticPlayer(shardConfig!, TestIdentities.Collab.OdinId);
        AssertAutomaticPlayer(shardConfig!, TestIdentities.Merry.OdinId);
        AssertAutomaticPlayer(shardConfig!, TestIdentities.TomBombadil.OdinId);

        Assert.That(shardConfig!.MinMatchingShards, Is.EqualTo(ShamirConfigurationService.MinimumPlayerCount));

        // verify the shards made it to the identities

        await frodo.Sync.DrainOutboxAsync();

        var verifyShardsResponse = await security.VerifyShards();
        Assert.That(verifyShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = verifyShardsResponse.Content;
        Assert.That(results, Is.Not.Null);
        Assert.That(results!.Players, Is.Not.Null);
        Assert.That(results.Players.Count, Is.EqualTo(4), "mismatch number of shards in verified results");
        Assert.That(results.Players.Values.All(p => p.IsValid), Is.True, "one or more players not verified");
    }

    private static void AssertAutomaticPlayer(DealerShardConfig config, Odin.Core.Identity.OdinId expected)
    {
        Assert.That(config.Envelopes.SingleOrDefault(e => e.Player.OdinId == expected &&
                                                          e.Player.Type == PlayerType.Automatic), Is.Not.Null,
            $"no automatic envelope for {expected}");
    }
}
