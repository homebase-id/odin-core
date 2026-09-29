using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Time;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;

namespace Odin.Hosting.Tests.V2.Ported.Authentication;

/// <summary>
/// Port of <c>OwnerApi/Authentication/AccountRecoveryTests</c> (class
/// <c>AccountRecoverySectionTests</c>). The recovery-phrase lifecycle: request it, wait out the
/// viewing window, read it once, and use it to reset the password.
/// </summary>
/// <remarks>
/// <para>
/// Four of the five tests carry <c>#if !DEBUG [Ignore]</c> verbatim. The waiting period is
/// <c>TimeSpan.FromDays(14)</c> unless <c>DEBUG</c> is defined, where
/// <c>PasswordKeyRecoveryService.GetWaitingPeriod</c> drops it to
/// <see cref="PasswordKeyRecoveryService.RecoveryKeyWaitingPeriodSecondsForTesting"/> — so a Release
/// run cannot exercise them at all. That is a compile-time condition on the product, not on the test
/// framework, so it moves unchanged.
/// </para>
/// <para>
/// <b>This fixture sleeps, and it is not the framework's fault.</b> Four of the five tests wait out
/// <see cref="PasswordKeyRecoveryService.RecoveryKeyWaitingPeriodSecondsForTesting"/> — a real
/// five-second product timer before a requested recovery key becomes viewable — so the fixture costs
/// roughly twenty seconds of wall clock no matter how fast the host boots. There is nothing to
/// convert: the constant is a <c>const</c> on the service, not a setting, so a test cannot shorten
/// it, and the waiting period is the behaviour under test. Do not "optimize" the delays away.
/// </para>
/// <para>
/// Deviations, all checked:
/// <list type="bullet">
/// <item>The originals called <c>OldOwnerApi.SetupOwnerAccount</c> with their own password because
/// <c>WebScaffold</c> hands out already-provisioned identities. Here the fixture's own login sets it,
/// so every password reply is computed against <see cref="OwnerLogin.DefaultPassword"/>. The value is
/// incidental: what is under test is that the endpoint accepts a correct one.</item>
/// <item>Each original pinned a different identity (Frodo / Samwise / Pippin / TomBombadil / Merry)
/// only because the recovery key can be viewed once per <c>WebScaffold</c> run. Per-test reset removes
/// that constraint, so all five run as the fixture default.</item>
/// <item>The originals ran against an un-initialized tenant. No
/// <see cref="V2Fixture.WarmTenantBaselineAsync"/> override is needed: the recovery key is created by
/// <c>OwnerAuthenticationService</c> when the password is set, not by initial setup — verified by
/// reading <c>TenantConfigService.CreateInitialKeysAsync</c>'s call site.</item>
/// <item>The security endpoints are the system under test, so they go through
/// <see cref="OwnerSession.RefitFor{T}"/>.</item>
/// </list>
/// </para>
/// <para>No caller matrix in the original and none added.</para>
/// </remarks>
[TestFixture]
public class AccountRecoverySectionTests : V2Fixture
{
    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task CanGetAccountRecoveryKey()
    {
        await RequestAndViewRecoveryKeyAsync();

        //TODO: additional checks on the key
        // RecoveryKeyGenerator.Characters
    }

    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task CanToGetAccountRecoveryKeyWhenViewedAfterTimeWindow()
    {
        var security = await RequestAndViewRecoveryKeyAsync();

        // this should fail because we've cleared
        var response2 = await security.GetAccountRecoveryKey();
        Assert.That(response2.Content!.Key, Is.Null.Or.Empty);
    }

