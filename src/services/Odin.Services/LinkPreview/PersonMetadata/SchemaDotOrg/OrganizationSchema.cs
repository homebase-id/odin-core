using System.Text.Json.Serialization;

namespace Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;

public class OrganizationSchema
{
    [JsonPropertyName("@type")]
    public string Type { get; } = "Organization";

    [JsonPropertyName("name")]
    public string Name { get; set; }
}