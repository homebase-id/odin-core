#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// The recovery phrase in the browser (docs/zero-knowledge-recovery-plan.md, decision 3): the
/// browser takes over the phrase the owner wrote down, the server forgets every copy it could open,
/// and the phrase then gives back the password.  <see cref="ZkBrowser"/> plays the browser.
/// </summary>
[TestFixture]
public class RecoveryPhraseTests : V2Fixture
{
    private const string Password = OwnerLogin.DefaultPassword;
    private const string NewPassword = "672c~!!9402044";

    /// <summary>
    /// The vectors odin-js checks too (js-lib ZeroKnowledgeKeys.test.ts), made with Python's hashlib:
    /// the phrase and its check value for the bytes 0..15.
    /// </summary>
    [Test]
    public void TheSharedVectorsMatchTheBrowsers()
    {
        var phraseKey = System.Linq.Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        Assert.That(Odin.Core.Cryptography.Crypto.BIP39Util.GenerateBIP39(phraseKey),
            Is.EqualTo("abandon amount liar amount expire adjust cage candy arch gather drum buyer"));
        Assert.That(RecoveryPhraseCheck.Compute(phraseKey), Is.EqualTo("Eo89wt2+AtmPabdl1v7jP8jYb9oHQlRxCiqqpMave6Y="));
    }

    [Test]
    public async Task ThePhraseGivesBackThePasswordOnceTakenOver()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);

        Assert.That((await ZkOwnerSteps.GetStatusAsync(owner)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.OnServer));
        Assert.That((await RecoveryClient().GetPhraseRecovery()).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "before the takeover the page falls back to the old reset");

        var phrase = await ZkOwnerSteps.TakeOverPhraseAsync(owner, keys.MasterKey);

        Assert.That((await ZkOwnerSteps.GetStatusAsync(owner)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.TakenOver));
        var recovery = await RecoveryClient().GetPhraseRecovery();
        Assert.That(recovery.StatusCode, Is.EqualTo(HttpStatusCode.OK), recovery.Error?.Content);
        Assert.That(ZkBrowser.RecoverPassword(phrase, recovery.Content!), Is.EqualTo(Password));
    }

    [Test]
    public async Task AfterTheTakeoverTheServerHasNoCopyOfThePhrase()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);
        var phrase = await ZkOwnerSteps.TakeOverPhraseAsync(owner, keys.MasterKey);

        DriveAsserts.AssertRejected(await ZkOwnerSteps.Client(owner).HandOverPhrase(), OdinClientErrorCode.RecoveryPhraseTakenOver);

        // Showing it again returns only the copy the browser opens.
        var reveal = await owner.RefitFor<ITestSecurityContextOwnerClient>().RequestRecoveryKey();
        Assert.That(reveal.StatusCode, Is.EqualTo(HttpStatusCode.OK), reveal.Error?.Content);
        Assert.That(reveal.Content!.Key, Is.Null);
        Assert.That(ZkBrowser.Open(reveal.Content.ZkMasterKeyEncryptedPhraseKey, keys.MasterKey),
            Is.EqualTo(ZkBrowser.PhraseKey(phrase)));

        // The old reset with the phrase needs a copy the server no longer has.
        var reset = await OwnerPasswordFlow.ResetPasswordUsingRecoveryKeyAsync(Host, PrimaryIdentity, phrase, NewPassword);
        DriveAsserts.AssertRejected(reset, OdinClientErrorCode.RecoveryPhraseTakenOver);
        var login = await OwnerSession.LoginAsync(Host, PrimaryIdentity, Password);
        Assert.That(login.Token.Id, Is.Not.EqualTo(System.Guid.Empty), "the refused reset must leave the password as it was");
    }

    [Test]
    public async Task APasswordChangeKeepsThePhraseAndUpdatesThePassword()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);
        var phrase = await ZkOwnerSteps.TakeOverPhraseAsync(owner, keys.MasterKey);

        var request = await OwnerPasswordFlow.BuildResetPasswordRequestAsync(Host, PrimaryIdentity, Password, NewPassword);
        request.ZkKeys = ZkBrowser.MakeKeys(NewPassword, keys.MasterKey).Request;
        var response = await owner.RefitFor<ITestSecurityContextOwnerClient>().ResetPassword(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);

        var recovery = await RecoveryClient().GetPhraseRecovery();
        Assert.That(recovery.StatusCode, Is.EqualTo(HttpStatusCode.OK), recovery.Error?.Content);
        Assert.That(ZkBrowser.RecoverPassword(phrase, recovery.Content!), Is.EqualTo(NewPassword));
    }

    [Test]
    public async Task ALoginWritesThePasswordCopyForKeysThatLackIt()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);

        var response = await ZkOwnerSteps.Client(owner).SavePassword(new ZkPasswordRequest
        {
            ZkMasterKeyEncryptedPassword = ZkBrowser.LockPassword("typed at this login", keys.MasterKey)
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);

        var status = await ZkOwnerSteps.GetStatusAsync(owner);
        Assert.That(ZkBrowser.OpenPassword(status.ZkMasterKeyEncryptedPassword, keys.MasterKey), Is.EqualTo("typed at this login"));
    }

    [Test]
    public async Task TheHealthCheckComparesTheCheckValueOnceTakenOver()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);
        var phrase = await ZkOwnerSteps.TakeOverPhraseAsync(owner, keys.MasterKey);
        // Not RefitFor: the middleware leaves this path's request body unencrypted, as the owner app sends it.
        var (http, _) = owner.NewAdminHttpClient();
        var health = Refit.RestService.For<IZeroKnowledgeHealthClient>(http);

        var wrong = await health.VerifyRecoveryKey(new() { PhraseCheck64 = RecoveryPhraseCheck.Compute(new byte[16]) });
        DriveAsserts.AssertRejected(wrong, OdinClientErrorCode.RecoveryPhraseMismatch);

        var right = await health.VerifyRecoveryKey(new() { PhraseCheck64 = RecoveryPhraseCheck.Compute(ZkBrowser.PhraseKey(phrase)) });
        Assert.That(right.StatusCode, Is.EqualTo(HttpStatusCode.OK), right.Error?.Content);
    }

    [Test]
    public async Task EachSessionHasItsOwnSessionKey()
    {
        var first = await LoginAsOwner();
        var second = await LoginAsOwner();

        var a = await SessionKey(first);
        Assert.That(await SessionKey(first), Is.EqualTo(a), "one session, one key");
        Assert.That(await SessionKey(second), Is.Not.EqualTo(a));
        Assert.That(a.FromBase64(), Has.Length.EqualTo(16));
    }

    private static async Task<string> SessionKey(OwnerSession owner)
    {
        var response = await ZkOwnerSteps.Client(owner).GetSessionKey();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return response.Content!.SessionKey64;
    }

    private IZeroKnowledgeRecoveryClient RecoveryClient() =>
        Host.AnonymousRefitFor<IZeroKnowledgeRecoveryClient>(PrimaryIdentity);
}
