using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Odin.Core.Services.Authentication.YouAuth;

namespace Odin.Hosting.Controllers.OwnerToken.YouAuth;

#nullable enable

public class YouAuthTokenRequest
{
    [JsonPropertyName("code")]
    [Required(ErrorMessage = "code is required")]
    public string Code { get; set; } = "";
    
    [JsonPropertyName("code_verifier")]
    [Required(ErrorMessage = "code_verifier is required")]
    public string CodeVerifier { get; set; } = "";
}