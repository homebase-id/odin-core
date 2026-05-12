using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Examples;

/// <summary>
/// Port of <c>_V2/Tests/Auth/AuthTests.CanVerifyTokenAndLogout</c> — full Owner / App / Guest
/// matrix. Exercises the V2 auth pipeline (verify-token, logout, verify-token-after-logout) and
/// confirms each caller-type setup path (owner login, app registration + ECC exchange, YouAuth
/// domain + client registration) works end-to-end over the in-process router.
/// </summary>
[TestFixture]
public class AuthTests : V2Fixture
{
    public static IEnumerable<object[]> CallerVariants()
    {
        yield return [CallerSpec.Owner(TargetDrive.NewTargetDrive()), HttpStatusCode.Unauthorized];
        yield return [CallerSpec.App(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.Unauthorized];
        yield return [CallerSpec.Guest(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(CallerVariants))]
    public async Task CanVerifyTokenAndLogout(CallerSpec spec, HttpStatusCode expectedAfterLogout)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        await owner.Admin.CreateDrive(spec.TargetDrive, "Test Drive 001");
        var caller = await spec.Build(owner, Host);

        Assert.That((await caller.Auth.VerifyToken()).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await caller.Auth.Logout()).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await caller.Auth.VerifyToken()).StatusCode, Is.EqualTo(expectedAfterLogout));
    }
}

/// <summary>
/// Describes how to build a V2 caller for a parameterized test. The <see cref="TargetDrive"/> is
/// the drive that should exist on the host before the caller is built — usually because the caller
/// needs permissions to it (App / Guest) or because the test just needs a drive to operate on.
/// </summary>
public sealed class CallerSpec
{
    private readonly string _name;
    public TargetDrive TargetDrive { get; }
    public Func<OwnerSession, OdinHost, Task<IV2Caller>> Build { get; }

    private CallerSpec(string name, TargetDrive drive, Func<OwnerSession, OdinHost, Task<IV2Caller>> build)
    {
        _name = name;
        TargetDrive = drive;
        Build = build;
    }

    public override string ToString() => _name;

    public static CallerSpec Owner(TargetDrive drive) =>
        new("Owner", drive, (o, _) => Task.FromResult<IV2Caller>(o));

    public static CallerSpec App(TargetDrive drive, DrivePermission perm) =>
        new($"App[{perm}]", drive, async (o, h) => await AppSession.SetupAsync(o, h, drive, perm));

    public static CallerSpec Guest(TargetDrive drive, DrivePermission perm) =>
        new($"Guest[{perm}]", drive, async (o, h) => await GuestSession.SetupAsync(o, h, drive, perm));
}
