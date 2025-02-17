using System.Collections.Generic;
using Newtonsoft.Json;

namespace Odin.Hosting.PersonMetadata.SchemaDotOrg;

public class PersonSchema
{
    [JsonProperty("@context")]
    public string Context { get; } = "https://schema.org";

    [JsonProperty("@type")]
    public string Type { get; } = "Person";

    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("givenName")]
    public string GivenName { get; set; }

    [JsonProperty("familyName")]
    public string FamilyName { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }
    
    [JsonProperty("email")]
    public string Email { get; set; }

    [JsonProperty("birthDate")]
    public string BirthDate { get; set; } // Use ISO 8601 format: YYYY-MM-DD

    [JsonProperty("jobTitle")]
    public string JobTitle { get; set; }

    [JsonProperty("image")]
    public string Image { get; set; }
    
    [JsonProperty("worksFor")]
    public OrganizationSchema WorksFor { get; set; }

    [JsonProperty("address")]
    public AddressSchema Address { get; set; }

    [JsonProperty("sameAs")]
    public List<string> SameAs { get; set; } // Social media or reference URLs
    
}