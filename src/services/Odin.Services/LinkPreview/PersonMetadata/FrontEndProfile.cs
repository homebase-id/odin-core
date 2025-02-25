
using System.Text.Json.Serialization;

namespace Odin.Services.LinkPreview.PersonMetadata;

public class FrontEndProfile
{
    
    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    
    [JsonPropertyName("givenName")]
    public string GiveName { get; set; }
    
    
    [JsonPropertyName("surname")]
    public string Surname { get; set; }
    
    
    [JsonPropertyName("image")]
    public string Image { get; set; }
    
    [JsonPropertyName("bio")]
    public string Bio { get; set; }
}