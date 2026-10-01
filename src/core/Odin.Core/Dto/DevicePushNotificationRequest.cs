using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Odin.Core.Dto;
#nullable enable

// Version 1
public class DevicePushNotificationRequestV1
{
    public int Version { get; } = 1;

    [Required]
    public string DevicePlatform { get; set; } = "";

    [Required]
    public string DeviceToken { get; set; } = "";

    [Required]
    public string OriginDomain { get; set; } = "";

    [Required]
    public byte[] Signature { get; set; } = Array.Empty<byte>();

    [Required] public string Id { get; set; } = "";

    [Required]
    public string Timestamp { get; set; } = "";

    [Required]
    public string CorrelationId { get; set; } = "";

    [Required]
    public string Data { get; set; } = "";

    [Required]
    public string Title { get; set; } = "";

    [Required]
    public string Body { get; set; } = "";

    [Required]
    public string FromDomain { get; set; } = "";

    [Required]
    public string ToDomain { get; set; } = "";

    // Delivery options (optional, additive to version 1; an older relay ignores them and an older
    // host leaves them unset, so host and relay can deploy in either order). The bounds live here
    // because the host validates what it enqueues and the relay validates what it receives, and
    // this DTO is the one type both can see.

    /// <summary>A day. Longer than that and the push is not time-bound; leave TTL unset instead.</summary>
    public const int MaxTimeToLiveSeconds = 86400;

    /// <summary>The APNs limit for apns-collapse-id.</summary>
    public const int MaxCollapseIdLength = 64;

    /// <summary>Seconds the platform may hold the push before discarding it. Null: platform default.</summary>
    public int? TimeToLiveSeconds { get; set; }

    /// <summary>Platform collapse key: a later push with the same id replaces an undelivered earlier one.</summary>
    public string? CollapseId { get; set; }

    /// <summary>Background delivery without an alert (iOS content-available only).</summary>
    public bool Silent { get; set; }

    /// <summary>Ask for a time-sensitive interruption level where the platform supports it (iOS).</summary>
    public bool TimeSensitive { get; set; }

    /// <summary>
    /// What the push is for: one of <see cref="Kinds"/>. Null or absent means Notify. The relay
    /// uses it to choose the platform push type right before sending; the host never does.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// The device's PushKit (VoIP) token, iOS only, if it registered one. A different token than
    /// <see cref="DeviceToken"/>. Used solely for a <see cref="Kinds.Ring"/>: with an APNs key
    /// configured on the relay, the ring goes out as a VoIP push to this token instead of an
    /// alert to the FCM token.
    /// </summary>
    public string? VoipDeviceToken { get; set; }

    /// <summary>The <see cref="Kind"/> values. Names, not numbers, so the relay reads them without the host's enum.</summary>
    public static class Kinds
    {
        public const string Notify = "Notify";
        public const string Ring = "Ring";
        public const string Wake = "Wake";
        public static readonly string[] All = [Notify, Ring, Wake];
    }

    //

    public Dictionary<string, string> ToClientDictionary()
    {
        // This is the data that will be sent to the client
        return new Dictionary<string, string>
        {
            { "correlationId", CorrelationId },
            { "id", Id },
            { "data", Data },
            { "timestamp", Timestamp},
            { "version", Version.ToString() },
        };
    }
}

