#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version17tov18;
using Odin.Services.Membership.Connections;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// Covers the v17 -&gt; v18 backfill: the pass that moves an existing address book out of the two system
/// circles and into the per-app built-in circles that replace them.
/// </summary>
/// <remarks>
/// Provisioning creates Chat, Moments and Recovery but never puts anybody in them, so what these pin is
/// who each pass decides belongs where, and that it decides it from what the identity records rather
/// than from the circles being retired.  The three answers differ on purpose -- a review for Moments,
/// merely being connected for Chat, actually holding a shard for Recovery -- and getting any of them
/// wrong either strands a contact without capability they had, or hands out capability nobody granted.
/// </remarks>
[TestFixture]
public class CircleBackfillMigrationTests : V2Fixture
{
    protected override string[] HostIdentities =>
        [Identities.Frodo, Identities.Sam, Identities.Merry, Identities.Pippin, Identities.TomBombadil];

    [Test]
    public async Task AReviewedContactLandsInMoments()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await ConnectAsync(frodo, sam);

        // Accepting a request is the review, so sam arrives already stamped.
        Assert.That((await GetIcrAsync(frodo, sam.Identity)).ReviewedAt, Is.Not.Null);

        var enrolled = await RunMomentsAsync(frodo);

        Assert.That(enrolled, Is.EqualTo(1));
        Assert.That(await HoldsAsync(frodo, sam.Identity, BuiltinCircles.MomentsCircle.Id), Is.True);
    }

    [Test]
    public async Task AnUnreviewedContactStaysOutOfMoments()
    {
        // Moments is GrantOn.Review, so the review is what qualifies. Backfilling an unreviewed contact
        // would be asserting a decision the owner never made -- and membership of a personal circle is
        // what ClearReviewAsync's guard assumes implies one.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await ConnectAsync(frodo, sam);
        await ClearReviewAsync(frodo, sam.Identity);

        var enrolled = await RunMomentsAsync(frodo);

        Assert.That(enrolled, Is.EqualTo(0));
        Assert.That(await HoldsAsync(frodo, sam.Identity, BuiltinCircles.MomentsCircle.Id), Is.False);
    }

    [Test]
    public async Task AnUnreviewedContactStillLandsInChat()
    {
        // Chat is GrantOn.Connect: connecting is the qualifying act and this contact has performed it.
        // They could chat before the migration and must still be able to after, reviewed or not.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await ConnectAsync(frodo, sam);
        await ClearReviewAsync(frodo, sam.Identity);

        var enrolled = await RunChatAsync(frodo);

        Assert.That(enrolled, Is.EqualTo(1));
        Assert.That(await HoldsAsync(frodo, sam.Identity, BuiltinCircles.ChatCircle.Id), Is.True);
    }

    [Test]
    public async Task RunningTheWholeBackfillTwiceChangesNothing()
    {
        // Additive and re-runnable is what makes a partial run safe to repeat: an operator who has to
        // run this again after a failure must not risk re-minting grants over the ones that landed.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);
        await ConnectAsync(frodo, sam);
        await ConnectAsync(frodo, merry);

        var (scope, ctx) = await MigrationContextAsync(frodo);
        var migration = scope.Resolve<V17ToV18VersionMigrationService>();

        await migration.UpgradeAsync(ctx, CancellationToken.None);
        var afterFirst = await GrantedCircleIdsAsync(frodo, sam.Identity);

        var momentsAgain = await migration.EnrollReviewedContactsInMomentsAsync(ctx, CancellationToken.None);
        var chatAgain = await migration.EnrollConnectedContactsInChatAsync(ctx, CancellationToken.None);
        var recoveryAgain = await migration.EnrollShardHoldersInRecoveryAsync(ctx, CancellationToken.None);

        Assert.That(momentsAgain, Is.EqualTo(0), "a second run has nobody left to add to Moments");
        Assert.That(chatAgain, Is.EqualTo(0), "a second run has nobody left to add to Chat");
        Assert.That(recoveryAgain, Is.EqualTo(0), "a second run has nobody left to add to Recovery");

        Assert.That(await GrantedCircleIdsAsync(frodo, sam.Identity), Is.EquivalentTo(afterFirst),
            "the second run changed what the first one left");

        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);
    }

    [Test]
    public async Task TheBackfillLeavesTheSystemCirclesAlone()
    {
        // Retiring the two bundles is a separate act. Taking capability away here, on the strength of a
        // backfill nobody has watched run, is the one thing these passes must not do.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await ConnectAsync(frodo, sam);

        var before = await GrantedCircleIdsAsync(frodo, sam.Identity);

        var (scope, ctx) = await MigrationContextAsync(frodo);
        await scope.Resolve<V17ToV18VersionMigrationService>().UpgradeAsync(ctx, CancellationToken.None);

        var after = await GrantedCircleIdsAsync(frodo, sam.Identity);
        Assert.That(after, Is.SupersetOf(before), "nothing sam already held may be taken away");
    }

    [Test]
    public async Task WithNoShardConfigurationNobodyLandsInRecovery()
    {
        // The narrowing, stated as plainly as it can be: everybody here is connected and reviewed, which
        // under the old Confirmed Connections bundle was enough to be handed write on the shard drive.
        // Holding a shard is now the only thing that is.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await ConnectAsync(frodo, sam);

        var enrolled = await RunRecoveryAsync(frodo);

        Assert.That(enrolled, Is.EqualTo(0));
        Assert.That(await HoldsAsync(frodo, sam.Identity, BuiltinCircles.RecoveryCircle.Id), Is.False);
    }

    [Test]
    public async Task OnlyActualShardHoldersLandInRecovery()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);
        var pippin = await LoginAsOwner(Identities.Pippin);
        var tom = await LoginAsOwner(Identities.TomBombadil);

        foreach (var peer in new[] { sam, merry, pippin, tom })
        {
            await ConnectAsync(frodo, peer);
        }

        // Tom is connected and reviewed exactly like the others and is handed no shard, so he is the
        // one who proves the pass reads the dealer's roster rather than the connection list.
        var players = new[] { sam, merry, pippin }.Select(p => p.Identity).ToList();
        await ConfigureShardsAsync(frodo, players);

        var enrolled = await RunRecoveryAsync(frodo);

        Assert.That(enrolled, Is.EqualTo(players.Count));
        foreach (var player in players)
        {
            Assert.That(await HoldsAsync(frodo, player, BuiltinCircles.RecoveryCircle.Id), Is.True,
                $"{player} holds a shard and should be in the Recovery circle");
        }

        Assert.That(await HoldsAsync(frodo, tom.Identity, BuiltinCircles.RecoveryCircle.Id), Is.False,
            "a reviewed contact who holds no shard gets no shard-drive grant");
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<int> RunMomentsAsync(OwnerSession owner)
    {
        var (scope, ctx) = await MigrationContextAsync(owner);
        return await scope.Resolve<V17ToV18VersionMigrationService>()
            .EnrollReviewedContactsInMomentsAsync(ctx, CancellationToken.None);
    }

    private async Task<int> RunChatAsync(OwnerSession owner)
    {
        var (scope, ctx) = await MigrationContextAsync(owner);
        return await scope.Resolve<V17ToV18VersionMigrationService>()
            .EnrollConnectedContactsInChatAsync(ctx, CancellationToken.None);
    }

    private async Task<int> RunRecoveryAsync(OwnerSession owner)
    {
        var (scope, ctx) = await MigrationContextAsync(owner);
        return await scope.Resolve<V17ToV18VersionMigrationService>()
            .EnrollShardHoldersInRecoveryAsync(ctx, CancellationToken.None);
    }

    private async Task ConfigureShardsAsync(OwnerSession dealer, IEnumerable<OdinId> players)
    {
        // Straight at the service rather than over HTTP: what the pass reads is the dealer's package,
        // and this is the one call that writes it. Distribution to the players goes through the outbox
        // and is not what is under test -- the package is saved either way.
        var (scope, ctx) = await MigrationContextAsync(dealer);
        var shamir = scope.Resolve<ShamirConfigurationService>();

        var list = players.Select(p => new ShamiraPlayer { OdinId = p, Type = PlayerType.Delegate }).ToList();
        await shamir.ConfigureShards(list, ShamirConfigurationService.MinimumPlayerCount, ctx);
    }

    private async Task<(ILifetimeScope scope, IOdinContext ctx)> MigrationContextAsync(OwnerSession owner)
    {
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        return (scope, await BuildOwnerContextAsync(scope, owner));
    }

    private static async Task ConnectAsync(OwnerSession owner, OwnerSession peer)
    {
        var send = await owner.Connections.SendConnectionRequest(peer.Identity);
        Assert.That(send.IsSuccessStatusCode, Is.True, $"send to {peer.Identity} failed: {send.StatusCode}");

        var accept = await peer.Connections.AcceptConnectionRequest(owner.Identity);
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"accept on {peer.Identity} failed: {accept.StatusCode}");

        var icr = await owner.Connections.GetConnectionInfo(peer.Identity);
        Assert.That(icr.Content!.Status, Is.EqualTo(ConnectionStatus.Connected),
            $"{owner.Identity} is {icr.Content.Status} with {peer.Identity}");
    }

    private static async Task ClearReviewAsync(OwnerSession owner, OdinId target)
    {
        var cleared = await new V2ConnectionNetworkClient(owner.Identity, owner.Factory).ClearReviewAsync(target);
        Assert.That(cleared.IsSuccessStatusCode, Is.True, $"clear review failed: {cleared.StatusCode}");
    }

    private async Task<bool> HoldsAsync(OwnerSession owner, OdinId target, System.Guid circleId)
    {
        return (await GrantedCircleIdsAsync(owner, target)).Contains(circleId);
    }

    private async Task<List<System.Guid>> GrantedCircleIdsAsync(OwnerSession owner, OdinId target)
    {
        return (await GetIcrAsync(owner, target)).PeerKeyStore.CircleGrants.Keys.ToList();
    }

    private async Task<IdentityConnectionRegistration> GetIcrAsync(OwnerSession owner, OdinId target)
    {
        var icr = await Host.GetTenantScope(owner.Identity.DomainName)
            .Resolve<CircleNetworkStorage>().GetAsync(target);
        Assert.That(icr, Is.Not.Null, $"no connection record for {target}");
        return icr!;
    }

    /// <summary>
    /// An owner context carrying the master key, built the way <c>VersionUpgradeService</c> builds one,
    /// so a phase can be replayed by calling the service directly.
    /// </summary>
    private static async Task<IOdinContext> BuildOwnerContextAsync(ILifetimeScope scope, OwnerSession owner)
    {
        var authService = scope.Resolve<OwnerAuthenticationService>();
        var odinContext = new OdinContext { Tenant = default, AuthTokenCreated = null, Caller = null };
        var clientContext = new OdinClientContext
        {
            CorsHostName = null,
            AccessRegistrationId = null,
            DevicePushNotificationKey = null,
            ClientIdOrDomain = null
        };

        await authService.UpdateOdinContextAsync(owner.Token, clientContext, odinContext);
        odinContext.Caller!.AssertHasMasterKey();
        return odinContext;
    }
}
