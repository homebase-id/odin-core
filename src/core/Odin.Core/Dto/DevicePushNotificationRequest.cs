using System;
using System.Collections.Generic;

namespace Odin.Core.Dto;

// Version 1
public class DevicePushNotificationRequestV1
{
    public int Version { get; } = 1;

    // Backend stuff
    public string DeviceToken { get; set; } = "";
    public string OriginDomain { get; set; } = "";
    public string Signature { get; set; } = "";

    // Client stuff
    public string Id { get; } = Guid.NewGuid().ToString();
    public string Timestamp { get; } = DateTimeOffset.UtcNow.ToString("O");
    public string CorrelationId { get; set; } = "";
    public string Data { get; set; } = "";

    //

    public Dictionary<string, string> ToClientDictionary()
    {
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

