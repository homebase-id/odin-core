using Newtonsoft.Json;

namespace Odin.Hosting.PersonMetadata;

public class FrontEndProfile
{
    
    [JsonProperty("name")]
    public string Name { get; set; }
    
    
    [JsonProperty("givenName")]
    public string GiveName { get; set; }
    
    
    [JsonProperty("surname")]
    public string Surname { get; set; }
    
    
    [JsonProperty("image")]
    public string Image { get; set; }
    
    [JsonProperty("bio")]
    public string Bio { get; set; }
}