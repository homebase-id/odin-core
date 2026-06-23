using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Odin.Core;

namespace Odin.Services.Contacts;

/// <summary>
/// The <b>bulk</b> per-app data tier, stored as the <c>appextdata</c> payload on the contact file
/// (encrypted at rest under the file key header with its own IV — the same mechanism as
/// <see cref="ContactMergeLog"/> / <see cref="ContactExtData"/> / the profile-image payload). It holds
/// app-authored blobs too large for the size-capped per-app slot that rides inline in the contact JSON
/// (<see cref="ContactContent.AppData"/>); the inline slot serves the contacts list query, this payload
/// is fetched on demand only when a single contact is opened.
///
/// <para>
/// <b>App-owned, per-app merged — not <see cref="ContactExtData"/>.</b> <see cref="ContactExtData"/> is
/// peer-owned and replaced <i>wholesale</i> on every enrichment/merge, so app data parked there would be
/// clobbered by the next peer publish. This payload is instead keyed by the calling app's id and merged
/// one slot at a time (read-modify-write), and is carried forward untouched on peer enrichment and core
/// contact writes. Each value is the app's blob stored <b>verbatim</b> — the server never parses it.
/// </para>
/// </summary>
public class ContactAppData
{
    public const string PayloadKey = "appextdata"; // matches ^[a-z0-9_]{8,10}$
    public const string ContentType = "application/json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Per-app bulk blobs, keyed by the app's id (GUID string). Each value is that app's opaque JSON
    /// string, stored exactly as the app sent it.
    /// </summary>
    [JsonPropertyName("appData")]
    public Dictionary<string, string> AppData { get; set; }

    /// <summary>True when no app blob is carried — used to drop (rather than store) an empty payload.</summary>
    [JsonIgnore]
    public bool IsEmpty => AppData == null || AppData.Count == 0;

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, SerializerOptions);

    public static ContactAppData Deserialize(byte[] json) =>
        json is { Length: > 0 }
            ? JsonSerializer.Deserialize<ContactAppData>(json.ToStringFromUtf8Bytes(), SerializerOptions)
            : null;
}
