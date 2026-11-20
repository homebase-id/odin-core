using Newtonsoft.Json;

namespace Odin.Services.PublicPage.Posts;

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

    /// <summary>Alias of the channel's drive; set when the channel is loaded, not part of the stored definition</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Guid DriveAlias { get; set; }
    
    // [JsonIgnore]
    // [JsonProperty("templateId")]
    // public int? TemplateId { get; init; }
    
    // [JsonIgnore]
    // [JsonProperty("isCollaborative")]
    // public string IsCollaborative { get; init; }
}