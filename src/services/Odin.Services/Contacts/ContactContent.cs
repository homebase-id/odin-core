using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Odin.Services.Contacts;

/// <summary>
/// The subset of contact data that can be <b>sourced from a peer identity</b> — either pulled from the
/// peer's profile (<see cref="ContactEnrichmentService"/>) or shared by the peer over the connection
/// flow (<c>ContactRequestData.Contact</c>). It deliberately contains <b>only</b> peer-owned fields, so
/// a sync/enrichment can never reach the owner-only fields that live on the derived
/// <see cref="ContactContent"/> — there is nothing here to carry them.
///
/// <para>
/// This shape is byte-compatible with what odin-js writes today
/// (<c>packages/libs/js-lib/src/network/contact/ContactTypes.ts</c>); it is serialized
/// <b>camelCase</b> so existing files stay readable by both sides. Property names are pinned
/// explicitly via <see cref="JsonPropertyNameAttribute"/> — do not rely on the serializer default.
/// </para>
///
/// <para>
/// It holds contact data only — never connection status. Connected/blocked/introduced/pending is
/// derived live by the client from <c>CircleNetworkService</c>. The <see cref="Source"/> field marks
/// the origin of the data (matching odin-js) and is round-tripped verbatim.
/// </para>
/// </summary>
public class PeerContactContent
{
    /// <summary>Optional. A syntactically valid domain. No liveness check is performed.</summary>
    [JsonPropertyName("odinId")]
    public string OdinId { get; set; }

    [JsonPropertyName("name")]
    public ContactName Name { get; set; }

    /// <summary>Origin of the contact data (odin-js): <c>contact</c> | <c>public</c> | <c>user</c>.</summary>
    /// <remarks>Declared after Name to match the odin-js field order (odinId, name, source, …).</remarks>
    [JsonPropertyName("source")]
    public string Source { get; set; }

    [JsonPropertyName("location")]
    public ContactLocation Location { get; set; }

    /// <summary>Single-valued, matching odin-js.</summary>
    [JsonPropertyName("phone")]
    public ContactPhone Phone { get; set; }

    /// <summary>Single-valued, matching odin-js.</summary>
    [JsonPropertyName("email")]
    public ContactEmail Email { get; set; }

    [JsonPropertyName("birthday")]
    public ContactBirthday Birthday { get; set; }

    /// <summary>
    /// A short bio / tagline (~160 chars), small enough to live in the content header. Sourced from the
    /// peer's <b>"Short bio"</b> attribute (type <c>1d89f51a-…</c>, data field <c>short_bio</c>) — do
    /// <b>not</b> confuse it with the rich-text <b>"Bio"</b> attribute, whose <c>short_bio</c> is an
    /// array carried verbatim in the ext_data payload.
    /// </summary>
    [JsonPropertyName("shortBio")]
    public string ShortBio { get; set; }

    /// <summary>Peer-authored nickname / preferred name. Peer-sourced — not an owner-only label.</summary>
    [JsonPropertyName("nickname")]
    public string Nickname { get; set; }

    /// <summary>
    /// Peer-authored status text / tagline. <b>Not</b> connection status: connected/blocked/introduced/
    /// pending is still derived live by the client (see class remarks) and is never stored here.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; }

    /// <summary>Peer's personal link / website. Stored as the bare value; the client renders the URL.</summary>
    [JsonPropertyName("link")]
    public string Link { get; set; }

    /// <summary>
    /// Peer's social/game handles, keyed by the attribute's GUID string (as stored upstream — no short-code
    /// aliasing) with the handle as the value. Bundled into one object so the attribute envelope is paid
    /// once; omitted entirely when empty.
    /// </summary>
    [JsonPropertyName("social")]
    public Dictionary<string, string> Social { get; set; }
}

/// <summary>
/// The full contact <b>data</b> record stored as <c>AppData.Content</c> on a file in the
/// <see cref="Odin.Services.Drives.SystemDriveConstants.ContactDrive"/>. It is the peer-sourceable
/// <see cref="PeerContactContent"/> plus the <b>owner-owned</b> fields below, which are set only by the
/// owner via the contacts API and are <b>never</b> sourced from a peer. Because the enrichment /
/// connection-flow paths operate on <see cref="PeerContactContent"/>, they cannot carry these fields,
/// and the merge always preserves the stored value — a sync can never overwrite them.
/// </summary>
public class ContactContent : PeerContactContent
{
    /// <summary>
    /// Per-app opaque data blobs, keyed by the calling app's id (GUID string). Each value is a small,
    /// app-authored JSON string stored <b>verbatim</b> — the server never parses it. <b>Owner/app-owned</b>
    /// and never sourced from a peer: enrichment and the connection flow operate on
    /// <see cref="PeerContactContent"/>, which cannot carry this, so a sync can never overwrite it. Written
    /// only via the dedicated app-data endpoint (one app's slot at a time, resolved from the token); the
    /// core contact write preserves it untouched. Each entry is size-capped
    /// (<see cref="ContactService.AppDataBlobMaxBytes"/>); bulk data belongs in a payload, not here.
    /// </summary>
    [JsonPropertyName("appData")]
    public Dictionary<string, string> AppData { get; set; }
}

public class ContactName
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; }

    [JsonPropertyName("givenName")]
    public string GivenName { get; set; }

    [JsonPropertyName("additionalName")]
    public string AdditionalName { get; set; }

    [JsonPropertyName("surname")]
    public string Surname { get; set; }
}

public class ContactLocation
{
    /// <summary>Street address line 1 (odin-js AddressFields <c>address1</c>).</summary>
    [JsonPropertyName("addressLine1")]
    public string AddressLine1 { get; set; }

    /// <summary>Street address line 2 (odin-js AddressFields <c>address2</c>).</summary>
    [JsonPropertyName("addressLine2")]
    public string AddressLine2 { get; set; }

    [JsonPropertyName("postcode")]
    public string Postcode { get; set; }

    [JsonPropertyName("city")]
    public string City { get; set; }

    [JsonPropertyName("country")]
    public string Country { get; set; }
}

public class ContactPhone
{
    [JsonPropertyName("number")]
    public string Number { get; set; }
}

public class ContactEmail
{
    [JsonPropertyName("email")]
    public string Email { get; set; }
}

public class ContactBirthday
{
    /// <summary>Free-form string, stored verbatim (no fixed format).</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; }
}