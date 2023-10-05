using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Dawn;
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

    public async Task<bool> NeedConsent(string tenant, ClientType clientType, string clientIdOrDomain,
        string permissionRequest)
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

        //apps always require consent
        return true;
    }

    //

    public Task StoreConsent(string clientIdOrDomain, ClientType clientType, string permissionRequest, ConsentRequirements consentRequirements)
    {
        Guard.Argument(clientIdOrDomain, nameof(clientIdOrDomain)).NotEmpty().NotWhiteSpace();
        Guard.Argument(consentRequirements, nameof(consentRequirements)).NotNull();

        if (clientType == ClientType.app)
        {
            //so for now i'll just use this dictionary
            _tempConsent[clientIdOrDomain] = true;
        }

        if (clientType == ClientType.domain)
        {
            var domain = new AsciiDomainName(clientIdOrDomain);

            var existingDomain = _domainRegistrationService.GetRegistration(domain).GetAwaiter().GetResult();
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

                _domainRegistrationService.RegisterDomain(request).GetAwaiter().GetResult();
            }
            else
            {
                _domainRegistrationService.UpdateConsentRequirements(domain, consentRequirements).GetAwaiter().GetResult();
            }
            
            //so for now i'll just use this dictionary
            _tempConsent[clientIdOrDomain] = true;
        }

        return Task.CompletedTask;
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
            var deviceFriendlyName = clientInfo;

            //TODO: Need to check if the app is registered, if not need redirect to get consent.
            (token, _) = await _appRegistrationService.RegisterClient(appId, deviceFriendlyName);
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

        // SEB:TODO consider using one of identity's ECC keys instead of creating a new one
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, 1);
        var exchangeSalt = ByteArrayUtil.GetRndByteArray(16);

        var remotePublicKey = EccPublicKeyData.FromJwkBase64UrlPublicKey(publicKey);
        var exchangeSharedSecret = keyPair.GetEcdhSharedSecret(privateKey, remotePublicKey, exchangeSalt);
        var exchangeSharedSecretDigest = SHA256.Create().ComputeHash(exchangeSharedSecret.GetKey()).ToBase64();

        var sharedSecretPlain = token.SharedSecret.GetKey();
        var (sharedSecretIv, sharedSecretCipher) = AesCbc.Encrypt(sharedSecretPlain, ref exchangeSharedSecret);

        var clientAuthTokenPlain = token.ToAuthenticationToken().ToPortableBytes();
        var (clientAuthTokenIv, clientAuthTokenCipher) = AesCbc.Encrypt(clientAuthTokenPlain, ref exchangeSharedSecret);

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
        var appId = Guid.Parse(clientIdOrDomain);
        var appReg = await _appRegistrationService.GetAppRegistration(appId);
        if (appReg == null)
        {
            return true;
        }

        if (appReg.IsRevoked)
        {
            throw new OdinSecurityException("App is revoked");
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