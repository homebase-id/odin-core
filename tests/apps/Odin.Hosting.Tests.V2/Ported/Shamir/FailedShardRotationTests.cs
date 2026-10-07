using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.Ported.Shamir;

/// <summary>
/// A shard rotation that fails is not retried on every owner request: <c>OwnerAuthenticationHandler</c>
/// checks for rotation on each one, and <see cref="ShardRotationGate"/> holds the next attempt off
/// for an hour.
/// </summary>
/// <remarks>
/// Its own fixture because the gate is a tenant singleton that outlives <c>V2Fixture</c>'s per-test
/// reset; tripping it in a shared fixture would stop rotation in the tests that follow.
/// </remarks>
[TestFixture]
public class FailedShardRotationTests : ShamirFixture
{
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
    [
        // the rotation failing, once, is the arrange
        "Cannot resolve client access token; not connected",
        "Failed to start shard rotation shards"
    ];

    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task FailedRotationIsNotRetriedOnTheNextRequest()
    {
        var (frodo, players, config) = await ArrangeDelegateShardsAsync();

        // a delegate drops out; the other three still meet the threshold, so recovery succeeds but the
        // rotation after it cannot send to the one that left
        var disconnectResponse = await frodo.Connections.DisconnectFrom(players[0].Identity);
        Assert.That(disconnectResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(players.Count - 1, Is.GreaterThanOrEqualTo(config.MinMatchingShards));

        var recovered = await RecoverAsync(frodo, players.Skip(1).ToList(), config, NewPassword);

        var settingsResponse = await recovered.RefitFor<IRefitOwnerConfiguration>().GetTenantSettings();
        Assert.That(settingsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertHasDebugLogEvent(ShamirConfigurationService.RotateShardsHasStarted, count: 1);

        // a second owner request: the rotation failed (nothing saved) and was not tried again
        var afterConfig = await GetDealerShardConfigAsync(recovered);
        Assert.That(afterConfig.Updated.milliseconds, Is.EqualTo(config.Updated.milliseconds));
        AssertHasDebugLogEvent(ShamirConfigurationService.RotateShardsHasStarted, count: 1);

        // the pre-recovery shards still work, so the owner is told to reconfigure
        Assert.That(await IsRotationPendingAsync(recovered), Is.True);
    }
}
