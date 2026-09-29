#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Describes how to build a V2 caller (Owner / App / Guest) for a parameterized test, including
/// the <see cref="DriveSpec"/> the fixture should create before constructing the caller — usually
/// because the caller needs permissions to that drive (App / Guest) or because the test just needs
/// a drive to operate on.
/// </summary>
public sealed class CallerSpec
{
    private readonly string _name;
    public DriveSpec DriveSpec { get; }
    public Func<OwnerSession, Task<IV2Caller>> Build { get; }

    /// <summary>Convenience accessor — most test bodies just want the TargetDrive itself.</summary>
    public TargetDrive TargetDrive => DriveSpec.Drive;

    private CallerSpec(string name, DriveSpec driveSpec, Func<OwnerSession, Task<IV2Caller>> build)
    {
        _name = name;
        DriveSpec = driveSpec;
        Build = build;
    }

    public override string ToString() => _name;

    public static CallerSpec Owner(DriveSpec drive) =>
        new("Owner", drive, o => Task.FromResult<IV2Caller>(o));

    /// <summary>
    /// App caller, optionally holding tenant-wide permission keys — needed wherever the endpoint
    /// gates on a <see cref="Odin.Services.Authorization.Permissions.PermissionKeys"/> value rather
    /// than on the drive grant (PublishStaticContent, SendPushNotifications). Omit the keys for the
    /// negative case: an app with the drive grant but no key must still be refused.
    /// </summary>
    public static CallerSpec App(DriveSpec drive, DrivePermission perm, IReadOnlyList<int>? permissionKeys = null) =>
        new($"App[{perm}{(permissionKeys is { Count: > 0 } k ? $"+keys:{string.Join('|', k)}" : "")}]",
            drive,
            async o => await AppSession.SetupAsync(o, drive.Drive, perm, permissionKeys));

    public static CallerSpec Guest(DriveSpec drive, DrivePermission perm) =>
        new($"Guest[{perm}]", drive, async o => await GuestSession.SetupAsync(o, drive.Drive, perm));

    /// <summary>
    /// The app <c>OwnerApiTestUtils.SetupTestSampleApp</c> registered, which several ported
    /// <c>AppAPI</c> fixtures arrange with: a drive with anonymous reads off, <see cref="DrivePermission.All"/>
    /// on it, and the transit-write key.
    /// </summary>
    public static CallerSpec SampleApp(string? label = null) =>
        App(SampleAppDrive(label), DrivePermission.All, [PermissionKeys.UseTransitWrite]);

    /// <summary>
    /// As <see cref="SampleApp"/> plus the connection-read keys the app in
    /// <c>AppApiTestUtils.CreateAppAndUploadFileMetadata</c> held.
    /// </summary>
    public static CallerSpec SampleAppWithConnectionReads(string? label = null) =>
        App(SampleAppDrive(label), DrivePermission.All,
            [PermissionKeys.ReadConnections, PermissionKeys.ReadConnectionRequests, PermissionKeys.UseTransitWrite]);

    /// <summary>
    /// As <see cref="SampleApp"/> but holding every permission key — the shape fixtures whose original
    /// registered its app by hand with <c>new PermissionSet(PermissionKeys.All)</c> need. Kept distinct
    /// from <see cref="SampleApp"/> rather than folded into it: narrowing those apps to a single key
    /// would change what the arrange grants.
    /// </summary>
    public static CallerSpec SampleAppWithAllKeys(string? label = null) =>
        App(SampleAppDrive(label), DrivePermission.All, PermissionKeys.All);

    private static DriveSpec SampleAppDrive(string? label) =>
        label is null
            ? new DriveSpec(TargetDrive.NewTargetDrive(), AllowAnonymousReads: false)
            : new DriveSpec(TargetDrive.NewTargetDrive(), label, AllowAnonymousReads: false);
}
