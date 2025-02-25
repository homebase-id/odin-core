using Newtonsoft.Json;

namespace Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;

public class AddressSchema
{
    [JsonProperty("@type")]
    public string Type { get; } = "PostalAddress";

    [JsonProperty("streetAddress")]
    public string StreetAddress { get; set; }

    [JsonProperty("addressLocality")]
    public string AddressLocality { get; set; }

    [JsonProperty("addressRegion")]
    public string AddressRegion { get; set; }

    [JsonProperty("postalCode")]
    public string PostalCode { get; set; }

    [JsonProperty("addressCountry")]
    public string AddressCountry { get; set; }
}