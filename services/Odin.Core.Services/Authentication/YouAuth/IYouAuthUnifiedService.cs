using System;
using System.Threading.Tasks;

namespace Odin.Core.Services.Authentication.YouAuth;

#nullable enable

// ReSharper disable InconsistentNaming
public enum ClientType
{
    unknown,
    app, 
    domain
}

public enum TokenDeliveryOption
{
    unknown,
    cookie,
    json
}
// ReSharper restore InconsistentNaming


public interface IYouAuthUnifiedService
{
    Task<bool> NeedConsent(
        string tenant, 
        ClientType clientType, 
        string clientIdOrDomain, 
        string permissionRequest);
    
    Task StoreConsent(string clientIdOrDomain, string permissionRequest);
    
    Task<string> CreateAuthorizationCode(ClientType clientType,
        string clientId,
        string clientInfo,
        string permissionRequest,
        string codeChallenge,
        TokenDeliveryOption tokenDeliveryOption);

    Task<bool> ExchangeCodeForToken(
        string code,
        string codeVerifier,
        out byte[]? sharedSecret,
        out byte[]? clientAccessToken);
}
