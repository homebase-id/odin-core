using System.Web;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.YouAuth;

// SEB:TODO
// This is a partial copy of the class in namespace Odin.Hosting.Controllers.OwnerToken.YouAuth.
// It should be in a shared lib instead.

namespace YouAuthClientReferenceImplementation.Models;

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

}