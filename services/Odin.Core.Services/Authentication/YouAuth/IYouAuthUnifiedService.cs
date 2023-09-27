using System;
using System.Threading.Tasks;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Membership.YouAuth;

namespace Odin.Core.Services.Authentication.YouAuth;

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
    Task<bool> AppNeedsRegistration(ClientType clientType, string clientIdOrDomain, string permissionRequest);

    Task<bool> NeedConsent(
        string tenant, 
        ClientType clientType, 
        string clientIdOrDomain, 
        string permissionRequest);
    
    Task StoreConsent(string clientIdOrDomain, string permissionRequest, ConsentRequirements consentRequirement);
    
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

