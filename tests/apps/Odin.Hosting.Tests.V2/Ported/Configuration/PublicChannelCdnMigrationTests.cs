#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Base;
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
        var (scope, ctx) = await MigrationContextAsync(frodo);
        var driveManager = scope.Resolve<IDriveManager>();

        var publicChannel = await CreateDriveAsync(driveManager, ctx, "pub-channel", SystemDriveConstants.ChannelDriveType, anonymous: true);
        var connectionsChannel = await CreateDriveAsync(driveManager, ctx, "conn-channel", SystemDriveConstants.ChannelDriveType, anonymous: false);
        var publicOther = await CreateDriveAsync(driveManager, ctx, "pub-other", Guid.NewGuid(), anonymous: true);

        var migration = scope.Resolve<V21ToV22VersionMigrationService>();
        Assert.ThrowsAsync<OdinSystemException>(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None),
            "validation must not pass while a public channel drive has the CDN off");

        await migration.EnableCdnOnPublicChannelDrivesAsync(ctx, CancellationToken.None);
        await migration.EnableCdnOnPublicChannelDrivesAsync(ctx, CancellationToken.None);

        Assert.That(await CdnEnabledAsync(driveManager, publicChannel), Is.True);
        Assert.That(await CdnEnabledAsync(driveManager, connectionsChannel), Is.False,
            "the CDN passes every file's ACL, so a connections-only channel must stay off it");
        Assert.That(await CdnEnabledAsync(driveManager, publicOther), Is.False, "only channel drives are in scope");
        Assert.That(await CdnEnabledAsync(driveManager, WellKnownAppDrives.PublicPostsChannelDrive.Alias), Is.True);

        Assert.DoesNotThrowAsync(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));
    }

    private static async Task<Guid> CreateDriveAsync(IDriveManager driveManager, IOdinContext ctx, string slug, Guid type,
        bool anonymous)
    {
        var drive = await driveManager.CreateDriveAsync(new CreateDriveRequest
        {
            Name = slug,
            TargetDrive = new TargetDrive { Alias = Guid.NewGuid(), Type = type },
            Metadata = "",
            AppId = SystemAppConstants.FeedAppId,
            DriveSlug = new OdinSlug(slug),
            DriveTypeSlug = new OdinSlug(type == SystemDriveConstants.ChannelDriveType ? "channel" : "other"),
            AllowAnonymousReads = anonymous,
            AllowCdn = false
        }, ctx);
        return drive.Id;
    }

    private static async Task<bool> CdnEnabledAsync(IDriveManager driveManager, Guid driveId) =>
        (await driveManager.GetDriveAsync(driveId))!.IsCdnEnabled();
}
