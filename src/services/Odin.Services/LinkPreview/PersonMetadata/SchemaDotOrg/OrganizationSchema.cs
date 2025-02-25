
using Newtonsoft.Json;

namespace Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;

public class OrganizationSchema
{
    [JsonProperty("@type")]
    public string Type { get; } = "Organization";

    [JsonProperty("name")]
    public string Name { get; set; }
}