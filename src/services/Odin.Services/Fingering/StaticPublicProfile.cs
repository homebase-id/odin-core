using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Odin.Services.Fingering;

#nullable enable

// C# representation of the tenant file static/public_profile.json
public class StaticPublicProfile
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("email")]
    public List<EmailInfo>? Email { get; set; }

    [JsonPropertyName("links")]
    public List<LinkInfo>? Links { get; set; }

    public class EmailInfo
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }

    public class LinkInfo
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
