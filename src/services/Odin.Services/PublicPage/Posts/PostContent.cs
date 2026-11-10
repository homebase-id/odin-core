using System.Text.Json;
using System.Text.Json.Serialization;
using Odin.Core.Time;

namespace Odin.Services.PublicPage.Posts;

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
    
    // Marked as ignore since we dont need this field. however I
    // do not want the deserializer to fail due to a missing member
    [JsonIgnore]
    [JsonPropertyName("captionAsRichText")]
    public object CaptionAsRichText { get; set; }
    
    [JsonPropertyName("abstract")]
    public string Abstract { get; set; }

    [JsonPropertyName("body")]
    public object Body { get; set; }
    
    /// <summary>Tiptap copy of the body; only valid while SlateHash matches the stored body</summary>
    [JsonPropertyName("bodyDoc")]
    public BodyDoc BodyDoc { get; set; }

    [JsonPropertyName("slug")] 
    public string Slug { get; set; }
    
    [JsonPropertyName("type")]
    //type: 'Article' | 'Media' | 'Tweet';
    public string Type { get; set; }

    [JsonPropertyName("primaryMediaFile")]
    public PrimaryMediaFile PrimaryMediaFile { get; set; }
    
    /// <summary>Kept as raw JSON so a readingTimeStats in an odd shape never fails the whole post; see <see cref="WordCount"/></summary>
    [JsonPropertyName("readingTimeStats")]
    public JsonElement? ReadingTimeStats { get; set; }

    /// <summary>readingTimeStats.words as a positive whole number, or null when it is missing or not a usable number</summary>
    [JsonIgnore]
    public int? WordCount
    {
        get
        {
            if (ReadingTimeStats is not { ValueKind: JsonValueKind.Object } stats ||
                !stats.TryGetProperty("words", out var words) ||
                words.ValueKind != JsonValueKind.Number ||
                !words.TryGetDouble(out var count) ||
                double.IsNaN(count) || count < 1 || count > int.MaxValue)
            {
                return null;
            }

            return (int)count;
        }
    }

    [JsonIgnore]
    public UnixTimeUtc? UserDate { get; set; }
    
}

public class BodyDoc
{
    [JsonPropertyName("format")]
    public string Format { get; set; }

    [JsonPropertyName("doc")]
    public JsonElement? Doc { get; set; }

    /// <summary>Lowercase hex SHA-256 of the UTF-8 JSON text of the body when this doc was written</summary>
    [JsonPropertyName("slateHash")]
    public string SlateHash { get; set; }
}
