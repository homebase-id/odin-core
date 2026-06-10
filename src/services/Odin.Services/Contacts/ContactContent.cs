using System.Text.Json.Serialization;

namespace Odin.Services.Contacts;

/// <summary>
/// The contact <b>data</b> stored as <c>AppData.Content</c> on a file in the
/// <see cref="Odin.Services.Drives.SystemDriveConstants.ContactDrive"/>.
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
/// derived live by the client from <c>CircleNetworkService</c>; the legacy <c>source</c> field is
/// tolerated on read but never emitted on write (see <see cref="ContactService"/>).
/// </para>
/// </summary>
public class ContactContent
{
    /// <summary>Optional. A syntactically valid domain. No liveness check is performed.</summary>
    [JsonPropertyName("odinId")]
    public string OdinId { get; set; }

    [JsonPropertyName("name")]
    public ContactName Name { get; set; }

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
