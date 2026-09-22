#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Security.Email;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;

namespace Odin.Hosting.Tests.V2.Ported.Shamir;

/// <summary>
/// The arrange every <c>OwnerApi/Shamir</c> port shares: one dealer (Frodo) plus the four players
/// (Sam, Merry, Pippin, Tom Bombadil) each original listed, the connection handshake between them,
/// shard distribution + verification, and the enter / exit recovery-mode dance.
/// </summary>
/// <remarks>
/// <para>
/// A base fixture rather than a static helper in the shape of
/// <c>Ported/Concepts/CollabScenario</c>, because all five originals carried byte-identical copies
/// of <c>PrepareConnections</c> / <c>DistributeAndVerify*Shards</c> / <c>EnterRecoveryMode</c> /
/// <c>ExitRecoveryMode</c>, and half of those need the host itself (the recovery nonces are read out
/// of the host's log store, and the cast is logged in once for the whole fixture — see below).
/// Nothing here is per-fixture: the identity list, the drain points and the assertions are the same
/// in all five originals.
/// </para>
/// <para>
/// <b>The cast is logged in once, in <see cref="WarmTenantBaselineAsync"/>.</b> Five owner logins
/// each pay PBKDF2 — three client-side 100k-iteration passes plus a server-side fourth — which is
/// pure CPU no test here is measuring, and all twelve tests want the same five sessions. The base
/// already logs every identity in before <c>TakeBaselineAsync</c> (it has to: the login is what sets
/// the password the snapshot needs) and throws the sessions away; this override keeps them. The
/// tokens are issued <i>before</i> the baseline is taken, so they live in the snapshotted identity DB
/// and every per-test <c>ResetAsync</c> restores them rather than invalidating them. Same trick, same
/// reason, as <c>Ported/Transit/AppTransitQueryTestsForPublicFiles</c>.
/// </para>
/// <para>
/// Nothing that <i>asserts</i> moves into the baseline with it: <see cref="PrepareConnectionsAsync"/>
/// and <see cref="DistributeAndVerifyShardsAsync"/> both carry assertions and so stay in the tests,
/// where a failure is a failed test rather than a broken fixture.
/// </para>
/// <para>
/// <b>Recovery nonces come out of the log, as they did in V1.</b> <c>RecoveryNotifier</c> writes the
/// enter / exit / finalize nonce at <c>Information</c> under a named property — explicitly "for
/// integration testing", and only under <c>#if DEBUG</c> — because with <c>Mailgun:Enabled</c> false
/// no mail is ever sent. V1 polled for that property with
/// <c>WebScaffold.WaitForLogPropertyValue</c> (180 s budget, 100 ms ticks). Here
/// <see cref="ReadLogPropertyValue"/> reads once and throws if absent: the notifier logs
/// synchronously inside the request that the test just awaited, so there is nothing to wait for, and
/// a poll against a store that will never fill is exactly the shape that costs a full timeout. Both
/// scan newest-first — the resubmit test enters recovery mode twice and must pick up the second
/// nonce, since <c>GetNonceDataOrFail</c> pops the first.
/// </para>
/// <para>
/// <b>Outbox verdict.</b> <c>ConfigureShards</c> writes each player's encrypted shard to the
/// transient temp drive and sends it peer-wise, then signals the outbox through a post-commit
/// <c>ProcessOutboxNow</c> — which the fast host's <c>NonNotifyingBackgroundServiceManager</c>
/// swallows. V1's <c>WaitForEmptyOutbox(TransientTempDrive)</c> becomes
/// <c>dealer.Sync.DrainOutboxAsync()</c>. Every player is a live tenant in-process, so the file
/// worker resolves each send permanently and the items are <i>gone</i> after the drain; that is what
/// lets <c>VerifyShards</c> — which asks each player whether it is holding the shard — come back
/// valid. Same verdict as <c>Ported/Configuration/SystemInitializeConfigTestsAutomatedPasswordRecovery</c>,
/// which drives the automated half of the same code path. No test here asserts on outbox contents.
/// </para>
/// <para>
/// <b>No recipient inbox processing is needed and none is done.</b> The shard transfer is a
/// transient file addressed at the players' <c>ShardRecoveryDrive</c>; the assertion that it landed
/// is <c>VerifyShards</c>, a live peer round-trip, and it passes off the drain alone.
/// </para>
/// <para>
/// <b>Carried latent defect:</b> the originals assert the recovery nonce with
/// <c>Is.Not.Null.Or.Empty</c>, which reads as "not null, or empty" and so accepts the empty string.
/// Carried verbatim — it is decorative either way, because the lookup throws when the property is
/// missing (V1 threw <c>TimeoutException</c>; here <see cref="ReadLogPropertyValue"/> throws).
/// </para>
/// </remarks>
public abstract class ShamirFixture : V2Fixture
{
    /// <summary>
    /// The four players, in the order every original listed them. All four are real tenants on the
    /// host: the shards are delivered to them and then verified over a peer call, so a merely-named
    /// identity would not do.
    /// </summary>
    protected static readonly string[] PlayerIdentities =
        [Identities.Sam, Identities.Merry, Identities.Pippin, Identities.TomBombadil];

