using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Exceptions;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Util;

namespace Odin.Core.Services.Authentication.YouAuth;

#nullable enable

public sealed class YouAuthUnifiedService : IYouAuthUnifiedService
{
    private readonly ConcurrentDictionary<string, string>
        _authorizations = new(); // SEB:TODO this should go to the database instead

    private readonly IMemoryCache _codesAndTokens = new MemoryCache(new MemoryCacheOptions());

    private readonly IAppRegistrationService _appRegistrationService;
    private readonly OdinContextAccessor _contextAccessor;
    private readonly YouAuthConsentService _consentService;
    private readonly ExchangeGrantService _exchangeGrantService;

    public YouAuthUnifiedService(IAppRegistrationService appRegistrationService, OdinContextAccessor contextAccessor, YouAuthConsentService consentService, ExchangeGrantService exchangeGrantService)
    {
        _appRegistrationService = appRegistrationService;
        _contextAccessor = contextAccessor;
        _consentService = consentService;
        _exchangeGrantService = exchangeGrantService;
    }

    //

    public Task<bool> NeedConsent(string tenant, ClientType clientType, string clientIdOrDomain, string permissionRequest)
    {
        AssertCanAcquireConsent(clientType, clientIdOrDomain, permissionRequest);

        // Lookup clientId and permissionRequest of previously stored consent.
        if (_authorizations.ContainsKey(clientIdOrDomain)) // SEB:TODO include permissionRequest in check
        {
            return Task.FromResult(false);
        }

        if (clientType == ClientType.domain)
        {
            bool needsConsent = _consentService.IsConsentRequired(new SimpleDomainName(clientIdOrDomain))
                .GetAwaiter()
                .GetResult();

            return Task.FromResult(needsConsent);

            // SEB:TODO
            // if (!isConnected)
            // {
            //    return Task.FromResult(true);    
            // }

            // SEB:TODO
            // if (string.NullOrWhitespace(permissionRequest)
            // {
            //   return Task.FromResult(false);
            // }
            // return Task.FromResult(true);
        }

        //clientType == app always needs consent
        return Task.FromResult(true);
    }

    //

    public Task StoreConsent(string clientIdOrDomain, string permissionRequest)
    {
        if (string.IsNullOrWhiteSpace(clientIdOrDomain))
        {
            throw new ArgumentException("Missing clientId");
        }

        _authorizations[clientIdOrDomain] = permissionRequest;
        return Task.CompletedTask;
    }

    //

    public Task<string> CreateAuthorizationCode(ClientType clientType,
        string clientId,
        string clientInfo,
        string permissionRequest,
        string codeChallenge,
        TokenDeliveryOption tokenDeliveryOption)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        ClientAccessToken token = null;
        if (clientType == ClientType.app)
        {
            Guid appId = Guid.Parse(clientId);
            var deviceFriendlyName = clientInfo;

            (token, _) = _appRegistrationService.RegisterClientRaw(
                    appId,
                    deviceFriendlyName)
                .GetAwaiter()
                .GetResult();
        }

        if (clientType == ClientType.domain)
        {
            var emptyKey = Guid.Empty.ToByteArray().ToSensitiveByteArray();
            (var accessRegistration, token) = _exchangeGrantService.CreateClientAccessToken(emptyKey, ClientTokenType.YouAuth)
                .GetAwaiter()
                .GetResult();

            var client = new YouAuthUnifiedClient(accessRegistration.Id, new SimpleDomainName(clientId), accessRegistration);

            //SEB: todo- store the client in your youauth authorizations database
        }

        var code = Guid.NewGuid().ToString();

        var ac = new AuthorizationCodeAndToken(
            code,
            clientType,
            permissionRequest,
            codeChallenge,
            tokenDeliveryOption,
            token);

        _codesAndTokens.Set(code, ac, TimeSpan.FromMinutes(5));

        return Task.FromResult(code);
    }

    //

    public Task<bool> ExchangeCodeForToken(
        string code,
        string codeVerifier,
        out byte[]? sharedSecret,
        out byte[]? clientAuthToken)
    {
        sharedSecret = null;
        clientAuthToken = null;

        var ac = _codesAndTokens.Get<AuthorizationCodeAndToken>(code);

        if (ac == null)
        {
            return Task.FromResult(false);
        }

        var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();
        if (codeChallenge != ac.CodeChallenge)
        {
            return Task.FromResult(false);
        }

        _codesAndTokens.Remove(code);


        //
        // TODD:YOUAUTH
        // Create CAT and shared secret here. Use: 
        //   ac.ClientType determines if it's an app or domain (aka thirdparty, aka identity)
        //   ac.TokenDeliveryOption determines if put CAT in cookie or json response
        //
        var accessToken = ac.PreCreatedClientAccessToken;
        sharedSecret = accessToken.SharedSecret.GetKey();
        clientAuthToken = accessToken.ToAuthenticationToken().ToPortableBytes();


        return Task.FromResult(true);
    }

    //

    private void AssertCanAcquireConsent(ClientType clientType, string clientIdOrDomain, string permissionRequest)
    {
        if (clientType == ClientType.app)
        {
            //check if app is registered
            var appId = Guid.Parse(clientIdOrDomain);
            var appReg = _appRegistrationService.GetAppRegistration(appId).GetAwaiter().GetResult();

            if (null == appReg)
            {
                throw new OdinClientException("App not registered");
            }
        }
    }

    //

    private sealed class AuthorizationCodeAndToken
    {
        public string Code { get; }
        public ClientType ClientType { get; }
        public string PermissionRequest { get; }
        public string CodeChallenge { get; }
        public TokenDeliveryOption TokenDeliveryOption { get; }

        public ClientAccessToken PreCreatedClientAccessToken { get; }

        public AuthorizationCodeAndToken(string code,
            ClientType clientType,
            string permissionRequest,
            string codeChallenge,
            TokenDeliveryOption tokenDeliveryOption,
            ClientAccessToken clientAccessToken)
        {
            Code = code;
            ClientType = clientType;
            PermissionRequest = permissionRequest;
            CodeChallenge = codeChallenge;
            TokenDeliveryOption = tokenDeliveryOption;
            PreCreatedClientAccessToken = clientAccessToken;
        }
    }

    //
}