using System.Text.Json.Serialization;

namespace Odin.Services.LinkPreview.Posts;

public class PrimaryMediaFile
{
    [JsonPropertyName("fileKey")] // this is the payload key
    public string FileKey { get; set; }

    [JsonPropertyName("fileId")] // ignore this (it was pre multi-payload support)
    public string FileId { get; set; }

    [JsonPropertyName("type")] //mime-type
    public string Type { get; set; }
}