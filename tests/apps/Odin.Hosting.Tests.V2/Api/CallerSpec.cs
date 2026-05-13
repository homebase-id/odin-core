using System;
using System.Threading.Tasks;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Describes how to build a V2 caller (Owner / App / Guest) for a parameterized test. The
/// <see cref="TargetDrive"/> is the drive that should exist on the host before the caller is built —
/// usually because the caller needs permissions to it (App / Guest) or because the test just needs
/// a drive to operate on.
/// </summary>
public sealed class CallerSpec
{
    private readonly string _name;
    public TargetDrive TargetDrive { get; }
    public Func<OwnerSession, Task<IV2Caller>> Build { get; }

    private CallerSpec(string name, TargetDrive drive, Func<OwnerSession, Task<IV2Caller>> build)
    {
        _name = name;
        TargetDrive = drive;
        Build = build;
    }

    public override string ToString() => _name;

    public static CallerSpec Owner(TargetDrive drive) =>
        new("Owner", drive, o => Task.FromResult<IV2Caller>(o));

    public static CallerSpec App(TargetDrive drive, DrivePermission perm) =>
        new($"App[{perm}]", drive, async o => await AppSession.SetupAsync(o, drive, perm));

    public static CallerSpec Guest(TargetDrive drive, DrivePermission perm) =>
        new($"Guest[{perm}]", drive, async o => await GuestSession.SetupAsync(o, drive, perm));
}
