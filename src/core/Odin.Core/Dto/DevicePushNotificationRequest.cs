using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Odin.Core.Dto;
#nullable enable

/// <summary>
/// What a push is for. The one device-independent knob an app sets; the host derives the delivery
/// details from it (PushKindDefaults) and the push relay picks the platform mechanics right before
/// sending (PushRouter). Lives here, next to the relay request, so host and relay share one list.
/// </summary>
public enum PushKind
{
    /// <summary>An ordinary notification. The default.</summary>
    Notify = 0,

    /// <summary>
    /// An incoming call; TagId is the call id. Derives a short TTL, a collapse id from the call id
    /// and time-sensitive delivery. On iOS, when the device registered a PushKit token and the relay
    /// has an APNs key, this is a VoIP push; the app must then report the call to CallKit at once,
    /// or iOS stops delivering VoIP pushes to it. Elsewhere a high-priority alert the app turns into
    /// its own call UI.
    /// </summary>
    Ring = 1,

    /// <summary>
    /// A silent background wake-up with no UI; the app acts on the request in the LiveRelay blob.
    /// iOS throttles these; never use Wake for a call.
    /// </summary>
    Wake = 2,

    /// <summary>
    /// The call with this TagId is over (hung up or answered elsewhere). Derives the same collapse
    /// id as the Ring, so an undelivered or still-showing ring is replaced by this one, and the
    /// same TTL, so a stale "call ended" never arrives on its own.
    /// </summary>
    Hangup = 3,
}

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

    /// <summary>A <see cref="PushKind"/> name; null or absent means Notify. A string on the wire so no serializer setting can change its shape.</summary>
    public string? Kind { get; set; }

    /// <summary>The device's PushKit (VoIP) token, iOS only, if it registered one. Used solely for a Ring.</summary>
    public string? VoipDeviceToken { get; set; }

    public PushKind KindOrNotify() => Enum.TryParse<PushKind>(Kind, ignoreCase: true, out var kind) ? kind : PushKind.Notify;

    /// <summary>
    /// ProblemDetails types the relay answers 502 with, and the host acts on. Both are "the token
    /// is dead", for different tokens: the first drops the whole subscription, the second only the
    /// PushKit token (the FCM one was fine).
    /// </summary>
    public static class ProblemTypes
    {
        /// <summary>Firebase's ErrorCode.NotFound as a string: the FCM token is unregistered.</summary>
        public const string DeviceGone = "NotFound";

        public const string VoipTokenGone = "VoipTokenGone";
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
