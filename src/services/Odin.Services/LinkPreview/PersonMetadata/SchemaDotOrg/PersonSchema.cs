using System.Collections.Generic;
using System.Text.Json.Serialization;


namespace Odin.Services.LinkPreview.PersonMetadata.SchemaDotOrg;

public class PersonSchema
{
    [JsonPropertyName("@context")]
    public string Context { get; } = "https://schema.org";

    [JsonPropertyName("@type")]
    public string Type { get; } = "Person";

    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    [JsonPropertyName("givenName")]
    public string GivenName { get; set; }

    [JsonPropertyName("familyName")]
    public string FamilyName { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }
    
    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("birthDate")]
    public string BirthDate { get; set; } // Use ISO 8601 format: YYYY-MM-DD

    [JsonPropertyName("jobTitle")]
    public string JobTitle { get; set; }

    [JsonPropertyName("image")]
    public string Image { get; set; }
    
    [JsonPropertyName("worksFor")]
    public OrganizationSchema WorksFor { get; set; }

    [JsonPropertyName("address")]
    public AddressSchema Address { get; set; }

    [JsonPropertyName("identifier")]
    public List<string> Identifier { get; set; } // Social media or reference URLs

    [JsonPropertyName("sameAs")]
    public List<string> SameAs { get; set; } // Social media or reference URLs
    
}