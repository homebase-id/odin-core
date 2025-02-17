using System;
using Newtonsoft.Json;

namespace Odin.Services.LinkPreview.Posts;

// Modeled after the Article and PostContent in odin-js
// I only pulled in what is needed for link preview

public enum PostType
{
    Article,
    Media,
    Tweet
}

public class PostContent
{
    public bool IsPostType(PostType expectedType)
    {
        return this.Type.Equals(expectedType.ToString(), StringComparison.InvariantCultureIgnoreCase);
    }

    [JsonProperty("id")] public string Id { get; set; }

    [JsonProperty("channelId")] public string ChannelId { get; set; }

    [JsonProperty("caption")] public string Caption { get; set; }

    [JsonProperty("captionAsRichText")]
    //captionAsRichText?: RichText;
    public string CaptionAsRichText { get; set; }

    [JsonProperty("abstract")] public string Abstract { get; set; }

    [JsonProperty("slug")] public string Slug { get; set; }
    

    [JsonProperty("type")]
    //type: 'Article' | 'Media' | 'Tweet';
    public string Type { get; set; }

    [JsonProperty("primaryMediaFile")]
    //primaryMediaFile?: PrimaryMediaFile;
    public PrimaryMediaFile PrimaryMediaFile { get; set; }
}

public class PrimaryMediaFile
{
    [JsonProperty("fileKey")] // this is the payload key
    public string FileKey { get; set; }

    [JsonProperty("fileId")] // ignore this (it was pre multi-payload support)
    public string FileId { get; set; }

    [JsonProperty("type")] //mime-type
    public string Type { get; set; }
}

// export interface PrimaryMediaFile {
//      fileKey: string;
//      fileId: string  // ignore this (it was pre multi-payload support)
//      type: string; // mime-type
//    }
//
// export interface PostContent {
//     channelId: string;
//     reactAccess?: ReactAccess;
//     isCollaborative?: boolean; // A collaborative post; => Anyone with access can edit it; (Only supported on collaborative channels)
//
//     caption: string;
//     slug: string;
//
//     /**
//      * @deprecated Use fileMetadata.originalAuthor instead
//      */
//     authorOdinId?: string;
//     embeddedPost?: EmbeddedPost;
//
//     // For posts from external sources
//     sourceUrl?: string;
// }