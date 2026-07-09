using System.Text.Json.Serialization;

namespace Odin.Services.PublicPage.PersonMetadata.SchemaDotOrg;

public class OrganizationSchema
{
    [JsonPropertyName("@type")]
    public string Type { get; } = "Organization";

    [JsonPropertyName("name")]
    public string Name { get; set; }
}