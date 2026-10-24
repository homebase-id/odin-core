#nullable enable
using System;
using Autofac;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.OwnerToken.Security;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests.V2.ZeroKnowledge;
using Odin.Services.Security;
using Odin.Services.Security.Email;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Security.PasswordRecovery.Shamir.ShardRequestApproval;
using Odin.Services.Security.ZeroKnowledge;
using Refit;
using Serilog.Events;

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
/// and <see cref="ArrangeFriendDealAsync"/> both carry assertions and so stay in the tests,
/// where a failure is a failed test rather than a broken fixture.
/// </para>
/// <para>
/// <b>Recovery links come from a <see cref="CapturingRecoveryNotificationSink"/>.</b> With
/// <c>Mailgun:Enabled</c> false no mail is ever sent, so the fixture swaps in a sink that records the
/// enter / exit / finalize links <c>RecoveryNotifier</c> generates. V1 (and this port, until #1866)
/// scraped the nonces out of a <c>#if DEBUG</c>-only log line instead, which is why none of these
/// tests could run in a Release build. The notifier calls the sink synchronously inside the request
/// the test just awaited, so <see cref="CapturingRecoveryNotificationSink.Latest"/> reads once and
/// throws if absent; it takes the newest link because the resubmit test enters recovery mode twice
/// and must follow the second, <c>GetNonceDataOrFail</c> having popped the first. The sink is
/// per host, so unlike the log it cannot pick up a neighbouring fixture's links.
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
/// <b>Friend deals are dealt in the browser</b> (docs/zero-knowledge-recovery-plan.md, "Building the
/// friends pair"): <see cref="ZkFriends"/> plays the browser.  Every owner in a friend deal first makes
/// their zero-knowledge keys and converts their Shard Recovery drive, as their first login since plan A
/// does, so a gated friend's shard can be sealed to them; <see cref="ZkMasterKeyOf"/> keeps each one's
/// zero-knowledge master key, which their browser would hold.
/// </para>
/// <para>
/// <b>No recipient inbox processing is needed and none is done.</b> The shard transfer is a
/// transient file addressed at the players' <c>ShardRecoveryDrive</c>; the assertion that it landed
/// is <c>VerifyShards</c>, a live peer round-trip, and it passes off the drain alone.
/// </para>
/// </remarks>
public abstract class ShamirFixture : V2Fixture
{
    private readonly CapturingRecoveryNotificationSink _recoveryLinks = new();

    protected override void ConfigureRootContainer(ContainerBuilder cb)
    {
        cb.RegisterInstance(_recoveryLinks).As<IRecoveryNotificationSink>().SingleInstance();
    }

    /// <summary>
    /// The four players, in the order every original listed them. All four are real tenants on the
    /// host: the shards are delivered to them and then verified over a peer call, so a merely-named
    /// identity would not do.
    /// </summary>
    protected static readonly string[] PlayerIdentities =
        [Identities.Sam, Identities.Merry, Identities.Pippin, Identities.TomBombadil];

    /// <summary>
    /// The identities named by <c>AccountRecovery:AutomatedPasswordRecoveryIdentities</c> in
    /// <c>appsettings.development.json</c> — the players <c>EnableAutoPasswordRecovery</c> deals to.
    /// </summary>
    internal static readonly string[] AutomatedPlayerIdentities =
        [Identities.TomBombadil, Identities.Collab, Identities.Merry, Identities.Pippin];

    /// <summary>The password a recovery finalizes with.</summary>
    protected const string NewPassword = "bipbopboop";

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

    private readonly Dictionary<string, byte[]> _zkMasterKeys = new();

    /// <summary>The recovering browser's throwaway key pair, made when recovery starts.</summary>
    private protected ZkBrowser.KeyPair? RecoveringBrowser { get; private set; }

    /// <summary>The zero-knowledge master key <paramref name="owner"/>'s browser holds.</summary>
    protected byte[] ZkMasterKeyOf(OwnerSession owner) => _zkMasterKeys[owner.Identity.DomainName];

    /// <summary>
    /// The arrange the delegate fixtures open with: connect the cast, and a friend deal in which every
    /// player is gated, at the minimum allowed count.
    /// </summary>
    protected async Task<(OwnerSession Dealer, IReadOnlyList<OwnerSession> Players, DealerShardConfig Config)>
        ArrangeDelegateShardsAsync()
    {
        var (dealer, players) = Cast();
        var config = await ArrangeFriendDealAsync(dealer, players.Select(p => (p, PlayerType.Delegate)).ToList(),
            ShamirConfigurationService.CalculateMinAllowedShardCount(players.Count));
        return (dealer, players, config);
    }

