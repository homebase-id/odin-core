#nullable enable
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.OwnerApi.Authentication;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Services.Base;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Authentication;

/// <summary>
/// Port of <c>OwnerApi/Authentication/ResetPasswordTests</c>. Changing the owner password with the
/// current one: the new password logs in, the old one stops working, a wrong "current password" is
/// refused, and a cookie issued before the change is no longer honoured by <c>verifyToken</c>.
/// </summary>
/// <remarks>
/// <para>
/// Deviations, all checked:
/// <list type="bullet">
/// <item>The originals set their own password through <c>OldOwnerApi.SetupOwnerAccount</c>; here the
/// fixture's login owns it, so the "current password" is <see cref="OwnerLogin.DefaultPassword"/>.
/// The new password is carried verbatim.</item>
/// <item>Each original pinned an identity (TomBombadil / Pippin / Merry) because a password change is
/// one-way within a shared <c>WebScaffold</c> run. Per-test reset removes that, so all three run as
/// the fixture default.</item>
/// <item><c>VerifyTokenReturnsFalseForStaleCookieAfterPasswordReset</c> hand-rolled an
/// <see cref="HttpClientHandler"/> with <c>ServerCertificateCustomValidationCallback</c>,
/// <c>UseCookies</c> and its own <c>CookieContainer</c>. All of that existed only to reach the
/// loopback TLS listener and keep one cookie across calls. There is no TLS here, so the browser is
/// <c>Host.CreateAnonymousClient()</c> carrying the issued cookie on its default headers — the same
/// single-cookie jar, minus the transport plumbing.</item>
/// <item>The tenant <c>OdinContextCache</c> reset reaches the container through
/// <c>Host.GetTenantScope</c> rather than <c>_scaffold.Services.GetRequiredService&lt;IMultiTenantContainer&gt;()</c>.
/// Same object, one hop shorter.</item>
/// </list>
/// </para>
/// <para>No caller matrix in the original and none added.</para>
/// </remarks>
[TestFixture]
public class ResetPasswordTests : V2Fixture
{
    private const string Password = OwnerLogin.DefaultPassword;
    private const string NewPassword = "672c~!!9402044";

    [Test]
    public async Task CanResetPasswordUsingCurrentPassword()
    {
        var owner = await LoginAsOwner();
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

        //Ensure we can login using the first password
        var firstLoginResponse = await OwnerPasswordFlow.LoginAsync(Host, PrimaryIdentity, Password, clientEccFullKey);
        Assert.That(firstLoginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var resetPasswordResponse = await owner.RefitFor<ITestSecurityContextOwnerClient>().ResetPassword(
            await OwnerPasswordFlow.BuildResetPasswordRequestAsync(Host, PrimaryIdentity, Password, NewPassword));
        Assert.That(resetPasswordResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "failed resetting password to newPassword with key");

        //login with the password
        var secondLogin = await OwnerPasswordFlow.LoginAsync(Host, PrimaryIdentity, NewPassword, clientEccFullKey);
        Assert.That(secondLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        //fail to login with the old password
        var thirdLogin = await OwnerPasswordFlow.LoginAsync(Host, PrimaryIdentity, Password, clientEccFullKey);
        Assert.That(thirdLogin.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "Should have failed to login with old password");

        // Additional tests
        // Test that I can access data in drives as owner; this shows the master key is the same
        // Test can i send a file over transit as owner; this shows the master key is still good for the Icr Encryption key
    }

    // Regression: a stale-KEK cookie (valid Id but the registration's KEK no longer
    // unwraps the rewritten PasswordData.KekEncryptedMasterKey) used to pass
    // verifyToken because the endpoint only checked Id presence. That made the owner
    // login page redirect-bounce into /api/owner/v1/youauth/authorize forever, since
    // useValidateAuthorization on the client only forces logout when verifyToken
    // returns false. See youauth-redirect-loop.md.
    [Test]
    public async Task VerifyTokenReturnsFalseForStaleCookieAfterPasswordReset()
    {
        // Keep our own cookie jar so the cookie survives the later password-reset call.
        using var browserClient = Host.CreateAnonymousClient(PrimaryIdentity);

        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var reply = await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(
            browserClient, Password, clientEccFullKey);
        var authSvc = RestService.For<IOwnerAuthenticationClient>(browserClient);
        var loginResponse = await authSvc.Authenticate(reply);
        Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Initial login failed");

        var setCookie = loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(Odin.Services.Authentication.Owner.OwnerAuthConstants.CookieName + "="))
            : null;
        Assert.That(setCookie, Is.Not.Null, "Authenticate did not issue an owner cookie");
        browserClient.DefaultRequestHeaders.Add("Cookie", setCookie!.Split(';')[0]);

        // Fresh cookie: verifyToken must accept it.
        await AssertVerifyToken(browserClient, "true", "verifyToken should accept a fresh cookie");

        // Rewrite PasswordData via a separate owner session. The registration that
        // browserClient is holding now has TokenEncryptedKek wrapping the old KEK
        // while PasswordData.KekEncryptedMasterKey is wrapped with the new KEK.
        var owner = await LoginAsOwner();
        var resetResponse = await owner.RefitFor<ITestSecurityContextOwnerClient>().ResetPassword(
            await OwnerPasswordFlow.BuildResetPasswordRequestAsync(Host, PrimaryIdentity, Password, NewPassword));
        Assert.That(resetResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "ResetPassword failed");

        // OdinContextCache caches the pre-reset context against the cookie's
        // composite key. In production it eventually expires (or the server
        // restarts) and the loop only manifests after that. Reset it explicitly
        // here so the test exercises the same post-expiry decrypt path.
        await Host.GetTenantScope(PrimaryIdentity).Resolve<OdinContextCache>().ResetAsync();

        // Stale cookie: verifyToken must reject it.
        await AssertVerifyToken(browserClient, "false",
            "verifyToken must reject a stale-KEK cookie; otherwise the login page redirect-loops");
    }

    /// <summary>
    /// <c>verifyToken</c> answers 200 with a bare <c>true</c>/<c>false</c> body either way — the
    /// verdict is the body, not the status — so both halves of the stale-cookie regression assert the
    /// same two things about the same call.
    /// </summary>
    private static async Task AssertVerifyToken(HttpClient browserClient, string expectedBody, string because)
    {
        using var resp = await browserClient.GetAsync("/api/owner/v1/authentication/verifyToken");
        Assert.That(resp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.That(body, Is.EqualTo(expectedBody), because);
    }

    [Test]
    public async Task FailToResetPasswordUsingInvalidOldPassword()
    {
        const string invalidOldPassword = Password + "382";

        var owner = await LoginAsOwner();
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

        //Ensure we can login using the first password
        var firstLoginResponse = await OwnerPasswordFlow.LoginAsync(Host, PrimaryIdentity, Password, clientEccFullKey);
        Assert.That(firstLoginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var resetPasswordResponse = await owner.RefitFor<ITestSecurityContextOwnerClient>().ResetPassword(
            await OwnerPasswordFlow.BuildResetPasswordRequestAsync(Host, PrimaryIdentity, invalidOldPassword, NewPassword));
        Assert.That(resetPasswordResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "Should have failed to reset password using invalid old password");

        //Ensure we can still login using the first password
        var secondLogin = await OwnerPasswordFlow.LoginAsync(Host, PrimaryIdentity, Password, clientEccFullKey);
        Assert.That(secondLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
