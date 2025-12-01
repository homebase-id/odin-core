using System;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.YouAuth;
using Odin.Hosting.ApiExceptions.Client;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth;

#nullable enable

public sealed class YouAuthAuthorizeRequest
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

    public const string PermissionRequestName = "permission_request";
    [BindProperty(Name = PermissionRequestName, SupportsGet = true)]
    public string PermissionRequest { get; set; } = "";

    public const string PublicKeyName = "public_key";
    [BindProperty(Name = PublicKeyName, SupportsGet = true)]
    public string PublicKey { get; set; } = "";

    public const string StateName = "state";
    [BindProperty(Name = StateName, SupportsGet = true)]
    public string State { get; set; } = "";

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
        string publicKey,
        string permissionRequest,
        string clientInfo,
        string state)
    {
        RedirectUri = redirectUri;
        ClientType = clientType;
        ClientId = clientId;
        PublicKey = publicKey;
        PermissionRequest = permissionRequest;
        ClientInfo = clientInfo;
        State = state;
    }
    
    //
    
    public string ToQueryString()
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);
        
        qs[ClientIdName] = ClientId;
        qs[ClientTypeName] = ClientType.ToString();
        qs[ClientInfoName] = ClientInfo;
        qs[RedirectUriName] = RedirectUri;
        qs[PermissionRequestName] = PermissionRequest;
        qs[PublicKeyName] = PublicKey;
        qs[StateName] = State;

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
        
        return new YouAuthAuthorizeRequest(
            clientType: clientType,
            clientId: qs[ClientIdName] ?? string.Empty, 
            clientInfo: qs[ClientInfoName] ?? string.Empty,
            permissionRequest: qs[PermissionRequestName] ?? string.Empty,
            publicKey: qs[PublicKeyName] ?? string.Empty,
            redirectUri: qs[RedirectUriName] ?? string.Empty,
            state: qs[StateName] ?? string.Empty);
    }

    //
    
    public void Validate(string redirectUriHost)
    {
        if (ClientType != ClientType.app && ClientType != ClientType.domain)
        {
            throw new BadRequestException($"Bad or missing {ClientTypeName}: {ClientType}");
        }
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new BadRequestException($"Bad or missing {ClientIdName}");
        }
        if (ClientType == ClientType.domain && ClientId != redirectUriHost)
        {
            // Make it easier to do local app development
            if (redirectUriHost != "localhost")
            {
                throw new BadRequestException($"{ClientIdName} must equal host {redirectUriHost} when {ClientTypeName} is {ClientType.domain}");
            }
        }
        if (ClientType == ClientType.app && string.IsNullOrWhiteSpace(PermissionRequest))
        {
            throw new BadRequestException($"{PermissionRequestName} is required when {ClientTypeName} is {ClientType.app}");
        }
        if (string.IsNullOrWhiteSpace(PublicKey))
        {
            throw new BadRequestException($"Bad or missing {PublicKeyName}");
        }
        if (string.IsNullOrWhiteSpace(RedirectUri))
        {
            throw new BadRequestException($"Bad or missing {RedirectUriName}");
        }
    }
}

public class YouAuthAuthorizeConsentGiven
{
    public const string ReturnUrlName = "return_url";
    public const string ConsentRequirementName = "consent_req";
}
