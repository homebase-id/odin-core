using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Odin.Core;
using Odin.Core.Time;

namespace Odin.Services.Contacts;

/// <summary>
/// Who triggered a merge — recorded on each <see cref="ContactMergeLogEntry"/>.
/// </summary>
public enum ContactMergeSource
{
    Api,
    Enrichment
}

/// <summary>
/// One append-only entry capturing the field values that a merge <b>overwrote</b> (old values only;
/// the new values live in the contact's <c>Content</c>).
/// </summary>
public class ContactMergeLogEntry
{
    /// <summary>When the overwrite happened (UnixTimeUtc milliseconds).</summary>
    [JsonPropertyName("at")]
    public long At { get; set; }

    /// <summary><c>"api"</c> or <c>"enrichment"</c>.</summary>
    [JsonPropertyName("by")]
    public string By { get; set; }

    /// <summary>Map of <c>jsonPath → old value</c> for each overwritten field.</summary>
    [JsonPropertyName("changes")]
    public Dictionary<string, string> Changes { get; set; }
}

/// <summary>
/// The append-only merge history stored as the <c>merge_log</c> payload on a contact file (encrypted at
/// rest under the file key header, like <c>Content</c>). It records only fields whose prior non-empty
/// value was replaced by a different value — first-time fills and no-op writes are not logged. The log
/// is bounded to <see cref="MaxEntries"/>; the oldest entries are dropped past the cap.
/// </summary>
public static class ContactMergeLog
{
    public const string PayloadKey = "merge_log"; // matches ^[a-z0-9_]{8,10}$
    public const string ContentType = "application/json";
    public const int MaxEntries = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Returns the fields being overwritten: keys present and non-empty in <b>both</b> existing and
    /// incoming where the values differ (jsonPath → old value). An empty result means "nothing to log".
    /// </summary>
    public static Dictionary<string, string> ComputeOverwrites(PeerContactContent existing, PeerContactContent incoming)
    {
        var ex = Flatten(existing);
        var inc = Flatten(incoming);

        var changes = new Dictionary<string, string>();
        foreach (var (path, incomingValue) in inc)
        {
            // Logged only when the prior value was non-empty (present in `ex`) and is being replaced by
            // a different non-empty value. (An incoming empty string clearing a field is not captured —
            // Flatten drops empties; acceptable for an audit log.)
            if (ex.TryGetValue(path, out var oldValue) && oldValue != incomingValue)
            {
                changes[path] = oldValue;
            }
        }

        return changes;
    }

    /// <summary>
    /// Appends an entry for <paramref name="changes"/> and trims to <see cref="MaxEntries"/> (oldest
    /// dropped), returning the serialized JSON bytes ready to encrypt + store.
    /// </summary>
    public static byte[] BuildUpdatedLog(
        List<ContactMergeLogEntry> existingLog,
        Dictionary<string, string> changes,
        ContactMergeSource by,
        UnixTimeUtc at)
    {
        var log = existingLog ?? new List<ContactMergeLogEntry>();
        log.Add(new ContactMergeLogEntry
        {
            At = at.milliseconds,
            By = by == ContactMergeSource.Enrichment ? "enrichment" : "api",
            Changes = changes
        });

        if (log.Count > MaxEntries)
        {
            log = log.GetRange(log.Count - MaxEntries, MaxEntries);
        }

        return JsonSerializer.SerializeToUtf8Bytes(log, SerializerOptions);
    }

    public static List<ContactMergeLogEntry> Deserialize(byte[] json)
    {
        if (json == null || json.Length == 0)
        {
            return new List<ContactMergeLogEntry>();
        }

        return JsonSerializer.Deserialize<List<ContactMergeLogEntry>>(json.ToStringFromUtf8Bytes(), SerializerOptions)
               ?? new List<ContactMergeLogEntry>();
    }

    /// <summary>
    /// Flattens a contact into <c>jsonPath → value</c>, dropping null/empty leaves.
    /// </summary>
    private static Dictionary<string, string> Flatten(PeerContactContent c)
    {
        var d = new Dictionary<string, string>();
        if (c == null)
        {
            return d;
        }

        void Put(string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                d[key] = value;
            }
        }

        Put("odinId", c.OdinId);

        if (c.Name != null)
        {
            Put("name.displayName", c.Name.DisplayName);
            Put("name.givenName", c.Name.GivenName);
            Put("name.additionalName", c.Name.AdditionalName);
            Put("name.surname", c.Name.Surname);
        }

        if (c.Location != null)
        {
            Put("location.city", c.Location.City);
            Put("location.country", c.Location.Country);
        }

        if (c.Phone != null)
        {
            Put("phone.number", c.Phone.Number);
        }

        if (c.Email != null)
        {
            Put("email.email", c.Email.Email);
        }

        if (c.Birthday != null)
        {
            Put("birthday.date", c.Birthday.Date);
        }

        Put("shortBio", c.ShortBio);
        Put("nickname", c.Nickname);
        Put("status", c.Status);
        Put("link", c.Link);

        if (c.Social != null)
        {
            foreach (var (key, value) in c.Social)
            {
                Put($"social.{key}", value);
            }
        }

        return d;
    }
}
