using System;

namespace Odin.Services.AppNotifications.WebRtcSignaling;

// Response DTOs for notifications written directly to the requesting socket
// (not broadcast via MediatR — these are 1:1 replies, like Pong).

public class CallUnavailableData
{
    public Guid CallId { get; init; }
    public string Reason { get; init; }
}

public class WhoamiResponseData
{
    public string PublicIp { get; init; }
    public int PublicPort { get; init; }
}

public static class CallUnavailableReason
{
    public const string Offline = "offline";
    public const string NotConnected = "not_connected";
    public const string RejectedByServer = "rejected_by_server";
}
