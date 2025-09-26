using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Cache;
using Odin.Core.Util;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.YouAuth;
using Odin.Services.Util;

namespace Odin.Services.Authentication.YouAuth;

#nullable enable

public sealed class YouAuthUnifiedService(
    IAppRegistrationService appRegistrationService,
    YouAuthDomainRegistrationService domainRegistrationService,
    CircleNetworkService circleNetwork,
    ITenantLevel2Cache<YouAuthUnifiedService> level2Cache)
    : IYouAuthUnifiedService
{
    //

    public Task<bool> AppNeedsRegistration(string clientIdOrDomain, string permissionRequest)
    {
        throw new NotImplementedException();
    }

    public async Task<bool> NeedConsent(
        string tenant,
        ClientType clientType,
        string clientIdOrDomain,
        string permissionRequest,
        string redirectUri,
        IOdinContext odinContext)
    {
        await AssertCanAcquireConsent(clientType, clientIdOrDomain, permissionRequest, odinContext);

        //TODO: need to talk with Seb about the redirecting loop issue here
        var tempConsent = await level2Cache.GetOrDefaultAsync(TempConsentCacheKey(clientIdOrDomain), false);
        if (tempConsent)
        {
            await level2Cache.RemoveAsync(TempConsentCacheKey(clientIdOrDomain));
            return false;
        }

        if (clientType == ClientType.domain)
        {
            return await domainRegistrationService.IsConsentRequiredAsync(new AsciiDomainName(clientIdOrDomain), odinContext);
        }

        // Apps on /owner doesn't need consent
        if (clientType == ClientType.app)
        {
            var uri = new Uri(redirectUri);
            if (uri.Host == tenant)
            {
                return false;
            }
        }

        // everything else always require consent
        return true;
    }

    //

    public async Task StoreConsentAsync(string clientIdOrDomain, ClientType clientType, string permissionRequest, ConsentRequirements consentRequirements,
        IOdinContext odinContext)
    {
        if (clientType == ClientType.app)
        {
            //so for now i'll just use this dictionary
            await level2Cache.SetAsync(TempConsentCacheKey(clientIdOrDomain), true, TimeSpan.FromMinutes(60));
        }

        if (clientType == ClientType.domain)
        {
            var domain = new AsciiDomainName(clientIdOrDomain);

            var existingDomain = await domainRegistrationService.GetRegistration(domain, odinContext);
            if (null == existingDomain)
            {
                var request = new YouAuthDomainRegistrationRequest()
                {
                    Domain = domain.DomainName,
                    Name = domain.DomainName,
                    CorsHostName = clientIdOrDomain,
                    CircleIds = default, //TODO: should we set a circle here?
                    ConsentRequirements = consentRequirements
                };

                await domainRegistrationService.RegisterDomainAsync(request, odinContext);
            }
            else
            {
                await domainRegistrationService.UpdateConsentRequirements(domain, consentRequirements, odinContext);
            }

            //so for now i'll just use this dictionary
            await level2Cache.SetAsync(TempConsentCacheKey(clientIdOrDomain), true, TimeSpan.FromMinutes(60));
        }
    }

    //

    public async Task<(string exchangePublicKey, string exchangeSalt)> CreateClientAccessTokenAsync(
        ClientType clientType,
        string clientId,
        string clientInfo,
        string permissionRequest,
        string jwkbase64UrlPublicKey,
        IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        ClientAccessToken? token;
        if (clientType == ClientType.app)
        {
            Guid appId = Guid.Parse(clientId);

            OdinValidationUtils.AssertIsTrue(appId != Guid.Empty, "AppId is invalid");
            OdinValidationUtils.AssertNotNullOrEmpty(clientInfo, nameof(clientInfo));

            //TODO: Need to check if the app is registered, if not need redirect to get consent.
            (token, _) = await appRegistrationService.RegisterClientAsync(appId, clientInfo, odinContext);
        }
        else if (clientType == ClientType.domain)
        {
            var domain = new AsciiDomainName(clientId);

            var info = await circleNetwork.GetIcrAsync((OdinId)domain, odinContext);
            if (info.IsConnected())
            {
                var icrKey = odinContext.PermissionsContext.GetIcrKey();
                token = info.CreateClientAccessToken(icrKey);
            }
            else
            {
                var request = new YouAuthDomainRegistrationRequest()
                {
                    Domain = domain.DomainName,
                    Name = domain.DomainName,
                    CorsHostName = clientId,
                    CircleIds = default //TODO: should we set a circle here?
                };

                (token, _) = await domainRegistrationService.RegisterClientAsync(domain, domain.DomainName, request, odinContext);
            }
        }
        else
        {
            throw new OdinSystemException($"Invalid clientType '{clientType}'");
        }

        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);
        var exchangeSalt = ByteArrayUtil.GetRndByteArray(16);

        var remotePublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(jwkbase64UrlPublicKey);
        var exchangeSharedSecret = keyPair.GetEcdhSharedSecret(privateKey, remotePublicKey, exchangeSalt);
        var exchangeSharedSecretDigest = SHA256.Create().ComputeHash(exchangeSharedSecret.GetKey()).ToBase64();

        var sharedSecretPlain = token.SharedSecret.GetKey();
        var (sharedSecretIv, sharedSecretCipher) = AesCbc.Encrypt(sharedSecretPlain, exchangeSharedSecret);

        var clientAuthTokenPlain = token.ToAuthenticationToken().ToPortableBytes();
        var (clientAuthTokenIv, clientAuthTokenCipher) = AesCbc.Encrypt(clientAuthTokenPlain, exchangeSharedSecret);

        var encryptedTokenExchange = new EncryptedTokenExchange(
            sharedSecretCipher,
            sharedSecretIv,
            clientAuthTokenCipher,
            clientAuthTokenIv);

        await level2Cache.SetAsync(EncryptedTokenCacheKey(exchangeSharedSecretDigest), encryptedTokenExchange, TimeSpan.FromMinutes(5));

        return (keyPair.PublicKeyJwkBase64Url(), Convert.ToBase64String(exchangeSalt));
    }

    //

    public async Task<EncryptedTokenExchange?> ExchangeDigestForEncryptedToken(string exchangeSharedSecretDigest)
    {
        var ec = await level2Cache.TryGetAsync<EncryptedTokenExchange?>(EncryptedTokenCacheKey(exchangeSharedSecretDigest));

        if (ec.HasValue)
        {
            await level2Cache.RemoveAsync(EncryptedTokenCacheKey(exchangeSharedSecretDigest));
        }

        return ec.GetValueOrDefault();
    }

    //

    public async Task<bool> AppNeedsRegistration(string clientIdOrDomain, string permissionRequest, IOdinContext odinContext)
    {
        if (!Guid.TryParse(clientIdOrDomain, out var appId))
        {
            throw new OdinClientException("App id must be a uuid", OdinClientErrorCode.ArgumentError);
        }

        var appReg = await appRegistrationService.GetAppRegistration(appId, odinContext);
        if (appReg == null)
        {
            return true;
        }

        if (appReg.IsRevoked)
        {
            throw new OdinClientException("App is revoked", OdinClientErrorCode.AppRevoked);
        }

        return false;
    }

    //

    private async Task AssertCanAcquireConsent(ClientType clientType, string clientIdOrDomain, string permissionRequest, IOdinContext odinContext)
    {
        if (clientType == ClientType.app)
        {
            if (await AppNeedsRegistration(clientIdOrDomain, permissionRequest, odinContext))
            {
                throw new OdinSystemException("App must be registered before consent check is possible");
            }
        }
    }

    //

    private string TempConsentCacheKey(string clientIdOrDomain) => $"TempConsent:{clientIdOrDomain}";
    private string EncryptedTokenCacheKey(string exchangeSharedSecretDigest) =>
        $"EncryptedToken:{exchangeSharedSecretDigest}";
}
//