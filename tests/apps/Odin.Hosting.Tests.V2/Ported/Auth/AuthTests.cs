using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Auth;

/// <summary>
/// Port of <c>_V2/Tests/Auth/AuthTests</c> — both cases (<c>CanVerifyTokenAndLogout</c> and
/// <c>CanVerifySharedSecretEncryption</c>) across the Owner / App / Guest matrix. Exercises the
/// V2 auth pipeline (verify-token, logout, verify-token-after-logout, shared-secret round-trip)
/// and confirms each caller-type setup path (owner login, app registration + ECC exchange,
/// YouAuth domain + client registration) works end-to-end over the in-process router.
/// </summary>
[TestFixture]
public class AuthTests : V2Fixture
{
    public static IEnumerable<object[]> CallerVariants()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.Unauthorized];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Unauthorized];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(CallerVariants))]
    public async Task CanVerifyTokenAndLogout(CallerSpec spec, HttpStatusCode expectedAfterLogout)
    {
        var caller = await SetupCaller(spec);

        Assert.That((await caller.Auth.VerifyToken()).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await caller.Auth.Logout()).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await caller.Auth.VerifyToken()).StatusCode, Is.EqualTo(expectedAfterLogout));
    }

    [Test, TestCaseSource(nameof(CallerVariants))]
    public async Task CanVerifySharedSecretEncryption(CallerSpec spec, HttpStatusCode _)
    {
        // The expectedAfterLogout column is unused here — kept on the shared CallerVariants source
        // so this case fans out across the same Owner / App / Guest matrix as the logout case.
        var caller = await SetupCaller(spec);

        const string checkValue = "bing bam bop";
        var bytes = checkValue.ToUtf8ByteArray();
        var expectedHash = SHA256.Create().ComputeHash(bytes);

        var response = await caller.Auth.VerifySharedSecretEncryption(bytes.ToBase64());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(expectedHash, response.Content!.FromBase64()), Is.True);
    }
}

