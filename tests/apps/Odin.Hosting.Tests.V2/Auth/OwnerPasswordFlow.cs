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
using Odin.Services.Security.ZeroKnowledge;
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

    /// <summary>
    /// <see cref="CalculatePasswordReplyAsync(HttpClient,string,EccFullKeyData)"/> for a caller that
    /// holds only the host: opens the anonymous client and mints the throwaway client ECC key itself,
    /// exactly as <see cref="BuildResetPasswordRequestAsync"/> and
    /// <see cref="ResetPasswordUsingRecoveryKeyAsync"/> already do internally.
    /// </summary>
    public static async Task<PasswordReply> CalculatePasswordReplyAsync(
        OdinHost host, string identity, string password)
    {
        using var authClient = host.CreateAnonymousClient(identity);
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        return await CalculatePasswordReplyAsync(authClient, password, clientEccFullKey);
    }

    /// <summary>
    /// Proof of <paramref name="password"/> against a fresh authentication nonce, as an endpoint that
    /// wants the password itself (a password change, the phrase handover) takes it.
    /// </summary>
    public static async Task<PasswordReply> CalculateAuthenticationPasswordReplyAsync(
        OdinHost host, string identity, string password)
    {
        using var authClient = host.CreateAnonymousClient(identity);
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        return await CalculateAuthenticationPasswordReplyAsync(authClient, password, clientEccFullKey);
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
        using var authClient = host.CreateAnonymousClient(identity);
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var saltyReply = await CalculatePasswordReplyAsync(authClient, newPassword, clientEccFullKey);
        var encryptedRecoveryKey = await EncryptForHostAsync(authClient, recoveryKey, saltyReply, clientEccFullKey);
        var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

        var resetRequest = new ResetPasswordUsingRecoveryKeyRequest
        {
            EncryptedRecoveryKey = encryptedRecoveryKey,
            PasswordReply = saltyReply // The sensitive parts here are GCM encrypted.
        };

        return await svc.ResetPasswordUsingRecoveryKey(resetRequest);
    }

    /// <summary>
    /// Recovery with the phrase's reset key, as the forgot-password page sends it: the key encrypted
    /// to the host, a new password, and <paramref name="zkKeys"/>.
    /// </summary>
    public static async Task<ApiResponse<HttpContent>> ResetPasswordWithPhraseAsync(
        OdinHost host, string identity, byte[] resetKey, string newPassword, ZkKeysRequest zkKeys)
    {
        using var authClient = host.CreateAnonymousClient(identity);
        var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var saltyReply = await CalculatePasswordReplyAsync(authClient, newPassword, clientEccFullKey);

        return await RestService.For<IOwnerAuthenticationClient>(authClient).ResetPasswordWithPhrase(new ZkPhraseResetRequest
        {
            EncryptedResetKey = await EncryptForHostAsync(authClient, resetKey.ToBase64(), saltyReply, clientEccFullKey),
            PasswordReply = saltyReply,
            ZkKeys = zkKeys
        });
    }

    /// <summary>
    /// <paramref name="text"/> encrypted to the host's offline key, salted with <paramref name="reply"/>'s
    /// nonce, as the browser sends the recovery phrase and the reset key.
    /// </summary>
    private static async Task<EccEncryptedPayload> EncryptForHostAsync(HttpClient authClient, string text,
        PasswordReply reply, EccFullKeyData clientEccFullKey)
    {
        const PublicPrivateKeyType keyType = PublicPrivateKeyType.OfflineKey;
        var svc = RestService.For<IOwnerAuthenticationClient>(authClient);
        var publicKeyResponse = await svc.GetPublicKeyEcc(keyType);
        Assert.That(publicKeyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var publicKey = publicKeyResponse.Content!;

        var hostPublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(publicKey.PublicKeyJwkBase64Url);
        hostPublicKey.crc32c = publicKey.CRC32c;
        hostPublicKey.expiration = new UnixTimeUtc(publicKey.Expiration);

        var salt = reply.Nonce64.FromBase64();
        var transferSharedSecret = clientEccFullKey.GetEcdhSharedSecret(EccKeyListManagement.zeroSensitiveKey, hostPublicKey, salt);

        return new EccEncryptedPayload
        {
            //Note: i exclude the key type here because the methods that receive
            //this must decide the encryption they expect
            RemotePublicKeyJwk = clientEccFullKey.PublicKeyJwk(),
            Salt = salt,
            Iv = salt,
            EncryptionPublicKeyCrc32 = hostPublicKey.crc32c,
            EncryptedData = AesGcm.Encrypt(text.ToUtf8ByteArray(), transferSharedSecret, salt),
            KeyType = keyType
        };
    }
}