    /// <summary>
    /// Connects the cast, makes everyone's zero-knowledge keys and converts their Shard Recovery
    /// drives, deals in the browser, delivers and verifies the shards, and, unless
    /// <paramref name="writePasswordCopy"/> is false, writes the friends' password copy as the dealer's
    /// next password login does.
    /// </summary>
    protected async Task<DealerShardConfig> ArrangeFriendDealAsync(OwnerSession dealer,
        IReadOnlyList<(OwnerSession Player, PlayerType Type)> players, int minMatchingShards, bool writePasswordCopy = true)
    {
        await PrepareConnectionsAsync(dealer, players.Select(p => p.Player));
        foreach (var owner in players.Select(p => p.Player).Prepend(dealer))
        {
            await MakeZeroKnowledgeAsync(owner);
        }

        var deal = await ZkFriends.MakeDealAsync(dealer, ZkMasterKeyOf(dealer), players, minMatchingShards);
        var response = await SecurityOf(dealer).ConfigureShards(deal.Request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        await DrainAndVerifyShardsAsync(dealer, players.Count);

        Assert.That((await ZkOwnerSteps.GetStatusAsync(dealer)).HasShamirPasswordCopy, Is.False,
            "the browser deals without the password");
        if (writePasswordCopy)
        {
            await WriteFriendsPasswordCopyAsync(dealer, OwnerLogin.DefaultPassword);
        }

        return await GetDealerShardConfigAsync(dealer);
    }

    /// <summary>The first half of <paramref name="players"/> automatic, the rest gated.</summary>
    protected static IReadOnlyList<(OwnerSession Player, PlayerType Type)> MixedPlayers(IReadOnlyList<OwnerSession> players) =>
        players.Select((p, i) => (p, i < players.Count / 2 ? PlayerType.Automatic : PlayerType.Delegate)).ToList();

    /// <summary>
    /// What <paramref name="owner"/>'s first login since plan A does: the zero-knowledge keys, and the
    /// Shard Recovery drive converted.
    /// </summary>
    protected async Task MakeZeroKnowledgeAsync(OwnerSession owner)
    {
        if (_zkMasterKeys.ContainsKey(owner.Identity.DomainName) &&
            (await ZkOwnerSteps.GetStatusAsync(owner)).ZkSaltKek64 != null)
        {
            return;
        }

        _zkMasterKeys[owner.Identity.DomainName] =
            (await ZkOwnerSteps.MakeZeroKnowledgeAsync(owner, OwnerLogin.DefaultPassword)).ZkMasterKey;
    }

    /// <summary>
    /// A password login after a friend deal: it seals the password to the friends' public key.
    /// <paramref name="copy"/> is what gets sealed; anyone with the password can seal junk instead.
    /// </summary>
    protected static async Task WriteFriendsPasswordCopyAsync(OwnerSession dealer, string password, string? copy = null)
    {
        var status = await ZkOwnerSteps.GetStatusAsync(dealer);
        Assert.That(status.ShamirPublicKeyJwk, Is.Not.Null, "a friend deal publishes its public key");

        var response = await ZkOwnerSteps.SavePasswordAsync(dealer, password, phraseCopy: null,
            friendsCopy: ZkBrowser.SealPassword(copy ?? password, status.ShamirPublicKeyJwk!));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
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

    /// <summary>
    /// For the owner endpoints exempt from shared-secret encryption (<c>recovery-info</c>,
    /// <c>needs-attention</c>): they answer in plain JSON that <see cref="SecurityOf"/>'s decrypting
    /// serializer cannot read.
    /// </summary>
    protected static ITestSecurityContextOwnerClient PlainSecurityOf(OwnerSession owner)
        => RestService.For<ITestSecurityContextOwnerClient>(owner.NewAdminHttpClient().client);

    /// <summary>Turns on automated recovery. Note: no connections — production has none either.</summary>
    protected static async Task<DealerShardConfig> ArrangeAutomatedShardsAsync(OwnerSession dealer)
    {
        var enableResponse = await dealer.RefitFor<IRefitOwnerConfiguration>().EnableAutoPasswordRecovery();
        Assert.That(enableResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), enableResponse.Error?.Content);
        await dealer.Sync.DrainOutboxAsync();

        var config = await GetDealerShardConfigAsync(dealer);
        Assert.That(config.UsesAutomaticRecovery, Is.True);
        return config;
    }

    /// <summary>Connection request + accept, dealer to each player. Note: no circles.</summary>
    protected static async Task PrepareConnectionsAsync(OwnerSession dealer, IEnumerable<OwnerSession> players)
    {
        // Note: no circles. An owner-to-owner handshake is reviewed on both sides, and a reviewed
        // connection holds Write on the ShardRecoveryDrive ambiently (#1809).
        foreach (var player in players)
        {
            await PeerFlow.ConnectAsync(dealer, player);
        }
    }

    /// <summary>
    /// Drains the dealer's outbox so its shard sends are delivered, then asks every player to confirm
    /// it is holding its shard.
    /// </summary>
    protected static async Task DrainAndVerifyShardsAsync(OwnerSession dealer, int expectedPlayerCount)
    {
        await dealer.Sync.DrainOutboxAsync();

        var verifyShardsResponse = await SecurityOf(dealer).VerifyShards();
        Assert.That(verifyShardsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = verifyShardsResponse.Content;
        Assert.That(results, Is.Not.Null);
        Assert.That(results!.Players, Is.Not.Null);
        Assert.That(results.Players.Count, Is.EqualTo(expectedPlayerCount),
            "mismatch number of shards in verified results");
        // Names the players that failed rather than printing "Expected: True".
        Assert.That(results.Players.Where(p => !p.Value.IsValid).Select(p => p.Key), Is.Empty,
            "one or more players not verified");
    }

    /// <summary>
    /// Requests recovery mode from a new recovering browser (<see cref="RecoveringBrowser"/>), follows
    /// the emailed verify-enter link to the recovery page, and has the page post it back with the
    /// browser's public key.
    /// </summary>
    protected async Task EnterRecoveryModeAsync(OwnerSession dealer)
    {
        var security = AnonymousSecurityOf(dealer);
        RecoveringBrowser = ZkBrowser.MakeKeyPair();

        var enterResponse = await security.InitiateRecoveryMode(new InitiateRecoveryModeRequest
        {
            RecoveryPublicKeyJwk = RecoveringBrowser.PublicKeyJwk
        });
        Assert.That(enterResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), enterResponse.Error?.Content);

        var nonceId = RecoveryLink(dealer, RecoveryNotificationKind.EnterRecoveryMode).NonceId.ToString();

        var linkResponse = await security.VerifyEnterRecoveryMode(nonceId);
        // Windows CI has answered 403 here (docs/flakytests.md); the body names the exception behind it
        Assert.That(linkResponse.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), linkResponse.Error?.Content);
        Assert.That(linkResponse.Headers.Location?.ToString(), Does.Contain($"enter={nonceId}"),
            "the link only opens the recovery page");

        var verifyEnterResponse = await security.EnterRecoveryMode(new EnterRecoveryModeRequest
        {
            Id = nonceId,
            RecoveryPublicKeyJwk = RecoveringBrowser.PublicKeyJwk
        });
        Assert.That(verifyEnterResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), verifyEnterResponse.Error?.Content);
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

