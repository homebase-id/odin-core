using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Certes;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Exceptions;
using Odin.Core.Exceptions.Client;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.YouAuth;
using Odin.Core.Services.Base;
using Odin.Core.Util;

namespace Odin.Core.Services.Authentication.YouAuth;

#nullable enable

public sealed class YouAuthUnifiedService : IYouAuthUnifiedService
{
    private readonly IMemoryCache _codesAndTokens = new MemoryCache(new MemoryCacheOptions());

    private readonly IAppRegistrationService _appRegistrationService;
    private readonly OdinContextAccessor _contextAccessor;
    private readonly YouAuthDomainRegistrationService _domainRegistrationService;

    public YouAuthUnifiedService(IAppRegistrationService appRegistrationService,
        OdinContextAccessor contextAccessor,
        YouAuthDomainRegistrationService domainRegistrationService)
    {
        _appRegistrationService = appRegistrationService;
        _contextAccessor = contextAccessor;
        _domainRegistrationService = domainRegistrationService;
    }

    //

    public async Task<bool> NeedConsent(string tenant, ClientType clientType, string clientIdOrDomain, string permissionRequest)
    {
        await AssertCanAcquireConsent(clientType, clientIdOrDomain, permissionRequest);

        if (clientType == ClientType.domain)
        {
            return await _domainRegistrationService.IsConsentRequired(new AsciiDomainName(clientIdOrDomain));
        }

        // clientType == app always needs consent
        return true;
    }

    //

    public Task StoreConsent(string clientIdOrDomain, string permissionRequest)
    {
        if (string.IsNullOrWhiteSpace(clientIdOrDomain))
        {
            throw new ArgumentException("Missing clientId");
        }

        //TODO: i wonder if consent should be stored here or by the UI call on the backend.
        // if the latter, we need a mechanism proving the result of the consent

        // _authorizations[clientIdOrDomain] = permissionRequest;
        return Task.CompletedTask;
    }

    //

    public async Task<string> CreateAuthorizationCode(ClientType clientType,
        string clientId,
        string clientInfo,
        string permissionRequest,
        string codeChallenge)
    {
        _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

        ClientAccessToken token;
        if (clientType == ClientType.app)
        {
            Guid appId = Guid.Parse(clientId);
            var deviceFriendlyName = clientInfo;

            (token, _) = await _appRegistrationService.RegisterClientRaw(appId, deviceFriendlyName);
        }
        else // (clientType == ClientType.domain)
        {
            var domain = new AsciiDomainName(clientId);
            var request = new YouAuthDomainRegistrationRequest()
            {
                Domain = domain,
                Name = domain.DomainName,
                CorsHostName = clientId,
                Drives = default,
                PermissionSet = default
            };

            (token, _) = await _domainRegistrationService.RegisterClient(domain, domain.DomainName, request);
        }

        var code = Guid.NewGuid().ToString();

        var ac = new AuthorizationCodeAndToken(
            code,
            clientType,
            permissionRequest,
            codeChallenge,
            token);

        _codesAndTokens.Set(code, ac, TimeSpan.FromMinutes(5));

        return code;
    }

    //

    public Task<ClientAccessToken?> ExchangeCodeForToken(string code, string codeVerifier)
    {
        var ac = _codesAndTokens.Get<AuthorizationCodeAndToken>(code);

        if (ac == null)
        {
            return Task.FromResult<ClientAccessToken?>(null);
        }

        var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();
        if (codeChallenge != ac.CodeChallenge)
        {
            return Task.FromResult<ClientAccessToken?>(null);
        }

        _codesAndTokens.Remove(code);

        var accessToken = ac.PreCreatedClientAccessToken;

        return Task.FromResult(accessToken)!;
    }

    //

    public async Task<bool> AppNeedsRegistration(ClientType clientType, string clientIdOrDomain, string permissionRequest)
    {
        if (clientType != ClientType.app)
        {
            throw new OdinSystemException($"Invalid clientType '{clientType}'");
        }

        var appId = Guid.Parse(clientIdOrDomain);
        var appReg = await _appRegistrationService.GetAppRegistration(appId);

        return appReg == null;
    }

    //

    private async Task AssertCanAcquireConsent(ClientType clientType, string clientIdOrDomain, string permissionRequest)
    {
        if (clientType == ClientType.app)
        {
            if (await AppNeedsRegistration(clientType, clientIdOrDomain, permissionRequest))
            {
                throw new OdinSystemException("App must be registered before consent check is possible");
            }
        }
    }

    //

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

        public ClientAccessToken PreCreatedClientAccessToken { get; }

        public AuthorizationCodeAndToken(string code,
            ClientType clientType,
            string permissionRequest,
            string codeChallenge,
            ClientAccessToken clientAccessToken)
        {
            Code = code;
            ClientType = clientType;
            PermissionRequest = permissionRequest;
            CodeChallenge = codeChallenge;
            PreCreatedClientAccessToken = clientAccessToken;
        }
    }

    //
}