using System;
using System.Threading.Tasks;
using Odin.Services.Authorization.ExchangeGrants;
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

    public static CallerSpec App(DriveSpec drive, DrivePermission perm) =>
        new($"App[{perm}]", drive, async o => await AppSession.SetupAsync(o, drive.Drive, perm));

    public static CallerSpec Guest(DriveSpec drive, DrivePermission perm) =>
        new($"Guest[{perm}]", drive, async o => await GuestSession.SetupAsync(o, drive.Drive, perm));
}
