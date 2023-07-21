using System;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions.Client;
using Odin.Core.Services.Authentication.YouAuth;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth;

#nullable enable

public class YouAuthAuthorizeRequest
{
    public const string RedirectUriName = "redirect_uri";
    [BindProperty(Name = RedirectUriName, SupportsGet = true)]
    public string RedirectUri { get; set; } = "";
    
    public const string ClientIdName = "client_id";
    [BindProperty(Name = ClientIdName, SupportsGet = true)]
    public string ClientId { get; set; } = "";
        
    public const string ClientTypeName = "client_type";
    [BindProperty(Name = ClientTypeName, SupportsGet = true)]
    public ClientType ClientType { get; set; } = ClientType.unknown;
    
    public const string ClientInfoName = "client_info";
    [BindProperty(Name = ClientInfoName, SupportsGet = true)]
    public string ClientInfo { get; set; } = "";
    
    public const string CodeChallengeName = "code_challenge";
    [BindProperty(Name = CodeChallengeName, SupportsGet = true)]
    public string CodeChallenge { get; set; } = "";
    
    public const string PermissionRequestName = "permission_request";
    [BindProperty(Name = PermissionRequestName, SupportsGet = true)]
    public string PermissionRequest { get; set; } = ""; 

    public const string TokenDeliveryOptionName = "token_delivery_option";
    [BindProperty(Name = TokenDeliveryOptionName, SupportsGet = true)]
    public TokenDeliveryOption TokenDeliveryOption { get; set; } = TokenDeliveryOption.unknown; 
    
    //

    public YouAuthAuthorizeRequest()
    {
        // Empty on purpose. Needed by controller.
    }
    
    //

    private YouAuthAuthorizeRequest(
        string redirectUri,
        ClientType clientType,
        string clientId,
        TokenDeliveryOption tokenDeliveryOption,
        string codeChallenge,
        string permissionRequest,
        string clientInfo
        )
    {
        RedirectUri = redirectUri;
        ClientType = clientType;
        ClientId = clientId;
        TokenDeliveryOption = tokenDeliveryOption;
        CodeChallenge = codeChallenge;
        PermissionRequest = permissionRequest;
        ClientInfo = clientInfo;
    }
    
    //
    
    public string ToQueryString()
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        
        qs[ClientIdName] = ClientId;
        qs[ClientTypeName] = ClientType.ToString();
        qs[ClientInfoName] = ClientInfo;
        qs[CodeChallengeName] = CodeChallenge;
        qs[RedirectUriName] = RedirectUri;
        qs[PermissionRequestName] = PermissionRequest;
        qs[TokenDeliveryOptionName] = TokenDeliveryOption.ToString();
    
        return qs.ToString() ?? string.Empty;
    }
    
    //
    
    public static YouAuthAuthorizeRequest FromQueryString(string queryString)
    {
        var qs = HttpUtility.ParseQueryString(queryString);

        if (!Enum.TryParse(qs[ClientTypeName], out ClientType clientType))
        {
            clientType = ClientType.unknown;
        }
        
        if (!Enum.TryParse(qs[TokenDeliveryOptionName], out TokenDeliveryOption tokenDeliveryOption))
        {
            tokenDeliveryOption = TokenDeliveryOption.unknown;
        }

        return new YouAuthAuthorizeRequest(
            redirectUri: qs[RedirectUriName] ?? string.Empty,
            clientType: clientType,
            clientId: qs[ClientIdName] ?? string.Empty,
            tokenDeliveryOption: tokenDeliveryOption,
            codeChallenge: qs[CodeChallengeName] ?? string.Empty,
            permissionRequest: qs[PermissionRequestName] ?? string.Empty,
            clientInfo: qs[ClientInfoName] ?? string.Empty);
    }
    
    //

    public void Validate()
    {
        if (ClientType != ClientType.app && ClientType != ClientType.domain)
        {
            throw new BadRequestException($"Bad or missing {ClientType}");
        }
        if (ClientType == ClientType.app && string.IsNullOrWhiteSpace(ClientId))
        {
            throw new BadRequestException($"{ClientIdName} is required when {ClientTypeName} is {ClientType.app}");
        }
        if (string.IsNullOrWhiteSpace(CodeChallenge))
        {
            throw new BadRequestException($"Bad or missing {CodeChallengeName}");
        }
        if (string.IsNullOrWhiteSpace(RedirectUri))
        {
            throw new BadRequestException($"Bad or missing {RedirectUriName}");
        }
        if (TokenDeliveryOption != TokenDeliveryOption.cookie && TokenDeliveryOption != TokenDeliveryOption.json)
        {
            throw new BadRequestException($"Bad or missing {TokenDeliveryOption}");
        }
    }
}

public class YouAuthAuthorizeConsentGiven
{
    public const string ReturnUrlName = "return_url";
}
