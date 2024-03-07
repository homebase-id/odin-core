using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Services.Membership.YouAuth;
using Odin.Core.Services.Util;
using Odin.Core.Util;

namespace Odin.Core.Services.Authentication.YouAuth;

#nullable enable

public sealed class YouAuthUnifiedService : IYouAuthUnifiedService
{
    private readonly IMemoryCache _encryptedTokens = new MemoryCache(new MemoryCacheOptions());
    private readonly IAppRegistrationService _appRegistrationService;
    private readonly OdinContextAccessor _contextAccessor;
    private readonly YouAuthDomainRegistrationService _domainRegistrationService;
    private readonly Dictionary<string, bool> _tempConsent;
    private readonly CircleNetworkService _circleNetwork;

    public YouAuthUnifiedService(IAppRegistrationService appRegistrationService,
        OdinContextAccessor contextAccessor,
        YouAuthDomainRegistrationService domainRegistrationService, CircleNetworkService circleNetwork)
    {
        _appRegistrationService = appRegistrationService;
        _contextAccessor = contextAccessor;
        _domainRegistrationService = domainRegistrationService;
        _circleNetwork = circleNetwork;

        _tempConsent = new Dictionary<string, bool>(StringComparer.InvariantCultureIgnoreCase);
    }

    //

    public async Task<bool> NeedConsent(
        string tenant,
        ClientType clientType,
        string clientIdOrDomain,
        string permissionRequest,
        string redirectUri)
    {
        await AssertCanAcquireConsent(clientType, clientIdOrDomain, permissionRequest);

        //TODO: need to talk with Seb about the redirecting loop issue here
        if (_tempConsent.ContainsKey(clientIdOrDomain))
        {
            _tempConsent.Remove(clientIdOrDomain);
            return false;
        }

        if (clientType == ClientType.domain)
        {
            return await _domainRegistrationService.IsConsentRequired(new AsciiDomainName(clientIdOrDomain));
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

    public async Task StoreConsent(string clientIdOrDomain, ClientType clientType, string permissionRequest, ConsentRequirements consentRequirements)
    {
        if (clientType == ClientType.app)
        {
            //so for now i'll just use this dictionary
            _tempConsent[clientIdOrDomain] = true;
        }

        if (clientType == ClientType.domain)
        {
            var domain = new AsciiDomainName(clientIdOrDomain);

            var existingDomain = await _domainRegistrationService.GetRegistration(domain);
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

                await _domainRegistrationService.RegisterDomain(request);
            }
            else
            {
                await _domainRegistrationService.UpdateConsentRequirements(domain, consentRequirements);
            }
            
            //so for now i'll just use this dictionary
            _tempConsent[clientIdOrDomain] = true;
        }
    }

    //

    public async Task<(string exchangePublicKey, string exchangeSalt)> CreateClientAccessToken(
        ClientType clientType,
        string clientId,
        string clientInfo,
        string permissionRequest,
        string publicKey)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        ClientAccessToken? token;
        if (clientType == ClientType.app)
        {
            Guid appId = Guid.Parse(clientId);
            
            OdinValidationUtils.AssertIsTrue(appId != Guid.Empty, "AppId is invalid");
            OdinValidationUtils.AssertNotNullOrEmpty(clientInfo, nameof(clientInfo));
            
            //TODO: Need to check if the app is registered, if not need redirect to get consent.
            (token, _) = await _appRegistrationService.RegisterClient(appId, clientInfo);
        }
        else if (clientType == ClientType.domain)
        {
            var domain = new AsciiDomainName(clientId);

            var info = await _circleNetwork.GetIdentityConnectionRegistration((OdinId)domain);
            if (info.IsConnected())
            {
                var icrKey = _contextAccessor.GetCurrent().PermissionsContext.GetIcrKey();
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

                (token, _) = await _domainRegistrationService.RegisterClient(domain, domain.DomainName, request);
            }
        }
        else
        {
            throw new OdinSystemException($"Invalid clientType '{clientType}'");
        }

        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);
        var exchangeSalt = ByteArrayUtil.GetRndByteArray(16);

        var remotePublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(publicKey);
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

        _encryptedTokens.Set(exchangeSharedSecretDigest, encryptedTokenExchange, TimeSpan.FromMinutes(5));

        return (keyPair.PublicKeyJwkBase64Url(), Convert.ToBase64String(exchangeSalt));
    }

    //

    public Task<EncryptedTokenExchange?> ExchangeDigestForEncryptedToken(string exchangeSharedSecretDigest)
    {
        var ec = _encryptedTokens.Get<EncryptedTokenExchange>(exchangeSharedSecretDigest);

        if (ec == null)
        {
            return Task.FromResult<EncryptedTokenExchange?>(null);
        }

        _encryptedTokens.Remove(exchangeSharedSecretDigest);

        return Task.FromResult(ec)!;
    }

    //

    public async Task<bool> AppNeedsRegistration(string clientIdOrDomain, string permissionRequest)
    {
        if (!Guid.TryParse(clientIdOrDomain, out var appId))
        {
            throw new OdinClientException("App id must be a uuid", OdinClientErrorCode.ArgumentError);
        }

        var appReg = await _appRegistrationService.GetAppRegistration(appId);
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

    private async Task AssertCanAcquireConsent(ClientType clientType, string clientIdOrDomain, string permissionRequest)
    {
        if (clientType == ClientType.app)
        {
            if (await AppNeedsRegistration(clientIdOrDomain, permissionRequest))
            {
                throw new OdinSystemException("App must be registered before consent check is possible");
            }
        }
    }

    //
}
//