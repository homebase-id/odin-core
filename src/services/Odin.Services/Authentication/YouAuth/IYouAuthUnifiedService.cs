using System;
using System.Threading.Tasks;
using Odin.Core.Storage.SQLite;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Membership.YouAuth;
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
        IOdinContext odinContext);

    Task StoreConsentAsync(string clientIdOrDomain, ClientType clientType, string permissionRequest, ConsentRequirements consentRequirements,
        IOdinContext odinContext);

    Task<(string exchangePublicKey, string exchangeSalt)> CreateClientAccessTokenAsync(
        ClientType clientType,
        string clientId,
        string clientInfo,
        string permissionRequest,
        string publicKey,
        IOdinContext odinContext);

    Task<EncryptedTokenExchange?> ExchangeDigestForEncryptedToken(string exchangeSharedSecretDigest);
}

public sealed class EncryptedTokenExchange
{
    public byte[] SharedSecretCipher { get; set; }
    public byte[] SharedSecretIv { get; set; }
    public byte[] ClientAuthTokenCipher { get; set; }
    public byte[] ClientAuthTokenIv { get; set; }

    public EncryptedTokenExchange(
        byte[] sharedSecretCipher,
        byte[] sharedSecretIv,
        byte[] clientAuthTokenCipher,
        byte[] clientAuthTokenIv)
    {
        SharedSecretCipher = sharedSecretCipher;
        SharedSecretIv = sharedSecretIv;
        ClientAuthTokenCipher = clientAuthTokenCipher;
        ClientAuthTokenIv = clientAuthTokenIv;
    }
}