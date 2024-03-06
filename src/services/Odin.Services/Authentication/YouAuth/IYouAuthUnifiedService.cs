using System;
using System.Threading.Tasks;
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
    Task<bool> AppNeedsRegistration(string clientIdOrDomain, string permissionRequest);

    Task<bool> NeedConsent(
        string tenant,
        ClientType clientType,
        string clientIdOrDomain,
        string permissionRequest,
        string redirectUri);

    Task StoreConsent(string clientIdOrDomain, ClientType clientType, string permissionRequest, ConsentRequirements consentRequirements);

    Task<(string exchangePublicKey, string exchangeSalt)> CreateClientAccessToken(
        ClientType clientType,
        string clientId,
        string clientInfo,
        string permissionRequest,
        string publicKey);

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