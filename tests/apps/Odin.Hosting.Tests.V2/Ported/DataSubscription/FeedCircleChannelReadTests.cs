#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Configuration.VersionUpgrade.Version21tov22;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.DataSubscription;

/// <summary>
/// #1956: the Feed circle holds Read, with the storage key, on every channel drive the Feed app owns, so an encrypted
/// post on a custom channel reaches a follower in it.  A new channel drive is granted when it is made; one made before
/// by the v21 -&gt; v22 upgrade.
/// </summary>
[TestFixture]
public class FeedCircleChannelReadTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private static readonly Guid Feed = BuiltinCircles.FeedCircle.Id;

    [Test]
    public async Task AnEncryptedPostOnANewChannel_ReachesAFollowerInTheFeedCircle()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await DataSubscriptionScenario.ConnectAsync(frodo, sam);

        var (scope, ctx) = await MigrationContextAsync(frodo);
        var network = scope.Resolve<CircleNetworkService>();
        await network.EnrollInCircleAsync(Feed, sam.Identity, ctx);

        // Made after Sam joined: his grant is re-minted with the new drive's key.
        var channel = await CreateFeedChannelDriveAsync(frodo);
        Assert.That(await network.CanDecryptDriveAsync(sam.Identity, channel.Alias), Is.True,
            "a Feed member should hold keyed Read on a new channel drive");

        await DataSubscriptionScenario.FollowAsync(sam, frodo);
        var (uploadResult, encryptedJsonContent64) = await DataSubscriptionScenario.UploadStandardEncryptedFileToChannelAsync(
            frodo, channel, "on a custom channel", 11960, Guid.NewGuid());

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(sam,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(uploadResult), encryptedJsonContent64, uploadResult);
    }

    [Test]
    public async Task AChannelDriveAnotherAppOwns_IsNotGranted()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (scope, _) = await MigrationContextAsync(frodo);

        // The owner console's: the Feed circle grants only drives the Feed app owns.
        var channel = await DataSubscriptionScenario.CreateChannelDriveAsync(frodo);

        Assert.That(await GrantedDrivesAsync(scope), Does.Not.Contain(channel));
    }

    [Test]
    public async Task TheUpgrade_GrantsAChannelDriveMadeBefore_WithTheKey_AndOnlyOnce()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await DataSubscriptionScenario.ConnectAsync(frodo, sam);

        var (scope, ctx) = await MigrationContextAsync(frodo);
        var network = scope.Resolve<CircleNetworkService>();
        var channel = await CreateFeedChannelDriveAsync(frodo);

        // Stands in for a channel drive made before v22: the Feed circle without it, and Sam's grant minted from that.
        var definitions = scope.Resolve<CircleDefinitionService>();
        var def = await definitions.GetCircleAsync(Feed);
        def!.DriveGrants = def.DriveGrants.Where(g => g.PermissionedDrive.Drive != channel).ToList();
        await definitions.UpdateAsync(def);
        await network.EnrollInCircleAsync(Feed, sam.Identity, ctx);
        Assert.That(await network.CanDecryptDriveAsync(sam.Identity, channel.Alias), Is.False, "precondition");

        var migration = scope.Resolve<V21ToV22VersionMigrationService>();
        Assert.ThrowsAsync<OdinSystemException>(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));

        await migration.GrantFeedCircleReadOnChannelDrivesAsync(ctx, CancellationToken.None);
        await migration.GrantFeedCircleReadOnChannelDrivesAsync(ctx, CancellationToken.None);

        Assert.That((await GrantedDrivesAsync(scope)).Count(d => d == channel), Is.EqualTo(1));
        Assert.That(await GrantedDrivesAsync(scope), Does.Contain(WellKnownAppDrives.FeedDrive), "the rest is kept");
        Assert.That(await network.CanDecryptDriveAsync(sam.Identity, channel.Alias), Is.True,
            "Sam's grant should have been re-minted with the channel drive's key");
        Assert.DoesNotThrowAsync(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));
    }

    private static Task<TargetDrive> CreateFeedChannelDriveAsync(OwnerSession owner) =>
        DataSubscriptionScenario.CreateChannelDriveAsync(owner, allowAnonymousReads: true, appId: SystemAppConstants.FeedAppId);

    private static async Task<List<TargetDrive>> GrantedDrivesAsync(ILifetimeScope scope)
    {
        var def = await scope.Resolve<CircleDefinitionService>().GetCircleAsync(Feed);
        return def!.DriveGrants.Select(g => g.PermissionedDrive.Drive).ToList();
    }
}