    /// <summary>Frodo is the dealer in all five originals; the players follow.</summary>
    protected override string[] HostIdentities => [Identities.Frodo, .. PlayerIdentities];

    private OwnerSession _dealer = null!;
    private IReadOnlyList<OwnerSession> _players = null!;

    /// <summary>
    /// Logs the dealer and all four players in once, before the baseline snapshot is taken, so
    /// <see cref="Cast"/> can hand the same sessions to every test. See the class remarks.
    /// </summary>
    protected override async Task WarmTenantBaselineAsync()
    {
        await base.WarmTenantBaselineAsync();

        _dealer = await LoginAsOwner(Identities.Frodo);

        var players = new List<OwnerSession>();
        foreach (var identity in PlayerIdentities)
        {
            players.Add(await LoginAsOwner(identity));
        }

        _players = players;
    }

    /// <summary>The dealer and the four players, logged in by <see cref="WarmTenantBaselineAsync"/>.</summary>
    protected (OwnerSession Dealer, IReadOnlyList<OwnerSession> Players) Cast() => (_dealer, _players);

    /// <summary>
    /// The arrange the five delegate fixtures open with, byte-identical in all six of their tests:
    /// connect the cast, distribute delegate shards at the minimum allowed count, and read back the
    /// dealer's published configuration.
    /// </summary>
    protected async Task<(OwnerSession Dealer, IReadOnlyList<OwnerSession> Players, DealerShardConfig Config)>
        ArrangeDelegateShardsAsync()
    {
        var (dealer, players) = Cast();

        await PrepareConnectionsAsync(dealer, players);

        await DistributeAndVerifyShardsAsync(dealer, players, PlayerType.Delegate,
            minMatchingShards: ShamirConfigurationService.CalculateMinAllowedShardCount(players.Count));

        var config = await GetDealerShardConfigAsync(dealer);

        return (dealer, players, config);
    }

    /// <summary>
    /// The owner security surface. It is the system under test in every one of these fixtures, so it
    /// goes through <see cref="OwnerSession.RefitFor{T}"/> rather than <c>owner.Admin</c>.
    /// </summary>
    protected static ITestSecurityContextOwnerClient SecurityOf(OwnerSession owner)
        => owner.RefitFor<ITestSecurityContextOwnerClient>();

    /// <summary>
    /// The recovery endpoints V1's <c>SecurityApiClient</c> reached with an <i>anonymous</i> client —
    /// initiate / verify-enter / exit / verify-exit / finalize / status. They are the links a browser
    /// follows out of a recovery email, with no owner session in hand, which is the whole point.
    /// </summary>
    protected ITestSecurityContextOwnerClient AnonymousSecurityOf(OwnerSession owner)
        => Host.AnonymousRefitFor<ITestSecurityContextOwnerClient>(owner.Identity.DomainName);

    /// <summary>Connection request + accept, dealer to each player. Note: no circles.</summary>
    protected static async Task PrepareConnectionsAsync(OwnerSession dealer, IEnumerable<OwnerSession> players)
    {
        // Note: no circles. The ShardRecoveryDrive write grant rides the builtin
        // ConfirmedConnections circle, which the handshake grants on its own.
        foreach (var player in players)
        {
            await PeerFlow.ConnectAsync(dealer, player);
        }
    }

