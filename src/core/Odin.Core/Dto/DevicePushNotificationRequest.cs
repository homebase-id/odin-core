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
    public string Signature { get; set; } = "";

    [Required]
    public string Id { get; } = Guid.NewGuid().ToString();

    [Required]
    public string Timestamp { get; } = DateTimeOffset.UtcNow.ToString("O");

    [Required]
    public string CorrelationId { get; set; } = "";

    [Required]
    public string Data { get; set; } = "";

    [Required]
    public string Title { get; set; } = "";

    [Required]
    public string Body { get; set; } = "";

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

