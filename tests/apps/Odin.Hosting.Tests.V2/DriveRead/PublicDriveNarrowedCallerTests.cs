#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Standard;

namespace Odin.Hosting.Tests.V2.DriveRead;

/// <summary>
/// On a public drive the read rule skips the drive's Read only for a context built with the public-drive grants
/// (<see cref="PermissionContext.ReadsPublicDrives"/>).  A narrowed context, a connected caller holding no grant on the
/// drive, is asked for it there as on any other drive, so it can't pass a Connected file's ACL on a public drive without
/// one (#2130).  The same context built with them passes to the file's ACL.  The callers built that way (anonymous,
/// guest, connected, app) read through their own routes as before, which the drive-read suites cover.
/// </summary>
[TestFixture]
public class PublicDriveNarrowedCallerTests : V2Fixture
{
    /// <remarks>
    /// The stored header's load is what a payload or thumbnail read asks before it streams (<c>GetPayloadStreamAsync</c>),
    /// so it is where a narrowed caller would have got through: building the header a client gets back asks the drive's
    /// grant on its own (<c>DriveFileUtility.CreateClientFileHeader</c>).
    /// </remarks>
    [Test]
    public async Task ANarrowedConnectedCallerWithoutTheDrivesGrantIsRefusedOnAPublicDrive()
    {
        var (file, tenant) = await SeedConnectedFileOnAPublicDriveAsync();
        await using var scope = tenant.BeginLifetimeScope();
        var storage = scope.Resolve<StandardFileSystem>().Storage;

        Assert.ThrowsAsync<OdinSecurityException>(() => storage.GetServerFileHeader(file, ConnectedContext(readsPublicDrives: false)));
    }

    [Test]
    public async Task TheSameCallerBuiltWithThePublicDriveGrantsPassesToTheFilesAcl()
    {
        var (file, tenant) = await SeedConnectedFileOnAPublicDriveAsync();
        await using var scope = tenant.BeginLifetimeScope();
        var storage = scope.Resolve<StandardFileSystem>().Storage;

        var header = await storage.GetServerFileHeader(file, ConnectedContext(readsPublicDrives: true));
        Assert.That(header?.FileMetadata.File.FileId, Is.EqualTo(file.FileId), "the file's Connected ACL lets a connected caller in");
    }

    private async Task<(InternalDriveFileId File, ILifetimeScope Tenant)> SeedConnectedFileOnAPublicDriveAsync()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var file = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "for connections", AccessControlList.Connected);
        return (new InternalDriveFileId(spec.TargetDrive.Alias, file.FileId), Host.GetTenantScope(owner.Identity.DomainName));
    }

    /// <summary>
    /// A reviewed connected caller holding Read on some other drive only: a narrowed context such as #2127's direct-read
    /// token, or, with <paramref name="readsPublicDrives"/>, one built as the public-drive callers are.
    /// </summary>
    private static OdinContext ConnectedContext(bool readsPublicDrives)
    {
        var otherDrive = TargetDrive.NewTargetDrive();
        var grant = new DriveGrant
        {
            DriveId = otherDrive.Alias,
            PermissionedDrive = new PermissionedDrive { Drive = otherDrive, Permission = DrivePermission.Read }
        };

        var context = new OdinContext
        {
            Caller = new CallerContext((OdinId)"stranger.dotyou.cloud", null!, SecurityGroupType.Connected) { IsReviewed = true }
        };
        context.SetPermissionContext(new PermissionContext(
            new Dictionary<string, PermissionGroup> { ["narrowed"] = new(new PermissionSet(), [grant], null, null) },
            ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray(),
            readsPublicDrives: readsPublicDrives));
        return context;
    }
}
