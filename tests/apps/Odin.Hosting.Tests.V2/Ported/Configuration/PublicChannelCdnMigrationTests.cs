#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Configuration.VersionUpgrade.Version21tov22;
using Odin.Services.Drives;

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
}
