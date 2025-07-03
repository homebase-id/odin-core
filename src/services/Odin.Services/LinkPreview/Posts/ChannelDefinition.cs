using Newtonsoft.Json;

namespace Odin.Services.LinkPreview.Posts;

public class ChannelDefinition
{
    /*
     copied from odin-js
     export interface ChannelDefinition {
           name: string;
           slug: string;
           description: string;
           showOnHomePage: boolean;
           templateId?: number;
           isCollaborative?: boolean;
       }
    */
    
    [JsonProperty("name")]
    public string Name { get; init; }
    
    [JsonProperty("slug")]
    public string Slug { get; init; }
    
    [JsonProperty("description")]
    public string Description { get; init; }
    
    [JsonProperty("showOnHomePage")]
    public bool ShowOnHomePage { get; init; }
    
    [JsonIgnore]
    [JsonProperty("templateId")]
    public string TemplateId { get; init; }
    
    [JsonIgnore]
    [JsonProperty("isCollaborative")]
    public string IsCollaborative { get; init; }
}