        var exitRecoveryNonceId = RecoveryLink(dealer, RecoveryNotificationKind.ExitRecoveryMode).NonceId.ToString();

        var verifyExitResponse = await security.VerifyExitRecoveryMode(exitRecoveryNonceId);
        // Windows CI has answered 403 here (docs/flakytests.md); the body names the exception behind it
        Assert.That(verifyExitResponse.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), verifyExitResponse.Error?.Content);
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

    protected static async Task<RecoveryInfo> RecoveryInfoAsync(OwnerSession owner)
    {
        var response = await PlainSecurityOf(owner).GetRecoveryInfo();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        Assert.That(response.Content, Is.Not.Null);
        return response.Content!;
    }

    /// <summary>
    /// Whether the owner is told their shards predate the password: the flag behind the console's
    /// needs-attention indicator and the risk report (#1861).
    /// </summary>
    protected static async Task<bool> IsRotationPendingAsync(OwnerSession owner)
        => (await RecoveryInfoAsync(owner)).RotationPending;

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

    /// <summary>
    /// Each gated player finds the dealer's release request in its list and approves it: their browser
    /// opens the shard and seals it again to the recovering browser's key.
    /// </summary>
    protected async Task ApproveEveryShardRequestAsync(IReadOnlyList<OwnerSession> players, DealerShardConfig config)
    {
        foreach (var player in players)
        {
            var item = await GetPlayerShardRequestAsync(config, player);
            Assert.That(item, Is.Not.Null, "Release request for shard was not found");
            Assert.That(item!.RecoveryPublicKeyJwk, Is.EqualTo(RecoveringBrowser!.PublicKeyJwk),
                "the request carries the recovering browser's key");

            var approveResponse = await ZkFriends.ApproveAsync(player, ZkMasterKeyOf(player), item);
            Assert.That(approveResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), approveResponse.Error?.Content);
        }
    }

    /// <summary>
    /// The recovering browser, once enough friends approved: fetches the bundle sealed to its key and
    /// rebuilds the secret, the zero-knowledge master key and the password.
    /// </summary>
    private protected async Task<ZkFriends.Recovered> RebuildInTheRecoveringBrowserAsync(OwnerSession dealer)
    {
        await AssertRecoveryStateAsync(dealer, ShamirRecoveryState.AwaitingRecoveringBrowser);
        var bundle = await AnonymousSecurityOf(dealer).GetRecoveryBundle();
        Assert.That(bundle.StatusCode, Is.EqualTo(HttpStatusCode.OK), bundle.Error?.Content);
        return ZkFriends.Rebuild(bundle.Content!, RecoveringBrowser!);
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
    /// The newest recovery link of <paramref name="kind"/> generated for <paramref name="dealer"/>.
    /// </summary>
    protected RecoveryNotification RecoveryLink(OwnerSession dealer, RecoveryNotificationKind kind) =>
        _recoveryLinks.Latest(new OdinId(dealer.Identity.DomainName), kind);

    /// <summary>How many <paramref name="kind"/> notifications the host sent <paramref name="dealer"/> so far.</summary>
    protected int RecoveryNotificationCount(OwnerSession dealer, RecoveryNotificationKind kind) =>
        _recoveryLinks.Count(new OdinId(dealer.Identity.DomainName), kind);

    /// <summary>
    /// The automated deal's recovery: enters recovery mode (the automated players release unasked) and
    /// finalizes with <paramref name="newPassword"/>.
    /// </summary>
    protected async Task<OwnerSession> RecoverAutomatedAsync(OwnerSession dealer, DealerShardConfig config, string newPassword)
    {
        Assert.That(config.UsesAutomaticRecovery, Is.True, "a friend deal recovers in the browser");
        await EnterRecoveryModeAsync(dealer);
        return await FinalizeRecoveryAndLoginAsync(dealer, newPassword);
    }

    /// <summary>
    /// A friend deal's recovery: enters recovery mode, has <paramref name="approvers"/> approve, and
    /// rebuilds in the recovering browser.
    /// </summary>
    private protected async Task<ZkFriends.Recovered> RecoverInBrowserAsync(OwnerSession dealer,
        IReadOnlyList<OwnerSession> approvers, DealerShardConfig config)
    {
        await EnterRecoveryModeAsync(dealer);
        await ApproveEveryShardRequestAsync(approvers, config);
        return await RebuildInTheRecoveringBrowserAsync(dealer);
    }

    /// <summary>
    /// Reads the finalize nonce + final recovery key from the captured link, posts
    /// <paramref name="newPassword"/> against them, and logs in with it.
    /// </summary>
    protected async Task<OwnerSession> FinalizeRecoveryAndLoginAsync(OwnerSession dealer, string newPassword)
    {
        // this is a dumb test but I just wanted to be clear about success criterion (i.e. an explicit assert)
        await AssertRecoveryStateAsync(dealer, ShamirRecoveryState.AwaitingOwnerFinalization);

        var finalizeLink = RecoveryLink(dealer, RecoveryNotificationKind.FinalizeRecovery);
        var finalizeNonceId = finalizeLink.NonceId.ToString();
        var finalRecoveryKey = finalizeLink.FinalRecoveryKey.ToString();
        Assert.That(finalRecoveryKey, Is.Not.Empty, "the finalize link carried no recovery key");

        var saltyReply = await OwnerPasswordFlow.CalculatePasswordReplyAsync(
            Host, dealer.Identity.DomainName, newPassword);

        // here we will call finalize to get the recovery key
        var finalizeRecoveryResponse = await AnonymousSecurityOf(dealer).FinalizeRecovery(new FinalRecoveryRequest
        {
            Id = finalizeNonceId,
            FinalKey = finalRecoveryKey,
            PasswordReply = saltyReply
        });

        Assert.That(finalizeRecoveryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var recovered = await OwnerSession.LoginAsync(Host, dealer.Identity.DomainName, newPassword);
        Assert.That(recovered.Token.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(recovered.Token.AccessTokenHalfKey.IsSet(), Is.True);
        Assert.That(recovered.SharedSecret.IsSet(), Is.True);
        return recovered;
    }

    /// <summary>
    /// <c>WebScaffold.AssertHasDebugLogEvent</c>: exactly <paramref name="count"/> Debug events whose
    /// rendered message equals <paramref name="message"/>.
    /// </summary>
    protected void AssertHasDebugLogEvent(string message, int count)
    {
        var matching = Host.LogStore.GetLogEvents()[LogEventLevel.Debug]
            .Where(l => l.RenderMessage() == message)
            .ToList();

        Assert.That(matching, Has.Count.EqualTo(count), $"Debug log events matching '{message}'");
    }
}
