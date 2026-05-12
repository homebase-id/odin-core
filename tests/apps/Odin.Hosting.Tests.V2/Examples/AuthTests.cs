using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
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

