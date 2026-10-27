using System.Threading.Tasks;
using Odin.Services.Base;

namespace Odin.Services.Authentication.YouAuth;

#nullable enable

// ReSharper disable InconsistentNaming
public enum ClientType
{
    unknown,
    app,
    domain
}
// ReSharper restore InconsistentNaming

public interface IYouAuthUnifiedService
{
    Task<bool> AppNeedsRegistration(string clientIdOrDomain, string permissionRequest, IOdinContext odinContext);

    Task<bool> NeedConsent(
        string tenant,
        ClientType clientType,
        string clientIdOrDomain,
        string permissionRequest,
        string redirectUri,
        IOdinContext odinContext,
        string? zkAppKeyPublicKey = null);

    /// <summary>
    /// Keeps, for the sign-in it belongs to, the app's zero-knowledge key the owner's browser sealed at consent to the
    /// sign-in's public key (docs/zero-knowledge-drive-api-plan.md, step 3).  The server can't open it.
    /// </summary>
    Task StoreSealedZkAppKeyAsync(string clientId, string jwkbase64UrlPublicKey, string sealedZkAppKey64, IOdinContext odinContext);

    Task StoreConsentAsync(string clientIdOrDomain, ClientType clientType, string permissionRequest, ConsentRequirements consentRequirements,
        IOdinContext odinContext);

    Task<(string exchangePublicKey, string exchangeSalt)> CreateClientAccessTokenAsync(
        ClientType clientType,
        string clientId,
        string clientInfo,
        string permissionRequest,
        string jwkbase64UrlPublicKey,
        YouAuthCipher cipher,
        IOdinContext odinContext,
        bool wantsZkAppKey = false);

    Task<EncryptedTokenExchange?> ExchangeDigestForEncryptedToken(string exchangeSharedSecretDigest);
}

public sealed class EncryptedTokenExchange
{
    public byte[] SharedSecretCipher { get; set; }
    public byte[] SharedSecretIv { get; set; }
    public byte[] ClientAuthTokenCipher { get; set; }
    public byte[] ClientAuthTokenIv { get; set; }

    /// <summary>What sealed the two ciphers; an entry written before the field existed reads back as CBC.</summary>
    public YouAuthCipher Cipher { get; set; }

    /// <summary>
    /// The app's zero-knowledge key, sealed by the owner's browser to the sign-in's public key (<c>ZkSeal</c>, base64);
    /// null unless the app asked for it (docs/zero-knowledge-drive-api-plan.md, 3.5).
    /// </summary>
    public string? ZkAppKey { get; set; }

    public EncryptedTokenExchange(
        byte[] sharedSecretCipher,
        byte[] sharedSecretIv,
        byte[] clientAuthTokenCipher,
        byte[] clientAuthTokenIv,
        YouAuthCipher cipher)
    {
        SharedSecretCipher = sharedSecretCipher;
        SharedSecretIv = sharedSecretIv;
        ClientAuthTokenCipher = clientAuthTokenCipher;
        ClientAuthTokenIv = clientAuthTokenIv;
        Cipher = cipher;
    }
}