using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Odin.Core.Exceptions.Client;
using Odin.Core.Services.Authentication.YouAuth;
using NotImplementedException = System.NotImplementedException;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth;

#nullable enable

public class YouAuthTokenRequest
{
    public const string CodeName = "code";
    [JsonPropertyName(CodeName)]
    [Required(ErrorMessage = $"{CodeName} is required")]
    public string Code { get; set; } = "";

    public const string CodeVerifierName = "code_verifier";
    [JsonPropertyName(CodeVerifierName)]
    [Required(ErrorMessage = $"{CodeVerifierName} is required")]
    public string CodeVerifier { get; set; } = "";

    public const string TokenDeliveryOptionName = "token_delivery_option";
    [JsonPropertyName(TokenDeliveryOptionName)]
    [Required(ErrorMessage = $"{TokenDeliveryOptionName} is required")]
    public TokenDeliveryOption TokenDeliveryOption { get; set; } = TokenDeliveryOption.unknown;

    //

    public void Validate()
    {
        if (TokenDeliveryOption != TokenDeliveryOption.json && TokenDeliveryOption != TokenDeliveryOption.cookie)
        {
            throw new BadRequestException($"{TokenDeliveryOptionName} is invalid {TokenDeliveryOption}");
        }
    }
}