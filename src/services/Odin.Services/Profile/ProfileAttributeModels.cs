using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Odin.Services.Profile;

/// <summary>
/// The visibility of a profile attribute, mapped to the file's access-control list. Anonymous and
/// Authenticated attributes are stored plaintext (publicly/authenticated-readable); Connected and Owner
/// attributes are encrypted (matching odin-js <c>encrypt = !(Anonymous || Authenticated)</c>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProfileAttributeVisibility
{
    Anonymous,
    Authenticated,
    Connected,
    Owner
}

/// <summary>
/// Request to create or edit a built-in profile attribute. The server derives the standard-profile
/// <c>profileId</c> and the per-type <c>sectionId</c>; the caller supplies a built-in
/// <see cref="Type"/> and the full <see cref="Data"/> field set.
/// </summary>
public sealed class SetProfileAttributeRequest
{
    /// <summary>The built-in attribute type id (a value from <c>BuiltInProfileAttributes</c>).</summary>
    public Guid Type { get; set; }

    /// <summary>
    /// The attribute id (the file's unique id). When it matches an existing attribute this edits it;
    /// when null or unknown a new attribute is created with a server-generated id.
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>Authored rank; lower is preferred (odin-js attribute priority). Defaults to 0.</summary>
    public int? Priority { get; set; }

    /// <summary>The attribute's visibility / access-control level. Defaults to <see cref="ProfileAttributeVisibility.Anonymous"/>.</summary>
    public ProfileAttributeVisibility Visibility { get; set; } = ProfileAttributeVisibility.Anonymous;

    /// <summary>
    /// The attribute's field values (odin-js attribute <c>data</c>), written wholesale. The caller supplies
    /// the complete desired set; for the Name attribute the server fills in <c>displayName</c>.
    /// </summary>
    public Dictionary<string, object> Data { get; set; }

    /// <summary>The expected current version tag; required when editing an existing attribute (optimistic concurrency).</summary>
    public Guid? ExpectedVersionTag { get; set; }
}

/// <summary>
/// Request to create or edit the Photo attribute (see <c>ProfileAttributeService.SetPhotoAttributeAsync</c>).
/// Unlike <see cref="SetProfileAttributeRequest"/>, this carries the image + its pre-generated thumbnails
/// as a payload rather than header-only <c>Data</c> — the server does not resize images, so
/// <see cref="Thumbnails"/> must already be sized the way the caller wants them stored (matching odin-js,
/// which generates them client-side before upload).
/// </summary>
public sealed class SetPhotoAttributeRequest
{
    /// <summary>
    /// The attribute id (the file's unique id). When it matches an existing Photo attribute this edits it
    /// (replacing the image + thumbnails wholesale); when null or unknown a new attribute is created.
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>Authored rank; lower is preferred (odin-js attribute priority). Defaults to 0.</summary>
    public int? Priority { get; set; }

    /// <summary>
    /// The attribute's visibility / access-control level. Defaults to <see cref="ProfileAttributeVisibility.Anonymous"/>.
    /// Callers can hold multiple Photo attributes at once (e.g. a public avatar and a higher-resolution
    /// Connected-only one), each with its own <see cref="Id"/> and visibility.
    /// </summary>
    public ProfileAttributeVisibility Visibility { get; set; } = ProfileAttributeVisibility.Anonymous;

    /// <summary>The expected current version tag; required when editing an existing attribute (optimistic concurrency).</summary>
    public Guid? ExpectedVersionTag { get; set; }

    /// <summary>MIME type of the full-size image, e.g. <c>image/webp</c>.</summary>
    public string ContentType { get; set; }

    /// <summary>The full-size image bytes.</summary>
    public byte[] Content { get; set; }

    /// <summary>Pre-generated thumbnail renditions of <see cref="Content"/>, caller-supplied.</summary>
    public List<ProfilePhotoThumbnail> Thumbnails { get; set; } = new();
}

/// <summary>A caller-generated thumbnail rendition for a Photo attribute's image.</summary>
public sealed class ProfilePhotoThumbnail
{
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public string ContentType { get; set; }
    public byte[] Content { get; set; }
}

public enum ProfileAttributeWriteOutcome
{
    Created,
    Updated,
    VersionConflict
}

public sealed class ProfileAttributeWriteResult
{
    public ProfileAttributeWriteOutcome Outcome { get; init; }

    /// <summary>The attribute id (the file's unique id) that was created or edited.</summary>
    public Guid Id { get; init; }

    /// <summary>The attribute's version tag after the write (or the current tag on a version conflict).</summary>
    public Guid VersionTag { get; init; }
}

/// <summary>
/// The on-drive JSON shape of a profile attribute (odin-js <c>Attribute</c>). Guid fields are written in
/// the no-dash form odin-js uses (<c>toGuidId</c> / <c>ToString("N")</c>).
/// </summary>
public sealed class ProfileAttributeContent
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("profileId")]
    public string ProfileId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("sectionId")]
    public string SectionId { get; set; }

    [JsonPropertyName("data")]
    public Dictionary<string, object> Data { get; set; }
}
