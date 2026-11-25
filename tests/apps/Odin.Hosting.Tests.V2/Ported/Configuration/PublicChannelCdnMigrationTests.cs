#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Hosting.Tests.V2.ZeroKnowledge;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Configuration.VersionUpgrade.Version21tov22;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Covers the v21 -&gt; v22 pass (#1681): the CDN may read every public channel drive, and nothing else changes.
/// </summary>
[TestFixture]
public class PublicChannelCdnMigrationTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo];

    [Test]
    public async Task APublicChannelDrive_GetsTheCdn_AndConnectionsOnlyAndOtherDrivesDoNot()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        var publicChannel = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var connectionsChannel = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var publicOther = TargetDrive.NewTargetDrive(Guid.NewGuid());
        await frodo.Admin.CreateDrive(publicChannel, "public channel", allowAnonymousReads: true);
        await frodo.Admin.CreateDrive(connectionsChannel, "connections channel", allowAnonymousReads: false);
        await frodo.Admin.CreateDrive(publicOther, "public other", allowAnonymousReads: true);

        var (scope, ctx) = await MigrationContextAsync(frodo);
        var migration = scope.Resolve<V21ToV22VersionMigrationService>();
        Assert.ThrowsAsync<OdinSystemException>(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None),
            "validation must not pass while a public channel drive has the CDN off");

        await migration.EnableCdnOnPublicChannelDrivesAsync(ctx, CancellationToken.None);
        await migration.EnableCdnOnPublicChannelDrivesAsync(ctx, CancellationToken.None);

        Assert.That((await frodo.Admin.GetDrive(publicChannel)).AllowCdn, Is.True);
        Assert.That((await frodo.Admin.GetDrive(connectionsChannel)).AllowCdn, Is.False,
            "the CDN passes every file's ACL, so a connections-only channel must stay off it");
        Assert.That((await frodo.Admin.GetDrive(publicOther)).AllowCdn, Is.False, "only channel drives are in scope");
        Assert.That((await frodo.Admin.GetDrive(WellKnownAppDrives.PublicPostsChannelDrive)).AllowCdn, Is.True);

        Assert.DoesNotThrowAsync(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));
    }

    [Test]
    public async Task AZeroKnowledgeChannelDriveWithAnonymousReadsFromBeforeTheCheck_IsSkipped_NotAnUpgradeFailure()
    {
        var (frodo, zkMasterKey) = await ZeroKnowledgeFrodoAsync();
        var drive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await frodo.Admin.CreateDrive(drive, "private channel", allowAnonymousReads: false, sharingModel: DriveSharingModel.Private,
            zkKeys: ZkBrowser.MakeDriveKeys(drive.Alias, zkMasterKey).Request);

        // What a drive set before the settings check (2026-11-05) can hold: anonymous reads on a zero-knowledge drive
        var (scope, ctx) = await MigrationContextAsync(frodo);
        var db = scope.Resolve<IdentityDatabase>();
        var record = await db.DrivesCached.GetAsync(drive.Alias);
        var details = OdinSystemSerializer.Deserialize<StorageDriveDetails>(record!.detailsJson)!;
        details.AllowAnonymousReads = true;
        record.detailsJson = OdinSystemSerializer.Serialize(details);
        await db.DrivesCached.UpsertAsync(record);

        var migration = scope.Resolve<V21ToV22VersionMigrationService>();
        Assert.DoesNotThrowAsync(() => migration.EnableCdnOnPublicChannelDrivesAsync(ctx, CancellationToken.None));
        Assert.DoesNotThrowAsync(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));
        Assert.That((await frodo.Admin.GetDrive(drive)).AllowCdn, Is.False);
    }
}
