using System;
using System.Threading.Tasks;
using Odin.Core.Storage.SQLite;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Membership.YouAuth;
using Odin.Services.Base;
using Odin.Core.Storage.SQLite.IdentityDatabase;

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
    Task<bool> AppNeedsRegistration(string clientIdOrDomain, string permissionRequest, IOdinContext odinContext, IdentityDatabase db);

    Task<bool> NeedConsent(
        string tenant,
        ClientType clientType,
        string clientIdOrDomain,
        string permissionRequest,
        string redirectUri,
        IOdinContext odinContext,
        IdentityDatabase db);

    Task StoreConsent(string clientIdOrDomain, ClientType clientType, string permissionRequest, ConsentRequirements consentRequirements,
        IOdinContext odinContext, IdentityDatabase db);

    Task<(string exchangePublicKey, string exchangeSalt)> CreateClientAccessToken(
        ClientType clientType,
        string clientId,
        string clientInfo,
        string permissionRequest,
        string publicKey,
        IOdinContext odinContext,
        IdentityDatabase db);

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