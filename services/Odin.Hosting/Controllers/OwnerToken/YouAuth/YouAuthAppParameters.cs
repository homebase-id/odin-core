using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Web;
using Odin.Hosting.ApiExceptions.Client;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth;

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
    [Required(ErrorMessage = $"{AppOriginName} is required")]
    public string AppOrigin { get; set; } = "";

    public const string ClientFriendlyName = "fn";
    [JsonPropertyName(ClientFriendlyName)]
    [Required(ErrorMessage = $"{ClientFriendlyName} is required")]
    public string ClientFriendly { get; set; } = "";

    public const string DrivesParamName = "d";
    [JsonPropertyName(DrivesParamName)]
    [Required(ErrorMessage = $"{DrivesParamName} is required")]
    public string DrivesParam { get; set; } = "";
    
    public const string ReturnName = "return";
    [JsonPropertyName(ReturnName)]
    public string Return { get; set; } = "";

    //

    public YouAuthAppParameters()
    {
        // Empty on purpose. Needed by controller.
    }

    //

    private YouAuthAppParameters(
        string appName,
        string appOrigin,
        string appId,
        string clientFriendly,
        string drivesParam,
        string @return)
    {
        AppId = appId;
        AppName = appName;
        AppOrigin = appOrigin;
        ClientFriendly = clientFriendly;
        DrivesParam = drivesParam;
        Return = @return;
    }


    public string ToQueryString()
    {
        var qs = HttpUtility.ParseQueryString(string.Empty);

        qs[AppIdName] = AppId;
        qs[AppNameName] = AppName;
        qs[AppOriginName] = AppOrigin;
        qs[ClientFriendlyName] = ClientFriendly;
        qs[DrivesParamName] = DrivesParam;
        qs[ReturnName] = Return;

        return qs.ToString() ?? string.Empty;
    }

    //

    public static YouAuthAppParameters FromQueryString(string queryString)
    {
        var qs = HttpUtility.ParseQueryString(queryString);

        return new YouAuthAppParameters(
            appId: qs[AppIdName] ?? string.Empty,
            appName: qs[AppNameName] ?? string.Empty,
            appOrigin: qs[AppOriginName] ?? string.Empty,
            clientFriendly: qs[ClientFriendlyName] ?? string.Empty,
            drivesParam: qs[DrivesParamName] ?? string.Empty,
            @return: qs[ReturnName] ?? string.Empty);
    }

    //

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AppId))
        {
            throw new BadRequestException($"{AppIdName} is required");
        }
        if (string.IsNullOrWhiteSpace(AppName))
        {
            throw new BadRequestException($"{AppNameName} is required");
        }
        if (string.IsNullOrWhiteSpace(AppOrigin))
        {
            throw new BadRequestException($"{AppOriginName} is required");
        }
        if (string.IsNullOrWhiteSpace(ClientFriendly))
        {
            throw new BadRequestException($"{ClientFriendlyName} is required");
        }
        if (string.IsNullOrWhiteSpace(DrivesParam))
        {
            throw new BadRequestException($"{DrivesParamName} is required");
        }
    }

    //

}
