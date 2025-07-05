
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Odin.Services.LinkPreview.PersonMetadata;

public class FrontEndProfile
{
    
    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    
    [JsonPropertyName("givenName")]
    public string GiveName { get; set; }
    
    
    [JsonPropertyName("familyName")]
    public string FamilyName { get; set; }
    
    [JsonPropertyName("status")]
    public string Status { get; set; }
    
    [JsonPropertyName("image")]
    public string Image { get; set; }
    
    [JsonPropertyName("bioSummary")]
    public string BioSummary { get; set; }

    [JsonPropertyName("bio")]
    public string Bio { get; set; }
    
    [JsonPropertyName("links")]
    public List<FrontEndProfileLink> Links { get; set; }
    
    [JsonPropertyName("sameAs")]
    public List<FrontEndProfileLink> SameAs { get; set; }
}

public class FrontEndProfileLink
{
    [JsonPropertyName("type")]
    public string Type { get; set; }
    
    [JsonPropertyName("url")]
    public string Url { get; set; }

}