    /// <summary>
    /// Confirm the stored key, request it, wait out the viewing window, and read it — asserting the
    /// phrase that comes back. This is the whole of <see cref="CanGetAccountRecoveryKey"/>, and
    /// <see cref="CanToGetAccountRecoveryKeyWhenViewedAfterTimeWindow"/> is the same sequence plus a
    /// second read; the two tests were byte-identical up to that trailing assertion.
    /// </summary>
    private async Task<ITestSecurityContextOwnerClient> RequestAndViewRecoveryKeyAsync()
    {
        var owner = await LoginAsOwner();
        var security = owner.RefitFor<ITestSecurityContextOwnerClient>();

        // let us say the user already has their key from before
        await security.ConfirmStoredRecoveryKey();

        // since we just set up the account - first request the recovery key
        var requestRecoveryKeyResponse = await security.RequestRecoveryKey();

        Assert.That(requestRecoveryKeyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(requestRecoveryKeyResponse.Content, Is.Not.Null);

        await WaitUntil(requestRecoveryKeyResponse.Content!.NextViewableDate);

        var response = await security.GetAccountRecoveryKey();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var decryptedRecoveryKey = response.Content!;
        Assert.That(decryptedRecoveryKey.Created.milliseconds, Is.LessThan(UnixTimeUtc.Now().milliseconds));
        Assert.That(decryptedRecoveryKey.Key, Is.Not.Null);
        Assert.That(decryptedRecoveryKey.Key, Is.Not.Empty);
        Assert.That(decryptedRecoveryKey.Key.Split(" ").Length, Is.EqualTo(12), "there should be 12 words");

        return security;
    }

    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task FailToGetAccountRecoveryKeyWhenViewedBeforeTimeWindow()
    {
        var owner = await LoginAsOwner();
        var security = owner.RefitFor<ITestSecurityContextOwnerClient>();

        // let us say the user already has their key from before
        await security.ConfirmStoredRecoveryKey();

        // make the first request since we just set up the account
        var requestRecoveryKeyResponse = await security.RequestRecoveryKey();
        Assert.That(requestRecoveryKeyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = requestRecoveryKeyResponse.Content;
        Assert.That(result, Is.Not.Null);

        await Task.Delay(1000 * PasswordKeyRecoveryService.RecoveryKeyWaitingPeriodSecondsForTesting + 1);

        var response = await security.GetAccountRecoveryKey();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.Key, Is.Not.Null.Or.Empty, "should have the recovery key");

        // now make a second request
        var requestRecoveryKeyResponse2 = await security.RequestRecoveryKey();
        Assert.That(requestRecoveryKeyResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(requestRecoveryKeyResponse2.Content, Is.Not.Null);

        var response2 = await security.GetAccountRecoveryKey();
        Assert.That(response2.Content!.Key, Is.Null, "key should not yet be viewable");
    }

    [Test]
#if !DEBUG
    [Ignore("Ignored for release tests due to how we test recovery mode")]
#endif
    public async Task CanResetPasswordUsingAccountRecoveryKey()
    {
        const string password = OwnerLogin.DefaultPassword;
        const string newPassword = "672c~!!9402044";

        var owner = await LoginAsOwner();
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

        //Ensure we can login using the first password
        var firstLoginResponse = await OwnerPasswordFlow.LoginAsync(Host, PrimaryIdentity, password, clientEccFullKey);
        Assert.That(firstLoginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var security = owner.RefitFor<ITestSecurityContextOwnerClient>();

        // let us say the user already has their key from before
        await security.ConfirmStoredRecoveryKey();

        var requestRecoveryKeyResponse = await security.RequestRecoveryKey();
        Assert.That(requestRecoveryKeyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = requestRecoveryKeyResponse.Content;
        Assert.That(result, Is.Not.Null);

        await WaitUntil(result!.NextViewableDate);

        var response = await security.GetAccountRecoveryKey();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var decryptedRecoveryKey = response.Content!;
        Assert.That(decryptedRecoveryKey.Created.milliseconds, Is.LessThan(UnixTimeUtc.Now().milliseconds));

        var key = decryptedRecoveryKey.Key;

        var resetPasswordResponse = await OwnerPasswordFlow.ResetPasswordUsingRecoveryKeyAsync(
            Host, PrimaryIdentity, key, newPassword);
        Assert.That(resetPasswordResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"failed resetting password to newPassword with key [{key}]");

        //login with the password
        var secondLogin = await OwnerPasswordFlow.LoginAsync(Host, PrimaryIdentity, newPassword, clientEccFullKey);
        Assert.That(secondLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Additional tests
        // Test that I can access data in drives as owner; this shows the master key is the same
        // Test can i send a file over transit as owner; this shows the master key is still good for the Icr Encryption key
    }

    [Test]
    public async Task FailToResetPasswordUsingInvalidAccountRecoveryKey()
    {
        const string password = OwnerLogin.DefaultPassword;
        const string newPassword = "672c~!!9402044";

        var owner = await LoginAsOwner();
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

        //Ensure we can login using the first password
        var firstLogin = await OwnerPasswordFlow.LoginAsync(Host, PrimaryIdentity, password, clientEccFullKey);
        Assert.That(firstLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var response = await owner.RefitFor<ITestSecurityContextOwnerClient>().GetAccountRecoveryKey();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var invalidRecoveryKey = Guid.NewGuid().ToString("N");
        var resetPasswordResponse = await OwnerPasswordFlow.ResetPasswordUsingRecoveryKeyAsync(
            Host, PrimaryIdentity, invalidRecoveryKey, newPassword);
        Assert.That(resetPasswordResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            $"should have failed resetting password to newPassword with an invalid recovery key [{invalidRecoveryKey}]");

        // Fail to login with new password
        var secondLogin = await OwnerPasswordFlow.LoginAsync(Host, PrimaryIdentity, newPassword, clientEccFullKey);
        Assert.That(secondLogin.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "Should have failed to login with the new password");

        // Succeed in logging in with old password
        var thirdLogin = await OwnerPasswordFlow.LoginAsync(Host, PrimaryIdentity, password, clientEccFullKey);
        Assert.That(thirdLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Should have been able to login with old password");
    }

    /// <summary>
    /// The originals' inline "time keeps ticking" block: sleep out whatever remains of the viewing
    /// window (plus 100 ms of slack) before reading the key.
    /// </summary>
    private static async Task WaitUntil(UnixTimeUtc nextViewableDate)
    {
        var nextViewableUtc = DateTimeOffset.FromUnixTimeMilliseconds(nextViewableDate.milliseconds + 100);
        var now = DateTimeOffset.UtcNow;
        if (nextViewableUtc > now)
        {
            var delay = nextViewableUtc - now;
            await Task.Delay(delay);
        }
    }
}
