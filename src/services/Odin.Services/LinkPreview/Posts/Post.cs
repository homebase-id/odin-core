using System.Text.Json.Serialization;

namespace Odin.Services.LinkPreview.Posts;

// Modeled after the Article and PostContent in odin-js
// I only pulled in what is needed for link preview


public class PostContent
{
    [JsonPropertyName("id")] 
    public string Id { get; set; }

    [JsonPropertyName("channelId")] 
    public string ChannelId { get; set; }

    [JsonPropertyName("caption")] 
    public string Caption { get; set; }

    [JsonPropertyName("captionAsRichText")]
    //captionAsRichText?: RichText;
    public string CaptionAsRichText { get; set; }

    [JsonPropertyName("abstract")]
    public string Abstract { get; set; }

    [JsonPropertyName("slug")] 
    public string Slug { get; set; }
    

    [JsonPropertyName("type")]
    //type: 'Article' | 'Media' | 'Tweet';
    public string Type { get; set; }

    [JsonPropertyName("primaryMediaFile")]
    //primaryMediaFile?: PrimaryMediaFile;
    public PrimaryMediaFile PrimaryMediaFile { get; set; }
}

public class PrimaryMediaFile
{
    [JsonPropertyName("fileKey")] // this is the payload key
    public string FileKey { get; set; }

    [JsonPropertyName("fileId")] // ignore this (it was pre multi-payload support)
    public string FileId { get; set; }

    [JsonPropertyName("type")] //mime-type
    public string Type { get; set; }
}