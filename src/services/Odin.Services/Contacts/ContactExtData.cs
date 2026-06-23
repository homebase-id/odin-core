using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Odin.Core;

namespace Odin.Services.Contacts;

/// <summary>
/// Peer-sourced "extended" contact data too large to keep in the 10 KB content blob — the long,
/// rich-text profile attributes (Experience, Bio, …). Stored as the <c>ext_data</c> payload on the
/// contact file, encrypted at rest under the file key header with its own IV — the same mechanism as
/// the <see cref="ContactMergeLog"/> / profile-image payloads.
///
/// <para>
/// <b>Always peer data, carried verbatim.</b> There are no owner-owned fields, so a merge replaces the
/// payload <b>wholesale</b>. Each attribute's <c>data</c> object is stored exactly as the peer
/// published it (rich-text structure preserved), keyed by the attribute's <b>type id</b> in the same
/// string form the peer uses (e.g. <c>"65635623682c2fadd2767d424f53690f"</c>). The server neither
/// parses nor reshapes the values, so new attribute types/fields ride along without code changes, and
/// the client renders them with the same renderers it uses for profile attributes.
/// </para>
/// </summary>
public class ContactExtData
{
    public const string PayloadKey = "ext_data"; // matches ^[a-z0-9_]{8,10}$
    public const string ContentType = "application/json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Peer profile attributes, keyed by attribute type id (verbatim string, e.g.
    /// <c>"65635623682c2fadd2767d424f53690f"</c>). Each value is that attribute's <c>data</c> object
    /// exactly as published — opaque to the server.
    /// </summary>
    [JsonPropertyName("attributes")]
    public Dictionary<string, JsonElement> Attributes { get; set; }

    /// <summary>True when no attribute is carried — used to skip writing (and to avoid clearing) the payload.</summary>
    [JsonIgnore]
    public bool IsEmpty => Attributes == null || Attributes.Count == 0;

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, SerializerOptions);

    public static ContactExtData Deserialize(byte[] json) =>
        json is { Length: > 0 }
            ? JsonSerializer.Deserialize<ContactExtData>(json.ToStringFromUtf8Bytes(), SerializerOptions)
            : null;
}
