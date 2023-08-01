using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth;

public class YouAuthAppParameters
{
    public const string AppNameName = "n";
    [JsonPropertyName(AppNameName)]
    [Required(ErrorMessage = $"{AppNameName} is required")]
    public string AppName { get; set; } = "";

    public const string AppOriginName = "o";
    [JsonPropertyName(AppOriginName)]
    [Required(ErrorMessage = $"{AppOriginName} is required")]
    public string AppOrigin { get; set; } = "";

    public const string AppIdName = "appId";
    [JsonPropertyName(AppIdName)]
    [Required(ErrorMessage = $"{AppIdName} is required")]
    public string AppId { get; set; } = "";

    public const string ClientFriendlyName = "fn";
    [JsonPropertyName(ClientFriendlyName)]
    [Required(ErrorMessage = $"{ClientFriendlyName} is required")]
    public string ClientFriendly { get; set; } = "";

    public const string ReturnName = "return";
    [JsonPropertyName(ReturnName)]
    [Required(ErrorMessage = $"{ReturnName} is required")]
    public string Return { get; set; } = "";

    public const string DrivesParamName = "d";
    [JsonPropertyName(DrivesParamName)]
    [Required(ErrorMessage = $"{DrivesParamName} is required")]
    public string DrivesParam { get; set; } = "";

    public const string PkName = "pk";
    [JsonPropertyName(PkName)]
    [Required(ErrorMessage = $"{PkName} is required")]
    public string Pk { get; set; } = "";
}
