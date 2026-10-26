#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.OwnerApi.Authentication;
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
    /// the phrase, its check value and its reset key for the bytes 0..15.
    /// </summary>
    [Test]
    public void TheSharedVectorsMatchTheBrowsers()
    {
        var phraseKey = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        Assert.That(BIP39Util.GenerateBIP39(phraseKey),
            Is.EqualTo("abandon amount liar amount expire adjust cage candy arch gather drum buyer"));
        Assert.That(RecoveryPhraseCheck.Compute(phraseKey), Is.EqualTo("Eo89wt2+AtmPabdl1v7jP8jYb9oHQlRxCiqqpMave6Y="));
        Assert.That(ZkBrowser.ResetKey(phraseKey).ToBase64(), Is.EqualTo("UObib1b+8ABBLhC7sBHNOw=="));
    }

    /// <summary>
    /// A value sealed by WebCrypto (node 22) in odin-js's seal v1 (<see cref="ZkSeal"/>), as the browser
    /// seals the password; odin-js checks it too (js-lib ZeroKnowledgeKeys.test.ts).  Its key id is the
    /// CRC-32 of the recipient's raw public key, which both sides compute.
    /// </summary>
    [Test]
    public void TheSharedSealVectorOpens()
    {
        var privateKey = SealVectorPrivateKey.FromBase64();
        Assert.That(ZkBrowser.Unseal(SealVector, privateKey).ToStringFromUtf8Bytes(), Is.EqualTo("correct horse battery staple"));
        Assert.That(ZkSeal.ReadKeyId(SealVector.FromBase64()), Is.EqualTo(0x81ac6ed6u));
        Assert.That(ZkSeal.KeyId(SealVectorPublicKeyJwk), Is.EqualTo(0x81ac6ed6u));
        Assert.That(CRC32C.CalculateCRC32C(0, "123456789".ToUtf8ByteArray()), Is.EqualTo(0xcbf43926u),
            "the IEEE CRC-32 check value, which the browser's CRC-32 also gives");

        var pair = ZkBrowser.MakeKeyPair();
        var sealedValue = ZkBrowser.Seal("x".ToUtf8ByteArray(), pair.PublicKeyJwk);
        Assert.That(ZkBrowser.Unseal(sealedValue, pair.PrivateKeyPkcs8), Is.EqualTo("x".ToUtf8ByteArray()));
        Assert.That(ZkSeal.ReadKeyId(sealedValue.FromBase64()), Is.EqualTo(ZkSeal.KeyId(pair.PublicKeyJwk)));
    }

    private const string SealVectorPrivateKey =
        "MIG2AgEAMBAGByqGSM49AgEGBSuBBAAiBIGeMIGbAgEBBDAdIBNZdX4cc2HkbmyjAsYLgBCJOD4PKl+19MNegdIFHBNvBHXjKKPd1U43jVFC4W2hZANiAAQh7s8LrEkJbWspBmjbFT45ZclVZc9go3WGP86OB1nf16QB+2o3yNTYCg0YJ4Atpknyf0xxJGe5a/LTuILGNwlUaPDmFSyzZ4ALW18WutsGr8ItoW2/V6/fK5tpF8iT70k=";

    private const string SealVectorPublicKeyJwk =
        "{\"kty\":\"EC\",\"crv\":\"P-384\",\"x\":\"Ie7PC6xJCW1rKQZo2xU-OWXJVWXPYKN1hj_OjgdZ39ekAftqN8jU2AoNGCeALaZJ\",\"y\":\"8n9McSRnuWvy07iCxjcJVGjw5hUss2eAC1tfFrrbBq_CLaFtv1ev3yubaRfIk-9J\"}";

    private const string SealVector =
        "AYGsbtYEJzx9/APLDFRU02ky+GZwV8b3H7z+kwGsq61keI1Spr1TwEe4xemX1gXqt4ebsODaFTM9CTX1ZzYz8AEOKmwRfyCxFiAYyUY9XK31wB7oShI/GiQX6RjUGj2KekD+/f9GXM+hXaDl9UqGteiAsqESPMoIVY4iVeGO3GC/P2Dy4Igq2MsVPmZeNSxogKR0ZeC+rPTMjyeOkbM=";

    /// <summary>
    /// Shards the browser's Shamir split made (js-lib ZeroKnowledgeFriends.test.ts): any three rebuild
    /// the secret here, and the friends' reset key matches the browser's.
    /// </summary>
    [Test]
    public void TheBrowsersShamirSharesRebuildHere()
    {
        var shards = new[]
        {
            "tLcD3b72iAEbAEkldwD+PuuopzSp1BQDX/9X3Ef9zxU=",
            "ATsKhfMHdLF0wxhy2gRQvc0yWeqH5EDII3fKUy3sF6vo",
            "AZL6hj/Zenxa+Eh9HafvPqvUEsf2q0EWWT9X5+ngQIhp",
            "GfJJ2eHW9SMJyXO/8Hpf1eQk0ohw3UK6x77PsKTh2l0=",
            "FRtGlcbsAu5ZKT8hwLZjVTvs5C5RBMUb7tGXQTkotjo="
        }.Select((s, i) => new ShamirSecretSharing.ShamirShard(i + 1, s.FromBase64())).ToList();

        foreach (var three in new[] { new[] { 0, 1, 2 }, new[] { 1, 3, 4 }, new[] { 0, 2, 4 } })
        {
            var secret = ShamirSecretSharing.ReconstructShamirSecret(three.Select(i => shards[i]).ToList(), 16);
            Assert.That(secret.ToBase64(), Is.EqualTo("//79/Pv6+fj39vX08/Lx8A=="), string.Join(",", three));
        }

        Assert.That(ZkFriends.ResetKey(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray()).ToBase64(),
            Is.EqualTo("AgfFEly7RprtGEQcjgk2SQ=="));
    }

    [Test]
    public async Task ThePhraseGivesBackThePasswordOnceTakenOver()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);

        Assert.That((await ZkOwnerSteps.GetStatusAsync(owner)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.OnServer));
        Assert.That((await RecoveryClient().GetPhraseRecovery()).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "before the takeover the page falls back to the old reset");

        var phrase = await ZkOwnerSteps.TakeOverPhraseAsync(owner, Password, keys.MasterKey);

        Assert.That((await ZkOwnerSteps.GetStatusAsync(owner)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.TakenOver));
        var recovery = await RecoveryClient().GetPhraseRecovery();
        Assert.That(recovery.StatusCode, Is.EqualTo(HttpStatusCode.OK), recovery.Error?.Content);
        Assert.That(ZkBrowser.RecoverPassword(phrase, recovery.Content!), Is.EqualTo(Password));
    }

    [Test]
    public async Task TheHandoverAndThePasswordCopyNeedThePasswordItself()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);

        // A session alone, without the password, gets neither.
        var handover = await ZkOwnerSteps.HandOverPhraseAsync(owner, "not the password");
        Assert.That(handover.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), handover.Error?.Content);

        var phrase = await ZkOwnerSteps.TakeOverPhraseAsync(owner, Password, keys.MasterKey);
        var status = await ZkOwnerSteps.GetStatusAsync(owner);
        Assert.That(status.HasPasswordCopy, Is.True, "the takeover sealed it");

        var overwrite = await ZkOwnerSteps.SavePasswordAsync(owner, "not the password",
            ZkBrowser.SealPassword("not the password", status.PhrasePublicKeyJwk));
        Assert.That(overwrite.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), overwrite.Error?.Content);
        Assert.That(ZkBrowser.RecoverPassword(phrase, (await RecoveryClient().GetPhraseRecovery()).Content!), Is.EqualTo(Password),
            "the refused write changed nothing");

        var written = await ZkOwnerSteps.SavePasswordAsync(owner, Password, ZkBrowser.SealPassword(Password, status.PhrasePublicKeyJwk));
        Assert.That(written.StatusCode, Is.EqualTo(HttpStatusCode.OK), written.Error?.Content);
    }

    /// <summary>
    /// The password copy is written before the takeover only by the takeover itself: until then there
    /// is no phrase key to seal it to.
    /// </summary>
    [Test]
    public async Task ThePasswordCopyNeedsThePhraseKey()
    {
        var owner = await LoginAsOwner();
        await ZkOwnerSteps.CreateKeysAsync(owner, Password);

        var status = await ZkOwnerSteps.GetStatusAsync(owner);
        Assert.That(status.PhrasePublicKeyJwk, Is.Null);
        Assert.That(status.HasPasswordCopy, Is.False);

        var write = await ZkOwnerSteps.SavePasswordAsync(owner, Password,
            ZkBrowser.SealPassword(Password, ZkBrowser.MakeKeyPair().PublicKeyJwk));
        DriveAsserts.AssertRejected(write, OdinClientErrorCode.RecoveryPhraseNotTakenOver);
    }

    [Test]
    public async Task TheTakeoverRefusesCopiesMadeWithAnotherPhrase()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);

        var otherPhrase = BIP39Util.GenerateBIP39(ByteArrayUtil.GetRndByteArray(16));
        var response = await ZkOwnerSteps.Client(owner).TakeOverPhrase(
            await ZkOwnerSteps.MakeTakeoverAsync(owner, Password, otherPhrase, keys.MasterKey));

        DriveAsserts.AssertRejected(response, OdinClientErrorCode.RecoveryPhraseMismatch);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(owner)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.OnServer));
    }

    /// <summary>The browser only makes and seals with P-384 keys; any other key would break every later seal.</summary>
    [Test]
    public async Task TheTakeoverRefusesAnythingButAP384PublicKey()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);
        var phrase = (await ZkOwnerSteps.HandOverPhraseAsync(owner, Password)).Content!.Phrase;

        var request = await ZkOwnerSteps.MakeTakeoverAsync(owner, Password, phrase, keys.MasterKey);
        using var p256 = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var q = p256.ExportParameters(false).Q;
        request.PhrasePublicKeyJwk = System.Text.Json.JsonSerializer.Serialize(new
        {
            kty = "EC", crv = "P-256", x = Base64UrlEncoder.Encode(q.X!), y = Base64UrlEncoder.Encode(q.Y!)
        });

        var response = await ZkOwnerSteps.Client(owner).TakeOverPhrase(request);
        DriveAsserts.AssertRejected(response, OdinClientErrorCode.ArgumentError);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(owner)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.OnServer));
    }

    [Test]
    public async Task AMalformedResetIsRefusedWithoutAServerError()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);
        await ZkOwnerSteps.TakeOverPhraseAsync(owner, Password, keys.MasterKey);

        var reset = await RecoveryClient().ResetPasswordWithResetKey(new ZkResetRequest
        {
            EncryptedResetKey = new Odin.Services.EncryptionKeyService.EccEncryptedPayload(),
            PasswordReply = await OwnerPasswordFlow.CalculatePasswordReplyAsync(Host, PrimaryIdentity, NewPassword),
            ZkKeys = ZkBrowser.MakeKeys(NewPassword).Request
        });
        DriveAsserts.AssertRejected(reset, OdinClientErrorCode.ArgumentError);
    }

    [Test]
    public async Task TheTakeoverNeedsThePasswordItself()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);
        var phrase = (await ZkOwnerSteps.HandOverPhraseAsync(owner, Password)).Content!.Phrase;

        // A session that has the phrase (an owner who never viewed it gets it at once) but not the password.
        var response = await ZkOwnerSteps.Client(owner).TakeOverPhrase(
            await ZkOwnerSteps.MakeTakeoverAsync(owner, "not the password", phrase, keys.MasterKey));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), response.Error?.Content);
        Assert.That((await ZkOwnerSteps.GetStatusAsync(owner)).RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.OnServer));
    }

    /// <summary>
    /// Someone with the password can seal junk as the password copy, so it may give back a password
    /// that doesn't work.  The reset key, which only the phrase gives, still resets the password,
    /// brings back the same zero-knowledge master key, and ends every owner session.
    /// </summary>
    [Test]
    public async Task TheResetKeyResetsThePasswordWhenTheCopyWasRewritten()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);
        var phrase = await ZkOwnerSteps.TakeOverPhraseAsync(owner, Password, keys.MasterKey);

        var publicKey = (await ZkOwnerSteps.GetStatusAsync(owner)).PhrasePublicKeyJwk;
        var rewritten = await ZkOwnerSteps.SavePasswordAsync(owner, Password, ZkBrowser.SealPassword("junk", publicKey));
        Assert.That(rewritten.StatusCode, Is.EqualTo(HttpStatusCode.OK), rewritten.Error?.Content);

        var recovery = (await RecoveryClient().GetPhraseRecovery()).Content!;
        Assert.That(ZkBrowser.RecoverPassword(phrase, recovery), Is.EqualTo("junk"), "the copy no longer logs in");
        var zkMasterKey = ZkBrowser.RecoverZkMasterKey(phrase, recovery);

        var reset = await OwnerPasswordFlow.ResetPasswordWithResetKeyAsync(Host, PrimaryIdentity,
            ZkBrowser.ResetKey(ZkBrowser.PhraseKey(phrase)), NewPassword,
            ZkBrowser.MakeKeys(NewPassword, zkMasterKey, recovery.PhrasePublicKeyJwk).Request);
        Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.OK), reset.Error?.Content);

        var stale = await ZkOwnerSteps.Client(owner).GetStatus();
        Assert.That(stale.IsSuccessStatusCode, Is.False, $"the session from before the reset still works: {stale.StatusCode}");

        var recovered = await OwnerSession.LoginAsync(Host, PrimaryIdentity, NewPassword);
        var status = await ZkOwnerSteps.GetStatusAsync(recovered);
        Assert.That(ZkBrowser.OpenMasterKey(NewPassword, status), Is.EqualTo(keys.MasterKey), "the same zero-knowledge master key");
        Assert.That(status.RecoveryPhrase, Is.EqualTo(RecoveryPhraseState.TakenOver), "the phrase still recovers");
        Assert.That(ZkBrowser.RecoverPassword(phrase, (await RecoveryClient().GetPhraseRecovery()).Content!), Is.EqualTo(NewPassword));
    }

    /// <summary>
    /// The attack the reset key used to allow: someone who once had the password opens the
    /// zero-knowledge master key, which a password change keeps.  With it they can neither reset
    /// the password nor read the password copy.  Only the phrase does either.
    /// </summary>
    [Test]
    public async Task TheZkMasterKeyAloneNeitherResetsNorReadsThePassword()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);
        await ZkOwnerSteps.TakeOverPhraseAsync(owner, Password, keys.MasterKey);

        var recovery = (await RecoveryClient().GetPhraseRecovery()).Content!;
        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => ZkBrowser.Open(recovery.PhraseKeyEncryptedPhrasePrivateKey, keys.MasterKey),
            "the zero-knowledge master key opens the phrase's private key");

        foreach (var notThePhrase in new[] { keys.MasterKey, ByteArrayUtil.GetRndByteArray(16) })
        {
            var reset = await OwnerPasswordFlow.ResetPasswordWithResetKeyAsync(Host, PrimaryIdentity,
                ZkBrowser.ResetKey(notThePhrase), NewPassword, ZkBrowser.MakeKeys(NewPassword).Request);
            Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), reset.Error?.Content);
        }

        var login = await OwnerSession.LoginAsync(Host, PrimaryIdentity, Password);
        Assert.That(login.Token.Id, Is.Not.EqualTo(System.Guid.Empty), "the password is as it was");
    }

    [Test]
    public async Task AfterTheTakeoverTheServerHasNoCopyOfThePhrase()
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);
        var phrase = await ZkOwnerSteps.TakeOverPhraseAsync(owner, Password, keys.MasterKey);

        DriveAsserts.AssertRejected(await ZkOwnerSteps.HandOverPhraseAsync(owner, Password), OdinClientErrorCode.RecoveryPhraseTakenOver);

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
        var phrase = await ZkOwnerSteps.TakeOverPhraseAsync(owner, Password, keys.MasterKey);

        var publicKey = (await ZkOwnerSteps.GetStatusAsync(owner)).PhrasePublicKeyJwk;
        var request = await OwnerPasswordFlow.BuildResetPasswordRequestAsync(Host, PrimaryIdentity, Password, NewPassword);
        request.ZkKeys = ZkBrowser.MakeKeys(NewPassword, keys.MasterKey, publicKey).Request;
        var response = await owner.RefitFor<ITestSecurityContextOwnerClient>().ResetPassword(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);

        var recovery = await RecoveryClient().GetPhraseRecovery();
        Assert.That(recovery.StatusCode, Is.EqualTo(HttpStatusCode.OK), recovery.Error?.Content);
        Assert.That(ZkBrowser.RecoverPassword(phrase, recovery.Content!), Is.EqualTo(NewPassword));
    }

    /// <summary>Before the takeover the server works out the expected check value from its own copy.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task TheHealthCheckComparesOnlyTheCheckValue(bool takenOver)
    {
        var owner = await LoginAsOwner();
        var keys = await ZkOwnerSteps.CreateKeysAsync(owner, Password);
        var phrase = takenOver
            ? await ZkOwnerSteps.TakeOverPhraseAsync(owner, Password, keys.MasterKey)
            : (await ZkOwnerSteps.HandOverPhraseAsync(owner, Password)).Content!.Phrase;

        var health = owner.RefitFor<ITestSecurityContextOwnerClient>();

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

    [Test]
    public async Task AfterLogoutTheSessionKeyIsGone()
    {
        var owner = await LoginAsOwner();
        await SessionKey(owner);

        var expire = await RecoveryClient().Expire(owner.Token.Id);
        Assert.That(expire.StatusCode, Is.EqualTo(HttpStatusCode.OK), expire.Error?.Content);

        var response = await ZkOwnerSteps.Client(owner).GetSessionKey();
        Assert.That(response.IsSuccessStatusCode, Is.False, $"actual {response.StatusCode}");
    }

    private static async Task<string> SessionKey(OwnerSession owner)
    {
        var response = await ZkOwnerSteps.Client(owner).GetSessionKey();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return response.Content!.SessionKey64;
    }

    private IOwnerAuthenticationClient RecoveryClient() =>
        Host.AnonymousRefitFor<IOwnerAuthenticationClient>(PrimaryIdentity);
}
