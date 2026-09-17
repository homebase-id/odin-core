#nullable enable
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Time;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Odin.Hosting.Tests.OwnerApi.Authentication;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Authentication.Owner;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Security;
using Refit;

namespace Odin.Hosting.Tests.V2.Auth;

/// <summary>
/// The anonymous half of the owner password dance against an in-process <see cref="OdinHost"/>:
/// nonce/salt fetch, password-reply computation, login, and the ECC-wrapped recovery-key reset.
/// </summary>
/// <remarks>
/// Mirrors the pieces of <c>OwnerApiTestUtils</c> the ported authentication fixtures used
/// (<c>CalculatePasswordReply</c>, <c>CalculateAuthenticationPasswordReply</c>,
/// <c>ResetPasswordUsingRecoveryKey</c>), minus the loopback-TLS plumbing: the V1 originals each
/// hand-rolled an <see cref="HttpClientHandler"/> with a
/// <c>ServerCertificateCustomValidationCallback</c> purely to reach the Kestrel listener on
/// <c>WebScaffold.HttpsPort</c>. There is no TLS here, so
/// <see cref="AnonymousHttp.CreateAnonymousClient"/> replaces all of it.
///
/// Lives beside <see cref="OwnerLogin"/> rather than on <see cref="Api.OwnerSession"/>: every call
/// here is made <i>without</i> owner credentials, which is the point — these are the endpoints a
/// browser reaches before it holds a token, and after a password change has invalidated the one it
/// held. It is under <c>Auth/</c> rather than folder-local to a port because its consumers span
/// three folders, <see cref="OwnerLogin"/> included.
/// </remarks>
internal static class OwnerPasswordFlow
{
    /// <summary>
    /// A password reply for <i>authenticating</i> — folded into a fresh authentication nonce.
    /// </summary>
    public static async Task<PasswordReply> CalculateAuthenticationPasswordReplyAsync(
        HttpClient authClient, string password, EccFullKeyData clientEccFullKey)
    {
        var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

        var nonceResponse = await svc.GenerateAuthenticationNonce();
        Assert.That(nonceResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "server failed when getting nonce");
        var clientNonce = nonceResponse.Content!;

        var nonce = new NonceData(clientNonce.SaltPassword64, clientNonce.SaltKek64, clientNonce.PublicJwk, clientNonce.CRC)
        {
            Nonce64 = clientNonce.Nonce64
        };

        return PasswordDataManager.CalculatePasswordReply(password, nonce, clientEccFullKey);
    }

    /// <summary>
    /// A password reply for <i>setting</i> a password — folded into freshly generated salts.
    /// </summary>
    public static async Task<PasswordReply> CalculatePasswordReplyAsync(
        HttpClient authClient, string password, EccFullKeyData clientEccFullKey)
    {
        var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

        var saltResponse = await svc.GenerateNewSalts();
        Assert.That(saltResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "failed to generate new salts");
        var clientSalts = saltResponse.Content!;

        var saltyNonce = new NonceData(clientSalts.SaltPassword64, clientSalts.SaltKek64, clientSalts.PublicJwk, clientSalts.CRC)
        {
            Nonce64 = clientSalts.Nonce64
        };

        return PasswordDataManager.CalculatePasswordReply(password, saltyNonce, clientEccFullKey);
    }

    /// <summary>Logs in as owner with <paramref name="password"/>, returning the raw response.</summary>
    public static async Task<ApiResponse<OwnerAuthenticationResult>> LoginAsync(
        OdinHost host, string identity, string password, EccFullKeyData clientEccFullKey)
    {
        using var authClient = host.CreateAnonymousClient(identity);
        var svc = RestService.For<IOwnerAuthenticationClient>(authClient);
        var reply = await CalculateAuthenticationPasswordReplyAsync(authClient, password, clientEccFullKey);
        return await svc.Authenticate(reply);
    }

    /// <summary>
    /// Builds the <c>ResetPasswordRequest</c> the owner security endpoint expects: the current
    /// password proved against an authentication nonce, plus the new password over fresh salts.
    /// </summary>
    public static async Task<ResetPasswordRequest> BuildResetPasswordRequestAsync(
        OdinHost host, string identity, string currentPassword, string newPassword)
    {
        using var authClient = host.CreateAnonymousClient(identity);
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

        return new ResetPasswordRequest
        {
            CurrentAuthenticationPasswordReply =
                await CalculateAuthenticationPasswordReplyAsync(authClient, currentPassword, clientEccFullKey),
            NewPasswordReply = await CalculatePasswordReplyAsync(authClient, newPassword, clientEccFullKey)
        };
    }

    /// <summary>
    /// The recovery-key reset: wrap the recovery phrase for the host's offline ECC key and post it
    /// with a new-password reply. Mirrors <c>OwnerApiTestUtils.ResetPasswordUsingRecoveryKey</c>.
    /// </summary>
    public static async Task<ApiResponse<HttpContent>> ResetPasswordUsingRecoveryKeyAsync(
        OdinHost host, string identity, string recoveryKey, string newPassword)
    {
        const PublicPrivateKeyType keyType = PublicPrivateKeyType.OfflineKey;
        using var authClient = host.CreateAnonymousClient(identity);
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var saltyReply = await CalculatePasswordReplyAsync(authClient, newPassword, clientEccFullKey);

        var svc = RestService.For<IOwnerAuthenticationClient>(authClient);
        var publicKeyResponse = await svc.GetPublicKeyEcc(keyType);
        Assert.That(publicKeyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var publicKey = publicKeyResponse.Content!;

        var hostPublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(publicKey.PublicKeyJwkBase64Url);
        hostPublicKey.crc32c = publicKey.CRC32c;
        hostPublicKey.expiration = new UnixTimeUtc(publicKey.Expiration);

        var transferSharedSecret = clientEccFullKey.GetEcdhSharedSecret(
            EccKeyListManagement.zeroSensitiveKey, hostPublicKey, saltyReply.Nonce64.FromBase64());

        var encryptedRecoveryKey = new EccEncryptedPayload
        {
            //Note: i exclude the key type here because the methods that receive
            //this must decide the encryption they expect
            RemotePublicKeyJwk = clientEccFullKey.PublicKeyJwk(),
            Salt = saltyReply.Nonce64.FromBase64(),
            Iv = saltyReply.Nonce64.FromBase64(),
            EncryptionPublicKeyCrc32 = hostPublicKey.crc32c,
            EncryptedData = AesGcm.Encrypt(recoveryKey.ToUtf8ByteArray(), transferSharedSecret, saltyReply.Nonce64.FromBase64()),
            KeyType = keyType
        };

        var resetRequest = new ResetPasswordUsingRecoveryKeyRequest
        {
            EncryptedRecoveryKey = encryptedRecoveryKey,
            PasswordReply = saltyReply // The sensitive parts here are GCM encrypted.
        };

        return await svc.ResetPasswordUsingRecoveryKey(resetRequest);
    }
}
