using System;
using Odin.Core.Identity;

namespace Odin.Services.AppNotifications.WebRtcSignaling;

// Inbound payload classes for the call.* socket commands. The server treats sdp/candidate
// as opaque strings — they're handed straight back to the recipient client.

public class CallInvitePayload
{
    public Guid CallId { get; init; }
    public OdinId To { get; init; }
}

public class CallOfferPayload
{
    public Guid CallId { get; init; }
    public OdinId To { get; init; }
    public string Sdp { get; init; }
}

public class CallAnswerPayload
{
    public Guid CallId { get; init; }
    public OdinId To { get; init; }
    public string Sdp { get; init; }
}

public class CallIcePayload
{
    public Guid CallId { get; init; }
    public OdinId To { get; init; }
    public string Candidate { get; init; }
    public string SdpMid { get; init; }
    public int? SdpMLineIndex { get; init; }
}

public class CallHangupPayload
{
    public Guid CallId { get; init; }
    public OdinId To { get; init; }
}

public class CallRejectPayload
{
    public Guid CallId { get; init; }
    public OdinId To { get; init; }
}
