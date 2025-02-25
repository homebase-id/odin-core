using System.Text.Json.Serialization;

namespace Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;

public class AddressSchema
{
    [JsonPropertyName("@type")]
    public string Type { get; } = "PostalAddress";

    [JsonPropertyName("streetAddress")]
    public string StreetAddress { get; set; }

    [JsonPropertyName("addressLocality")]
    public string AddressLocality { get; set; }

    [JsonPropertyName("addressRegion")]
    public string AddressRegion { get; set; }

    [JsonPropertyName("postalCode")]
    public string PostalCode { get; set; }

    [JsonPropertyName("addressCountry")]
    public string AddressCountry { get; set; }
}