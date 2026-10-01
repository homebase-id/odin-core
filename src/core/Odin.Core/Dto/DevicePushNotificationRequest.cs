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
    // host leaves them unset, so host and relay can deploy in either order).

    /// <summary>Seconds the platform may hold the push before discarding it. Null: platform default.</summary>
    public int? TimeToLiveSeconds { get; set; }

    /// <summary>Platform collapse key: a later push with the same id replaces an undelivered earlier one.</summary>
    public string? CollapseId { get; set; }

    /// <summary>Background delivery without an alert (iOS content-available only).</summary>
    public bool Silent { get; set; }

    /// <summary>Ask for a time-sensitive interruption level where the platform supports it (iOS).</summary>
    public bool TimeSensitive { get; set; }

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