    /// <summary>
    /// The request <c>configure-shards</c> takes: one <see cref="ShamiraPlayer"/> per session, all of
    /// <paramref name="type"/>.
    /// </summary>
    protected static ConfigureShardsRequest ShardRequest(
        IReadOnlyList<OwnerSession> players, PlayerType type, int minMatchingShards) => new()
    {
        Players = players.Select(p => new ShamiraPlayer
        {
            OdinId = p.Identity,
            Type = type
        }).ToList(),
        MinMatchingShards = minMatchingShards
    };

    /// <summary>
    /// Configures shards for <paramref name="players"/>, drains the dealer's outbox so they are
    /// delivered, then asks every player to confirm it is holding its shard.
    /// </summary>
    protected static async Task DistributeAndVerifyShardsAsync(
        OwnerSession dealer,
        IReadOnlyList<OwnerSession> players,
        PlayerType playerType,
        int minMatchingShards)
    {
        var security = SecurityOf(dealer);

        var configureShardsResponse =
            await security.ConfigureShards(ShardRequest(players, playerType, minMatchingShards));
        Assert.That(configureShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await dealer.Sync.DrainOutboxAsync();

        var verifyShardsResponse = await security.VerifyShards();
        Assert.That(verifyShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = verifyShardsResponse.Content;
        Assert.That(results, Is.Not.Null);
        Assert.That(results!.Players, Is.Not.Null);
        Assert.That(results.Players.Count, Is.EqualTo(players.Count),
            "mismatch number of shards in verified results");
        // Names the players that failed rather than printing "Expected: True".
        Assert.That(results.Players.Where(p => !p.Value.IsValid).Select(p => p.Key), Is.Empty,
            "one or more players not verified");
    }

    /// <summary>
    /// Requests recovery mode, follows the emailed verify-enter link, and asserts the redirect the
    /// controller answers with.
    /// </summary>
    protected async Task EnterRecoveryModeAsync(OwnerSession dealer)
    {
        var security = AnonymousSecurityOf(dealer);

        var enterResponse = await security.InitiateRecoveryMode();
        Assert.That(enterResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // watch for the recovery links
        var nonceId = ReadLogPropertyValue(RecoveryNotifier.EnterNoncePropertyName);
        Assert.That(nonceId, Is.Not.Null.Or.Empty, "Could not find recovery link");

        var verifyEnterResponse = await security.VerifyEnterRecoveryMode(nonceId);
        Assert.That(verifyEnterResponse.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
    }

    /// <summary>
    /// Requests an exit from recovery mode and follows the emailed verify-exit link. Note the
    /// originals do <i>not</i> assert on the exit nonce itself, only on the redirect.
    /// </summary>
    protected async Task ExitRecoveryModeAsync(OwnerSession dealer)
    {
        var security = AnonymousSecurityOf(dealer);

        var exitResponse = await security.ExitRecoveryMode();
        Assert.That(exitResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var exitRecoveryNonceId = ReadLogPropertyValue(RecoveryNotifier.ExitNoncePropertyName);

        var verifyExitResponse = await security.VerifyExitRecoveryMode(exitRecoveryNonceId);
        Assert.That(verifyExitResponse.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
    }

    /// <summary>
    /// Reads the dealer's recovery status and asserts it sits at <paramref name="expected"/>. A
    /// straight read, not a poll: an approval or rejection is delivered synchronously inside the
    /// request the caller has already awaited — see <see cref="ShamirPasswordRecoveryTestForDelegates"/>'s
    /// remarks for why there is nothing to wait for.
    /// </summary>
    protected async Task AssertRecoveryStateAsync(OwnerSession dealer, ShamirRecoveryState expected)
    {
        var getRecoveryStatusResponse = await AnonymousSecurityOf(dealer).GetShamirRecoverStatus();
        Assert.That(getRecoveryStatusResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getRecoveryStatusResponse.Content!.State, Is.EqualTo(expected));
    }

    /// <summary>
    /// The pending release request a player holds for the shard the dealer gave it, or null when the
    /// player has none.
    /// </summary>
    protected static async Task<ShardApprovalRequest?> GetPlayerShardRequestAsync(
        DealerShardConfig config, OwnerSession player)
    {
        var shardId = config.Envelopes.Single(e => e.Player.OdinId == player.Identity).ShardId;

        var getListOfShardRequestsResponse = await SecurityOf(player).GetShardRequestList();
        Assert.That(getListOfShardRequestsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getListOfShardRequestsResponse.Content, Is.Not.Null);

        return getListOfShardRequestsResponse.Content!.SingleOrDefault(item => item.ShardId == shardId);
    }

    /// <summary>The dealer's own view of the shard configuration it published.</summary>
    protected static async Task<DealerShardConfig> GetDealerShardConfigAsync(OwnerSession dealer)
    {
        var getConfigResponse = await SecurityOf(dealer).GetShardConfig();
        Assert.That(getConfigResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getConfigResponse.Content, Is.Not.Null);
        return getConfigResponse.Content!;
    }

    /// <summary>Asserts every player is holding the dealer's release request, and nothing more.</summary>
    protected static async Task AssertEveryPlayerHasRequestAsync(
        DealerShardConfig config, IEnumerable<OwnerSession> players)
    {
        foreach (var player in players)
        {
            var item = await GetPlayerShardRequestAsync(config, player);
            Assert.That(item, Is.Not.Null, "Release request for shard was not found");
        }
    }

    /// <summary>Each player finds the dealer's release request in its list and approves it.</summary>
    protected static async Task ApproveEveryShardRequestAsync(
        OwnerSession dealer, IReadOnlyList<OwnerSession> players, DealerShardConfig config)
    {
        foreach (var player in players)
        {
            var item = await GetPlayerShardRequestAsync(config, player);
            Assert.That(item, Is.Not.Null, "Release request for shard was not found");

            // now release the shard
            var approveResponse = await SecurityOf(player).ApproveShardRequest(new ApproveShardRequest
            {
                OdinId = dealer.Identity,
                ShardId = item!.ShardId
            });

            Assert.That(approveResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// The refusing twin of <see cref="ApproveEveryShardRequestAsync"/>: every player finds the
    /// dealer's release request and rejects it.
    /// </summary>
    protected static async Task RejectEveryShardRequestAsync(
        OwnerSession dealer, IReadOnlyList<OwnerSession> players, DealerShardConfig config)
    {
        foreach (var player in players)
        {
            var item = await GetPlayerShardRequestAsync(config, player);
            Assert.That(item, Is.Not.Null, "Release request for shard was not found");

            // now release the shard
            var rejectResponse = await SecurityOf(player).RejectShardRequest(new RejectShardRequest
            {
                OdinId = dealer.Identity,
                ShardId = item!.ShardId
            });

            Assert.That(rejectResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// Newest-first lookup of a named log property — the V2 stand-in for
    /// <c>WebScaffold.WaitForLogPropertyValue</c>. Throws rather than returning null so a missing
    /// nonce reads as a broken arrange, exactly as V1's <c>TimeoutException</c> did.
    /// </summary>
    /// <remarks>
    /// Every level is scanned, not just <c>Information</c>. These property names are unique to a
    /// single <c>RecoveryNotifier</c> call site, so there is nothing to disambiguate by level — while
    /// pinning the level means a product change from <c>LogInformation</c> to <c>LogDebug</c> would
    /// turn five tests into a confusing "broken arrange" throw instead of a clean pass.
    /// </remarks>
    protected string ReadLogPropertyValue(string propertyName)
    {
        // Newest-first: the resubmit test enters recovery mode twice and needs the second nonce.
        // Each level's list is chronological and GetLogEvents() hands back a copy, so unlike V1's
        // in-place Reverse() this does not mutate the store's own list.
        var match = Host.LogStore.GetLogEvents().Values
            .SelectMany(events => events)
            .LastOrDefault(e => e.Properties.ContainsKey(propertyName));

        if (match == null)
        {
            throw new InvalidOperationException(
                $"No log event carried the property '{propertyName}'. " +
                "These nonces are only logged under #if DEBUG — see RecoveryNotifier.");
        }

        return match.Properties[propertyName]?.ToString() ?? string.Empty;
    }
}
