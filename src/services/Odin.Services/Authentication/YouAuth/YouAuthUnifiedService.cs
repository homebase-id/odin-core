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
using Odin.Services.Configuration;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.YouAuth;
using Odin.Services.Security.ZeroKnowledge;
using Odin.Services.Util;

namespace Odin.Services.Authentication.YouAuth;

#nullable enable

public sealed class YouAuthUnifiedService(
    IAppRegistrationService appRegistrationService,
    YouAuthDomainRegistrationService domainRegistrationService,
    CircleNetworkService circleNetwork,
    ITenantLevel2Cache<YouAuthUnifiedService> level2Cache,
    OdinConfiguration configuration)
    : IYouAuthUnifiedService
{
    //

    public async Task<bool> NeedConsent(
        string tenant,
        ClientType clientType,
        string clientIdOrDomain,
        string permissionRequest,
        string redirectUri,
        IOdinContext odinContext,
        string? zkAppKeyPublicKey = null)
    {
        await AssertCanAcquireConsent(clientType, clientIdOrDomain, permissionRequest, odinContext);

        // Only the owner's browser can give an app its zero-knowledge key, at the consent page; an app that asked for
        // it goes there until it has, whatever else would let it skip consent.
        // Read as the type [070] reads: an untyped probe (ContainsAsync) on a node that doesn't hold it in memory stores
        // the distributed copy there as a JsonElement, which the typed read then can't cast
        if (zkAppKeyPublicKey != null &&
            await level2Cache.GetOrDefaultAsync<SealedZkAppKey?>(SealedZkAppKeyCacheKey(clientIdOrDomain, zkAppKeyPublicKey), null) == null)
        {
            return true;
        }

        //TODO: need to talk with Seb about the redirecting loop issue here
        var tempConsent = await level2Cache.GetOrDefaultAsync(TempConsentCacheKey(clientIdOrDomain), false);
        if (tempConsent)
        {
            await level2Cache.RemoveAsync(TempConsentCacheKey(clientIdOrDomain));
            return false;
        }

        if (clientType == ClientType.domain)
        {
            // The hosting provider's own domains (HostingProvider:AutoConsentDomains): it runs this host already, so
            // knowing who the owner is gives it nothing new. Domains only, never apps.
            var domain = new AsciiDomainName(clientIdOrDomain);
            if (configuration.HostingProvider.AutoConsentDomains.Contains(domain.DomainName))
            {
                return false;
            }

            return await domainRegistrationService.IsConsentRequiredAsync(domain, odinContext);
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

    public async Task StoreSealedZkAppKeyAsync(string clientId, string jwkbase64UrlPublicKey, string sealedZkAppKey64,
        string lockedZkAppKey, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        // A concurrent first sign-in (or login backfill) may have saved a different key since the browser made this one;
        // the device would hold a key nothing is locked with
        var appReg = await GetAppRegistrationByClientIdAsync(clientId, odinContext)
                     ?? throw new OdinClientException("App is not registered", OdinClientErrorCode.AppNotRegistered);
        var keyId = AppRegistration.ZkAppKeyId(lockedZkAppKey);
        if (keyId != await appRegistrationService.GetZkAppKeyIdAsync(appReg.AppId, odinContext))
        {
            throw new OdinClientException("The app's zero-knowledge key changed during this sign-in; try again",
                OdinClientErrorCode.ZkAppKeyAlreadySet);
        }

        // The browser seals to the key in the sign-in it is consenting to; anything else would never open on the device
        var publicKeyJwk = Base64UrlEncoder.DecodeString(jwkbase64UrlPublicKey);
        if (!ZkSeal.IsSealedTo(ZkBlob.AssertBase64(sealedZkAppKey64, nameof(sealedZkAppKey64)), publicKeyJwk))
        {
            throw new OdinClientException("The app's zero-knowledge key must be sealed to the sign-in's public key",
                OdinClientErrorCode.ArgumentError);
        }

        await level2Cache.SetAsync(SealedZkAppKeyCacheKey(clientId, jwkbase64UrlPublicKey),
            new SealedZkAppKey { Sealed64 = sealedZkAppKey64, KeyId = keyId! }, TimeSpan.FromMinutes(60));
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
        YouAuthCipher cipher,
        IOdinContext odinContext,
        bool wantsZkAppKey = false)
    {
        odinContext.Caller.AssertHasMasterKey();

        ClientAccessToken? token;
        SealedZkAppKey? sealedZkAppKey = null;
        if (clientType == ClientType.app)
        {
            // An app id goes straight to RegisterClientAsync, which loads the registration itself
            if (!IsAppId(clientId, out var appId))
            {
                var appReg = await GetAppRegistrationByClientIdAsync(clientId, odinContext)
                             ?? throw new OdinClientException("App is not registered", OdinClientErrorCode.AppNotRegistered);
                appId = appReg.AppId;
            }

            OdinValidationUtils.AssertIsTrue(appId != Guid.Empty, "AppId is invalid");
            OdinValidationUtils.AssertNotNullOrEmpty(clientInfo, nameof(clientInfo));

            if (wantsZkAppKey)
            {
                // Sealed by the owner's browser at consent, for this sign-in only
                var cacheKey = SealedZkAppKeyCacheKey(clientId, jwkbase64UrlPublicKey);
                sealedZkAppKey = await level2Cache.GetOrDefaultAsync<SealedZkAppKey?>(cacheKey, null)
                                 ?? throw new OdinClientException("The app's zero-knowledge key wasn't given at consent",
                                     OdinClientErrorCode.ZkAppKeyMissing);
                await level2Cache.RemoveAsync(cacheKey);
            }

            //TODO: Need to check if the app is registered, if not need redirect to get consent.
            (token, _) = await appRegistrationService.RegisterClientAsync(appId, clientInfo, odinContext, sealedZkAppKey?.KeyId);
        }
        else if (clientType == ClientType.domain)
        {
            if (wantsZkAppKey)
            {
                throw new OdinClientException("Only an app gets a zero-knowledge key", OdinClientErrorCode.ArgumentError);
            }

            var domain = new AsciiDomainName(clientId);

            var info = await circleNetwork.GetIcrAsync((OdinId)domain, odinContext);
            if (info.IsConnected())
            {
                var icrKey = odinContext.PermissionsContext.GetIcrKey();
                token = info.CreateClientAccessToken(icrKey);
            }
            else
            {
                // clientInfo is the name the domain published for itself, fetched from that domain
                // by the authorize endpoint, or empty when it published none. It names the
                // registration and the client; the bare host is the fallback.
                var publishedName = string.IsNullOrWhiteSpace(clientInfo) ? null : clientInfo;
                var name = publishedName ?? domain.DomainName;

                var request = new YouAuthDomainRegistrationRequest()
                {
                    Domain = domain.DomainName,
                    Name = name,
                    CorsHostName = clientId,
                    CircleIds = default //TODO: should we set a circle here?
                };

                (token, _) = await domainRegistrationService.RegisterClientAsync(domain, name, request, odinContext, publishedName);
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

        // Sealed with what the client said it can open at [030].
        var (sharedSecretIv, sharedSecretCipher) = cipher.Seal(token.SharedSecret.GetKey(), exchangeSharedSecret);
        var (clientAuthTokenIv, clientAuthTokenCipher) = cipher.Seal(token.ToAuthenticationToken().ToPortableBytes(), exchangeSharedSecret);

        var encryptedTokenExchange = new EncryptedTokenExchange(
            sharedSecretCipher,
            sharedSecretIv,
            clientAuthTokenCipher,
            clientAuthTokenIv,
            cipher)
        {
            ZkAppKey = sealedZkAppKey?.Sealed64
        };

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
        var appReg = await GetAppRegistrationByClientIdAsync(clientIdOrDomain, odinContext);
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

    /// <summary>
    /// An app client_id is its app id or, so that a second device can sign in without knowing a
    /// per-identity id, its slug (#1902): one that parses as a GUID is an app id, anything else a slug.
    /// </summary>
    public static bool IsAppId(string clientId, out Guid appId) => Guid.TryParse(clientId, out appId);

    /// <summary>The app an app client_id names on this identity; null when no registered app has it.</summary>
    private async Task<RedactedAppRegistration?> GetAppRegistrationByClientIdAsync(string clientId, IOdinContext odinContext)
    {
        return IsAppId(clientId, out var appId)
            ? await appRegistrationService.GetAppRegistration(appId, odinContext)
            : OdinSlug.TryParse(clientId, out var appSlug)
                ? await appRegistrationService.GetAppRegistrationBySlugAsync(appSlug, odinContext)
                : null;
    }

    //

    private async Task AssertCanAcquireConsent(ClientType clientType, string clientIdOrDomain, string permissionRequest, IOdinContext odinContext)
    {
        if (clientType == ClientType.app)
        {
            if (await AppNeedsRegistration(clientIdOrDomain, permissionRequest, odinContext))
            {
                throw new OdinClientException("App must be registered before consent check is possible",
                    OdinClientErrorCode.AppNotRegistered);
            }
        }
    }

    //

    private string TempConsentCacheKey(string clientIdOrDomain) => $"TempConsent:{clientIdOrDomain}";

    /// <summary>The app's key sealed to one sign-in, and the id of the key it is, which the device is marked with.</summary>
    private sealed class SealedZkAppKey
    {
        public string Sealed64 { get; init; } = "";
        public string KeyId { get; init; } = "";
    }

    private static string SealedZkAppKeyCacheKey(string clientId, string jwkbase64UrlPublicKey) =>
        $"SealedZkAppKey:{clientId}:{SHA256.HashData(jwkbase64UrlPublicKey.ToUtf8ByteArray()).ToBase64()}";
    private string EncryptedTokenCacheKey(string exchangeSharedSecretDigest) =>
        $"EncryptedToken:{exchangeSharedSecretDigest}";
}
//