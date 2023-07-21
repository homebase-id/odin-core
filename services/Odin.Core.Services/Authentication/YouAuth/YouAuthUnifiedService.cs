using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Odin.Core.Services.Authentication.YouAuth;

#nullable enable

public sealed class YouAuthUnifiedService : IYouAuthUnifiedService
{
    private readonly ConcurrentDictionary<string, string>
        _authorizations = new(); // SEB:TODO this should go to the database instead

    private readonly IMemoryCache _codesAndTokens = new MemoryCache(new MemoryCacheOptions());  

    //

    public Task<bool> NeedConsent(string tenant, ClientType clientType, string clientIdOrDomain, string permissionRequest)
    {
        // Lookup clientId and permissionRequest of previously stored consent.
        if (_authorizations.ContainsKey(clientIdOrDomain)) // SEB:TODO include permissionRequest in check
        {
            return Task.FromResult(false);    
        }

        if (clientType == ClientType.domain)
        {
            // TODD:YOUAUTH
            // isConnected = IsConnected(tenant, clientIdOrDomain)
            
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

    public Task<string> CreateAuthorizationCode(
        ClientType clientType,
        string permissionRequest, 
        string codeChallenge, 
        TokenDeliveryOption tokenDeliveryOption)
    {
        var code = Guid.NewGuid().ToString();
        
        var ac = new AuthorizationCodeAndToken(
            code, 
            clientType, 
            permissionRequest, 
            codeChallenge, 
            tokenDeliveryOption);
        
        _codesAndTokens.Set(code, ac, TimeSpan.FromMinutes(5));
        
        return Task.FromResult(code);
    }
    
    //

    public Task<bool> ExchangeCodeForToken(
        string code, 
        string codeVerifier, 
        out byte[]? sharedSecret,
        out byte[]? clientAccessToken)
    {
        sharedSecret = null;
        clientAccessToken = null;

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

        sharedSecret = "Put shared secret here".ToUtf8ByteArray();
        clientAccessToken = "Put client access token here ac.TokenDeliveryOption == json".ToUtf8ByteArray();
        
        //
        // TODD:YOUAUTH
        // if (ac.ClientType == app)
        // {
        //   Register app,
        //   Create app drives
        //   etc.
        // }
        //

        return Task.FromResult(true);
    }
    
    //
    
    private sealed class AuthorizationCodeAndToken
    {
        public string Code { get; }
        public ClientType ClientType { get; }
        public string PermissionRequest { get; }
        public string CodeChallenge { get; }
        public TokenDeliveryOption TokenDeliveryOption { get; }

        public AuthorizationCodeAndToken(
            string code, 
            ClientType clientType,
            string permissionRequest, 
            string codeChallenge, 
            TokenDeliveryOption tokenDeliveryOption)
        {
            Code = code;
            ClientType = clientType;
            PermissionRequest = permissionRequest;
            CodeChallenge = codeChallenge;
            TokenDeliveryOption = tokenDeliveryOption;
        }
    }
    
    //
    
}

