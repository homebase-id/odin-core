using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Web;

// SEB:TODO
// This is a partial copy of the class in namespace Odin.Hosting.Controllers.OwnerToken.YouAuth.
// It should be in a shared lib instead.

namespace YouAuthClientReferenceImplementation.Models;

public class YouAuthAppParameters
{
    public const string AppIdName = "appId";
    [JsonPropertyName(AppIdName)]
    [Required(ErrorMessage = $"{AppIdName} is required")]
    public string AppId { get; set; } = "";

    public const string AppNameName = "n";
    [JsonPropertyName(AppNameName)]
    [Required(ErrorMessage = $"{AppNameName} is required")]
    public string AppName { get; set; } = "";

    public const string AppOriginName = "o";
    [JsonPropertyName(AppOriginName)]
    public string AppOrigin { get; set; } = "";

    public const string ClientFriendlyName = "fn";
    [JsonPropertyName(ClientFriendlyName)]
    [Required(ErrorMessage = $"{ClientFriendlyName} is required")]
    public string ClientFriendly { get; set; } = "";

    public const string DrivesParamName = "d";
    [JsonPropertyName(DrivesParamName)]
    [Required(ErrorMessage = $"{DrivesParamName} is required")]
    public string DrivesParam { get; set; } = "";

    public const string CircleDrivesParamName = "cd";
    [JsonPropertyName(CircleDrivesParamName)]
    public string CircleDrivesParam { get; set; } = "";

    public const string PermissionParamName = "p";
    [JsonPropertyName(PermissionParamName)]
    public string PermissionParam { get; set; } = "";

    public const string ReturnName = "return";
    [JsonPropertyName(ReturnName)]
    public string Return { get; set; } = "";

    public const string CancelName = "cancel";
    [JsonPropertyName(CancelName)]
    public string Cancel { get; set; } = "";


    //

    public YouAuthAppParameters()
    {
        // Empty on purpose. Needed by controller.
    }

    //

    public string ToQueryString()
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);

        qs[AppIdName] = AppId;
        qs[AppNameName] = AppName;
        qs[AppOriginName] = AppOrigin;
        qs[ClientFriendlyName] = ClientFriendly;
        qs[DrivesParamName] = DrivesParam;
        qs[CircleDrivesParamName] = CircleDrivesParam;
        qs[PermissionParamName] = PermissionParam;
        qs[ReturnName] = Return;
        qs[CancelName] = Cancel;

        return qs.ToString() ?? string.Empty;
    }


}
