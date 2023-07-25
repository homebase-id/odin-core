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

    public YouAuthUnifiedService(IAppRegistrationService appRegistrationService, OdinContextAccessor contextAccessor, YouAuthConsentService consentService,
        ExchangeGrantService exchangeGrantService)
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
                //SEB: TODO = do a redirect to app registration
                // throw new OdinClientException("App not registered");
                DoRedirectToAppReg();
            }
        }
    }

    private void DoRedirectToAppReg()
    {
        //example: https://frodo.digital/owner/appreg?n=Odin%20-%20Photos&o=photos.odin.earth&appId=32f0bdbf-017f-4fc0-8004-2d4631182d1e&fn=Firefox%20%7C%20macOS&return=https%3A%2F%2Fphotos.odin.earth%2Fauth%2Ffinalize%3FreturnUrl%3D%252F%26&d=%5B%7B%22a%22%3A%226483b7b1f71bd43eb6896c86148668cc%22%2C%22t%22%3A%222af68fe72fb84896f39f97c59d60813a%22%2C%22n%22%3A%22Photo%20Library%22%2C%22d%22%3A%22Place%20for%20your%20memories%22%2C%22p%22%3A3%7D%5D&pk=MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA3lESpzsGk5PXQysoPZxXJ4Cp2FXycnAGxETP%2FF47EWWqDyKaR3Q1er16h4JNBZbvGQjoCgDUT5Q8vknBrnTJGL2z%2FVVdPsIenZ4IWsvI4hM%2FxQ7bQ3N4v4OJNb5f7dGtHAWrDEhpRYv1dw5s2ZnvxnxipkUc%2FUiazUuCrNV4OGTKsyeRAXdcteXrO13KK2ywl9s2eUBPLjy9OD5Vm4Du3FLDdJ2xkW6klKnINA%2BYPMFTLfeuhgJIloBMbNCyWxz0LLWiztB%2Bx0kqJyXGYPGcHxhPfUJppna6bsoJcQ462zFpkozZ%2BHROAfV324S4nHyL%2B4BvMfdcjLvEjwZAtcYy9QIDAQAB

            //TODO: the following are parameters that come in from the App
            // Guid appId = Guid.Parse("32f0bdbf-017f-4fc0-8004-2d4631182d1e");
            // string deviceFriendlyName = "TODO";
            // string appName = "TODO";
            // string origin = "photos.odin.earth"; //Note: this might empty if the app is something like chat
            //
            // //TODO: Currently the client passes in a base64 public key that we use
            // //to encrypt the result; that will probably change with YouAuthUnified
            // string publicKey64 =
            //     "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA3lESpzsGk5PXQysoPZxXJ4Cp2FXycnAGxETP%2FF47E" +
            //     "WWqDyKaR3Q1er16h4JNBZbvGQjoCgDUT5Q8vknBrnTJGL2z%2FVVdPsIenZ4IWsvI4hM%2FxQ7bQ3N4v4OJNb5f" +
            //     "7dGtHAWrDEhpRYv1dw5s2ZnvxnxipkUc%2FUiazUuCrNV4OGTKsyeRAXdcteXrO13KK2ywl9s2eUBPLjy9OD5Vm4" +
            //     "Du3FLDdJ2xkW6klKnINA%2BYPMFTLfeuhgJIloBMbNCyWxz0LLWiztB%2Bx0kqJyXGYPGcHxhPfUJppna6bsoJcQ" +
            //     "462zFpkozZ%2BHROAfV324S4nHyL%2B4BvMfdcjLvEjwZAtcYy9QIDAQAB";
            //
            // var appRegistrationPage = $"{Request.Scheme}://{Request.Host}/owner/appreg?" +
            //                   $"appId={appId}" +
            //                   $"&o={origin}" +
            //                   $"&n={appName}" +
            //                   $"&fn={deviceFriendlyName}" +
            //                   $"&return={returnUrl}";
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