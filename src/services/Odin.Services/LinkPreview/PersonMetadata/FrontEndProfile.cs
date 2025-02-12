using Newtonsoft.Json;

namespace Odin.Hosting.PersonMetadata;

public class FrontEndProfile
{
    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("image")]
    public string Image { get; set; }
    
    [JsonProperty("bio")]
    public string Bio { get; set; }
}